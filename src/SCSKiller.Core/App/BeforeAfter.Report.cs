namespace SCSKiller.Core.App;

/// <summary>One line of the report's details: the two runs' values and an optional note beside them.</summary>
public sealed record ReportRow(string Label, string Before, string After, string? Note = null);

/// <summary>The report's words (doc 6.1) as both apps show them: the card's subtitle and three tiles, the details, the notes.</summary>
public sealed record ReportText(string Subtitle, IReadOnlyList<(string Label, string Value)> Tiles, IReadOnlyList<ReportRow> Rows, IReadOnlyList<string> Notes);

public static partial class BeforeAfter
{
    public static ReportText Report(string game, BeforeAfterResult r, GpuVendor vendor)
    {
        var c = r.Compare;
        string subtitle = c.Dx11 ? Dx11Label
            : c.AlreadySmooth ? "这台电脑本来就流畅：编译前着色器编译卡顿就很少"
            : "SCSKiller 编译其着色器前后的着色器编译卡顿对比";
        // DirectX 11: no ready share and no shader split, every frame of 50 ms or more
        (string, string)[] tiles = c.Dx11
            ? [("卡顿", $"{c.Before.Hitches:N0}  →  {c.After.Hitches:N0}"),
               ("卡顿累计时长", $"{c.Before.HitchSeconds:0.0} s  →  {c.After.HitchSeconds:0.0} s"),
               ("最差卡顿", $"{Ms(c.Before.WorstHitchMs)}  →  {Ms(c.After.WorstHitchMs)}")]
            : [("着色器编译卡顿", $"{c.Before.ShaderStutters:N0}  →  {c.After.ShaderStutters:N0}"),
               ("缓存就绪占比", $"{Pct(c.Before.ReadyShare)}  →  {Pct(c.After.ReadyShare)}"),
               ("最差卡顿", $"{Ms(c.Before.WorstStutterMs)}  →  {Ms(c.After.WorstStutterMs)}")];

        string Count(long n, RunStats run) => $"{n:N0}" + (c.PerTenMinutes && run.PlaySeconds > 0 ? $"  （每 10 分钟 {n * 600 / run.PlaySeconds:0.#} 次）" : "");
        var rows = new List<ReportRow> { new("游戏时长", Clock(c.Before.PlaySeconds), Clock(c.After.PlaySeconds)) };
        if (c.Dx11)
        {
            rows.Add(new("50 ms 以上的卡顿", Count(c.Before.Hitches, c.Before), Count(c.After.Hitches, c.After)));
            rows.Add(new("由此冻结的时间", $"{c.Before.HitchSeconds:0.0} s", $"{c.After.HitchSeconds:0.0} s"));
            rows.Add(new("最差卡顿", Ms(c.Before.WorstHitchMs), Ms(c.After.WorstHitchMs)));
        }
        else
        {
            rows.Add(new("着色器编译卡顿", Count(c.Before.ShaderStutters, c.Before), Count(c.After.ShaderStutters, c.After)));
            rows.Add(new("缓存就绪占比", Pct(c.Before.ReadyShare), Pct(c.After.ReadyShare)));
            // state objects aren't in the ready share
            if (r.Before.Creates is { } bc && r.After.Creates is { } ac && bc.StateObjectsReady + bc.StateObjectsCompiled + ac.StateObjectsReady + ac.StateObjectsCompiled > 0)
                rows.Add(new("光追缓存就绪占比", Rt(bc), Rt(ac)));
            rows.Add(new("游戏中即时编译的管线", Count(c.Before.PlayCompiles, c.Before), Count(c.After.PlayCompiles, c.After)));
            rows.Add(new("被着色器编译卡顿冻结的时间", $"{c.Before.FrozenSeconds:0.0} s", $"{c.After.FrozenSeconds:0.0} s"));
            rows.Add(new("最差着色器编译卡顿", Ms(c.Before.WorstStutterMs), Ms(c.After.WorstStutterMs)));
        }
        rows.Add(new("1% 低帧", $"{c.Before.Low1PctFps:0} fps", $"{c.After.Low1PctFps:0} fps"));
        rows.Add(new("平均帧率", $"{c.Before.AvgFps:0} fps", $"{c.After.AvgFps:0} fps",
            "1% 低帧和平均帧率从游戏第一帧起计（含菜单和加载），仅启动更快也会拉高数值。"));
        if (!c.Dx11)
            rows.Add(new("其他卡顿（与着色器无关）", Count(c.Before.OtherHitches, c.Before), Count(c.After.OtherHitches, c.After),
                "流式加载卡顿在第二次运行时可能减少：Windows 会把游戏文件保留在内存中。"));

        var notes = new List<string>(c.Notes);
        if (c.Dx11) notes.Add("DirectX 11 下加载画面上的卡顿也会计入，且流式加载卡顿在第二次运行时可能减少。");
        if (c.AlreadySmooth) notes.Add($"{game} 在这台电脑上编译前就几乎没有着色器卡顿。");
        // the D3D11 recorder records input layouts on AMD only, frame times only on NVIDIA
        notes.Add(c.Dx11 && vendor != GpuVendor.Amd ? "依据游戏文件编译。" : "依据游戏文件和你“之前”一场的录制编译。");
        notes.Add("帧时间与本报告仅保存在你的电脑上，除非你分享图片。");
        return new(subtitle, tiles, rows, notes);
    }

    /// <summary>The card's corner line: the app and, with the GPU shown, its driver (a driver version names the vendor).</summary>
    public static string Versions(string? driver) => $"SCSKiller {AppVersion.Current}" + (driver == null ? "" : $"  ·  驱动 {driver}");

    /// <summary>The card's footer: the play, the swap chain's size (all SCSKiller knows of the settings) and, when shown, the GPU.</summary>
    public static string Footer(BeforeAfterResult r, string? gpu)
    {
        var (b, a) = PlayMinutes(r.Compare);
        string? size = Size(r.Before.SwapChain) is { } s && s == Size(r.After.SwapChain) ? s : null;
        return string.Join("  ·  ", new[] { b == a ? $"各自游戏 {b} 分钟" : $"分别游戏 {b} 和 {a} 分钟", "同一台电脑", size, gpu }.OfType<string>());
    }

    /// <summary>"2560×1440" from the csv's swap chain line ("2560x1440 fmt 28").</summary>
    static string? Size(string? swapChain) => swapChain?.Split(' ')[0].Replace('x', '×');

    /// <summary>"5:00": minutes and seconds.</summary>
    public static string Clock(double seconds) => $"{(int)(seconds / 60)}:{(int)seconds % 60:00}";

    // neither composer takes an image from a link: the apps copy it and say to paste it
    public static string PostOnX(string shareText) =>
        $"https://x.com/intent/post?text={Uri.EscapeDataString(WithoutLink(shareText))}&url={Uri.EscapeDataString(Link)}";

    // a text post: a link post has no body to paste the image into
    public static string PostOnReddit(string shareText) =>
        $"https://www.reddit.com/submit?selftext=true&title={Uri.EscapeDataString(WithoutLink(shareText))}&text={Uri.EscapeDataString(Link)}";

    /// <summary>X shows the url param as the link: the text's own trailing link would show twice.</summary>
    static string WithoutLink(string text)
    {
        text = text.TrimEnd();
        foreach (var link in new[] { Link, "scskiller.com" })
            if (text.EndsWith(link, StringComparison.OrdinalIgnoreCase)) return text[..^link.Length].TrimEnd();
        return text;
    }

    /// <summary>The save picker's suggested name, without the characters a file name can't hold.</summary>
    public static string ImageName(string game) =>
        string.Join(" ", $"{game} 之前之后".Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();

    static string Rt(CreateStats c) => $"{c.StateObjectsReady:N0} / {c.StateObjectsReady + c.StateObjectsCompiled:N0}";
    static string Pct(double? share) => share is { } s ? $"{s * 100:0}%" : Format.Dash;
    static string Ms(double ms) => ms <= 0 ? "无" : ms >= 1000 ? $"{ms / 1000:0.0} s" : $"{ms:0} ms";
}
