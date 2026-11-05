using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using SCSKiller.Core.App;
using SCSKiller.Ui.Controls;
using SCSKiller.Ui.Models;

namespace SCSKiller.Ui.Pages;

/// <summary>The before and after recording's dialogs, which the game page opens; the actions behind them are
/// <see cref="BeforeAfterUi"/>'s.</summary>
static class BeforeAfterDialogs
{
    public enum Intro { Start, StartOver, HowItWorks }

    /// <summary>Screenshots: the intro dialog while it's open.</summary>
    public static ContentDialog? Showing { get; private set; }

    /// <summary>The intro (doc 6.5): the steps (folded behind "How it works" once seen), the play time and the sound.
    /// Null = cancelled, or <see cref="Intro.HowItWorks"/>. <paramref name="frames11"/>: the pair puts a frames-only
    /// d3d11.dll into the game (<see cref="ScsKiller.Frames11"/>). <paramref name="on"/>: the window it shows over, the
    /// main one by default. <paramref name="max"/>: Max compile is offered for the after run.</summary>
    public static async Task<(int Minutes, bool Sound, bool Overlay, bool Max)?> IntroAsync(string game, Intro mode, bool frames11 = false, DialogLayer? on = null, bool max = false)
    {
        var s = Host.Core.Settings;
        bool full = mode == Intro.HowItWorks || !s.BeforeAfterIntroSeen;
        int minutes = 5;
        static TextBlock Line(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
        var (step1, step3) = (Line(""), Line(""));
        void Steps() => (step1.Text, step3.Text) = (
            $"1. SCSKiller 会删除该游戏的着色器缓存（{(Host.Platform.SystemShaderCache ? "驱动、Windows 和" : "驱动和")}游戏自己的）并启动游戏。读取一个之后还能再次读取的存档并游玩。{minutes} 分钟从游戏的第一帧开始计算，包括加载和暂停。",
            $"3. 读取同一个存档，再游玩同样的 {minutes} 分钟。");
        Steps();
        var steps = new StackPanel
        {
            Spacing = 8, IsVisible = full,
            Children = { step1, Line("2. 关闭游戏。SCSKiller 会再次删除这些缓存并编译该游戏的着色器。"), step3,
                Line("4. 你会得到一份可以保存和分享的前后对比报告。") },
        };
        var how = new HyperlinkButton { Content = "工作原理", Padding = new(0), IsVisible = !full };
        how.Click += (_, _) => (steps.IsVisible, how.IsVisible) = (true, false);

        var group = $"play-time-{Guid.NewGuid():N}";
        var (five, ten) = (new RadioButton { Content = "5 分钟", GroupName = group, IsChecked = true }, new RadioButton { Content = "10 分钟", GroupName = group });
        ten.IsCheckedChanged += (_, _) => { minutes = ten.IsChecked == true ? 10 : 5; Steps(); };
        var time = new StackPanel
        {
            Spacing = 8,
            Children = { new TextBlock { Text = "游玩时长" }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { five, ten } } },
        };
        var sound = new CheckBox { Content = "时间到时播放提示音", IsChecked = s.BeforeAfterSound };
        var overlay = new CheckBox { Content = "在游戏中显示倒计时", IsChecked = s.BeforeAfterOverlay };
        ToolTip.SetTip(overlay, "游戏右上角的一个小面板：剩余时间、你的帧时间，以及在 DirectX 12 和 Vulkan 上的每次着色器编译卡顿。无论是否显示，SCSKiller 都会在此倒计时。开启 FSR 或 XeSS 帧生成时不显示。");
        var maxRun = new CheckBox { Content = "为第二次运行进行极限编译", IsChecked = s.BeforeAfterMax, IsVisible = max };
        ToolTip.SetTip(maxRun, DetailVm.MaxCompileWhy);
        var checks = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { sound, overlay, maxRun } };
        var choices = new Grid { ColumnSpacing = 24, ColumnDefinitions = new("*,Auto"), Children = { time, checks }, IsVisible = mode != Intro.HowItWorks };
        Grid.SetColumn(checks, 1);

        static TextBlock Bullet(string text, bool shown = true) => new() { Text = "• " + text, TextWrapping = TextWrapping.Wrap, Classes = { "Secondary" }, IsVisible = shown };
        var dialog = new ContentDialog
        {
            Title = mode == Intro.StartOver ? $"重新开始“{game}”的录制？" : "“之前/之后”录制",
            Content = new StackPanel
            {
                Spacing = 16, [!Layoutable.MaxWidthProperty] = new DynamicResourceExtension("TextFit.560"),
                Children =
                {
                    new InfoBar
                    {
                        IsClosable = false, Severity = InfoBarSeverity.Warning,
                        Message = "这还会删除游戏自带的着色器缓存（可能达 2 GB，没有备份）。游戏会自行重建，可能需要几分钟。",
                    },
                    steps, how, choices,
                    new StackPanel
                    {
                        Spacing = 6,
                        Children =
                        {
                            Bullet("两次运行之间不要更改画面设置。"),
                            Bullet("该游戏当前的编译结果会被删除，第 2 步会重新编译。"),
                            Bullet("录制期间会在游戏旁临时放置一个小的 d3d11.dll，结束后移除。", frames11),
                            Bullet("帧时间仅保存在本机。若开启共享，只会照常发送匿名的哈希。"),
                        },
                    },
                },
            },
            CloseButtonText = mode == Intro.HowItWorks ? "关闭" : "取消",
            PrimaryButtonText = mode == Intro.HowItWorks ? "" : "开始“之前”一场",
            DefaultButton = mode == Intro.HowItWorks ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        Showing = dialog;
        var result = await App.ShowAsync(dialog, on);
        Showing = null;
        if (mode == Intro.HowItWorks || result != ContentDialogResult.Primary) return null;
        Host.Core.Settings = Host.Core.Settings with { BeforeAfterIntroSeen = true, BeforeAfterSound = sound.IsChecked == true, BeforeAfterOverlay = overlay.IsChecked == true, BeforeAfterMax = max ? maxRun.IsChecked == true : s.BeforeAfterMax };
        return (minutes, sound.IsChecked == true, overlay.IsChecked == true, max && maxRun.IsChecked == true);
    }

    /// <summary>The intro, then the before run: the caches cleared and the game started through its store. The error to
    /// show, or null (also when cancelled).</summary>
    public static async Task<string?> StartAsync(string id, bool over = false, DialogLayer? on = null)
    {
        var g = Host.Core.Games.First(x => x.Game.Id == id);
        bool frames11 = ScsKiller.Frames11(g.Engine, Host.Core.Vendor.Caps);
        bool max = ScsKiller.MaxCompileAdds(Host.Core.Vendor.Caps, g.Engine);
        if (await IntroAsync(g.Game.Name, over ? Intro.StartOver : Intro.Start, frames11, on, max) is not { } choice) return null;
        return await BeforeAfterUi.BeginAsync(id, choice.Minutes, choice.Overlay, over, choice.Max);
    }

    public static async Task<string?> RecordAfterAgainAsync(string id, DialogLayer? on = null)
    {
        var dialog = new ContentDialog
        {
            Title = "重新录制“之后”一场？",
            Content = "SCSKiller 会先清除缓存并重新编译，避免上一场的结果影响新一场。然后请再次读取同一个存档游玩。",
            PrimaryButtonText = "重新录制", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary,
        };
        if (await App.ShowAsync(dialog, on) != ContentDialogResult.Primary) return null;
        return await BeforeAfterUi.RecordAfterAgainAsync(id);
    }

    /// <summary>Cancel in progress: the caches were cleared, so "Compile now" is the default.</summary>
    public static async Task<string?> CancelAsync(string id)
    {
        var dialog = new ContentDialog
        {
            Title = "取消“之前/之后”录制？",
            Content = "其着色器缓存已被清除，在重新编译之前游戏会一直卡顿。",
            PrimaryButtonText = "立即编译", SecondaryButtonText = "不编译", CloseButtonText = "继续录制", DefaultButton = ContentDialogButton.Primary,
        };
        var result = await App.ShowAsync(dialog);
        if (result is not (ContentDialogResult.Primary or ContentDialogResult.Secondary)) return null;
        return await BeforeAfterUi.EndAsync(id, compile: result == ContentDialogResult.Primary);
    }
}
