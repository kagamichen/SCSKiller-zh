using System.Globalization;
using SCSKiller.Core;
using SCSKiller.Core.App;
using SCSKiller.Core.Games;
using SCSKiller.Core.Vendors;

namespace SCSKiller.Ui.Models;

/// <summary>The status card's icon, which the UI draws from its icon font.</summary>
public enum StatusIcon { Warning, Warmed, Ready, Record, Blocked }

public sealed class DetailVm(string id) : Bindable
{
    GameState s = Host.Core.Games.First(g => g.Game.Id == id);
    public GameRow Row { get; private set; } = new(Host.Core.Games.First(g => g.Game.Id == id));
    public string? Error { get; set; }
    public bool HasError => Error != null;

    public void Refresh()
    {
        place = InQueue(id);
        var now = Host.Core.Games.FirstOrDefault(g => g.Game.Id == id);
        if (now != null && s.Playing && !now.Playing) ReadCaches();   // the game exited: its Windows and pipeline caches grew
        if (now != null && (now != s || Row.Icon != Host.Icons.Get(now.Game.ExePath))) { s = now; Row = new GameRow(now); }   // or its icon failed: the tile
        ReadBlocked();
        ReadSteamCacheNote();
        Changed();
    }

    static string Sentence(string t) => t.Length > 0 ? char.ToUpperInvariant(t[0]) + t[1..] : t;
    bool Partial => ScsKiller.IsPartial(s.Plan);
    bool KeyMissing => s.Engine?.KeyMissing == true;
    /// <summary>Needs a recording for its ray tracing only: the rest compiles (a partial compile, offered but not the primary action).</summary>
    bool RtPartial => s.Status == GameStatus.NeedsRecording && ScsKiller.NeedsRtRecording(s);
    /// <summary>Compiled, and a recording would add its ray tracing: the record tip, not the warning callout.</summary>
    bool RtAfter => ScsKiller.RtAfterRecording(s);
    public bool NoAntiCheat => !s.AnyAntiCheat;
    /// <summary>The recorder may go next to the game: no anti-cheat, not a game added by hand whose folder isn't confirmed, and
    /// not one whose first d3d12.dll Streamline loads on NVIDIA.</summary>
    bool CanRecord => NoAntiCheat && !s.RootUnconfirmed && !s.StreamlineFirst;

    public string Name => s.Game.Name;
    public string Sub => $"{Fmt.FileName(s.Game.ExePath)} · {Fmt.StoreName(s.Game)}";   // engine and API are tags

    // Status: the state in its colour and why; the actions sit beside it.
    public StatusIcon StatusIcon => s.Status switch
    {
        _ when s.CompileUnreached || ScsKiller.IsPartlyCompiled(s) || ScsKiller.NeedsSteamStart(s) => StatusIcon.Warning,
        GameStatus.Warmed => StatusIcon.Warmed, GameStatus.Ready => StatusIcon.Ready, GameStatus.NeedsRecording => StatusIcon.Record,
        GameStatus.Stale => StatusIcon.Warning, _ => StatusIcon.Blocked,
    };
    // The queue page's progress for this game: its number in the run order while it waits, its stage and progress while it runs.
    (QueueItem? Item, int Number) place = InQueue(id);
    static (QueueItem?, int) InQueue(string id)
    {
        var (running, waiting) = Fmt.RunOrder(Host.Core.Queue);
        if (running?.GameId == id) return (running, 1);
        int i = waiting.FindIndex(q => q.GameId == id);
        return i < 0 ? (null, 0) : (waiting[i], i + (running != null ? 2 : 1));
    }
    /// <summary>A warm's tick: only the progress changes, unless the item started, finished or left the queue.</summary>
    public void RefreshProgress()
    {
        var was = place.Item?.Stage;
        place = InQueue(id);
        if (place.Item?.Stage != was) Refresh();
        else Changed(nameof(StatusTitle), nameof(StatusReason), nameof(Percent), nameof(Indeterminate));
    }
    QueueItem? Running => place.Item is { } q && Format.Running(q) ? q : null;
    public bool IsCompiling => Running != null;
    public double Percent => Fmt.Percent(Running);
    public bool Indeterminate => Fmt.Indeterminate(Running);

    public string StatusTitle => Running is { } r ? Fmt.Stage(r) : place.Item != null ? $"已在队列中（第 {place.Number} 位）"
        : Fmt.FoldingGames.Contains(id) ? Format.MergingStage : Row.StatusText;
    public string StatusReason => Running is { } r && (Fmt.ProgressText(r) is { } p
            ? p + (r.Progress is { PerSecond: > 0 } && Fmt.Left(r, s) is { } t ? $" · 剩余 {Format.Duration(t)}" : "") + (Fmt.Started(r) is { } at ? " · " + at : "")
            : Fmt.Started(r)) is { } line
        ? Sentence(line) + (Format.ProcessLine(r, s.Game) is { } shown ? "\n" + shown : "")
        : place.Item is { Stage: QueueStage.Waiting, Note: { } n } ? Sentence(n)
        : Fmt.FoldLine(id) ?? GameReason;
    string GameReason => Sentence(s.Status switch
    {
        _ when ScsKiller.CacheOffRefused(Host.Core.Queue, id) => ScsKiller.CacheOffReason,
        _ when s.CompileUnreached => s.StatusReason + (s.WarmedAt != null ? "。" + Sentence(ScsKiller.ClearForGameNote) : ""),
        _ when ScsKiller.IsPartlyCompiled(s) => ScsKiller.RefusedNote(s) + "。SCSKiller 的更新可能会修复它",
        GameStatus.Warmed when ScsKiller.IsPartlyWarmed(s) => s.StatusReason,
        GameStatus.Warmed => (Partial || RtAfter || KeyMissing ? "" : "缓存已完成，") + $"已按{(ScsKiller.BothApis(s.Plan) ? "DirectX 11 和 DirectX 12、" : "")}驱动 {s.WarmedDriverVersion} 编译" + (s.WarmedAt is { } t ? $"（{t.LocalDateTime:g}）" : "")
            + (RtAfter ? "。" + Sentence(ScsKiller.RtAfterRecordingNote) : "") + (KeyMissing ? "。" + Sentence(ScsKiller.KeyForFullCoverage) : ""),
        // a cache here: played since a clear or a stopped compile; then the planner's note when the row has one to say
        GameStatus.Ready => (s.CompileFailed ? "上次编译失败" + (s.CompileFailedWhy is { } why ? "。" + Sentence(why.TrimEnd('.')) : "")
            : s.CacheOnDisk > 0 ? $"尚未完全编译：其中 {Format.Bytes(s.CacheOnDisk)} 已在驱动缓存中"
            : s.LastWarmTime == null ? "尚未编译" : "其着色器缓存为空：请重新编译")
            + (Row.Note != null ? "。" + Sentence(s.StatusReason) : ""),
        GameStatus.NeedsRecording => HasDbTeaser ? NoDbNote(Row.FullNote) : Row.FullNote,   // the teaser says it
        _ when ScsKiller.AntiCheatHeld(s) => ScsKiller.HeldAntiCheatNote,
        _ when s.Engine?.Encrypted == true => "需要该游戏的 AES 密钥",   // the title: "Encrypted game files"
        _ => HasDbTeaser ? NoDbNote(s.StatusReason) : s.StatusReason,
    })
        + (IsPartlyCompiled || s.CompileUnreached ? "。" : "") + Format.ModNote(s);
    static string NoDbNote(string r) => r.Replace(". " + Sentence(ScsKiller.InDbSupportersNote), "").Replace(". " + Sentence(ScsKiller.InDbNote), "");
    public bool IsPartlyCompiled => ScsKiller.IsPartlyCompiled(s);
    public bool NeedsAesKey => ScsKiller.NeedsAesKey(s);
    public bool HasAesKey => Host.Core.HasEncryptionKey(id);
    public bool CanChangeAesKey => HasAesKey && !NeedsAesKey;
    public bool NeedsGameKey => ScsKiller.NeedsGameKey(s);
    public const string RemoveAesKeyNote = "该密钥已从此电脑删除。已编译的缓存会保留。下次编译的方案将不包含该密钥解锁的着色器；如果游戏的 exe 中自带该密钥，SCSKiller 会再次找到它。";
    public string PartlyCompiledWhy => s.LastWarmFailedBy switch
    {
        0 or 1 => $"SCSKiller 编译了此游戏，但显卡驱动拒绝了其中很大一部分。当驱动或 {Host.Platform.Runtime} 无法按游戏的方式构建管线时就会发生这种情况。被拒绝的管线会在游戏首次用到时由游戏自行构建，卡顿也就发生在那时。驱动更新后 SCSKiller 会重新编译它们。",
        2 => "SCSKiller 编译了此游戏，但无法从游戏文件构建其中很大一部分。这些管线会在游戏首次用到时由游戏自行构建，卡顿也就发生在那时。",
        _ => "SCSKiller 编译了此游戏，但其中很大一部分无法编译。这些管线会在游戏首次用到时由游戏自行构建，卡顿也就发生在那时。驱动更新后 SCSKiller 会重新编译它们。",
    };

    // The manifest is public: a PC without "db" sees that the community database covers the game, and where to get it.
    // Shown, it is the page's only mention of the database.
    public bool HasDbTeaser => s is { InCommunityDb: true, Community: null } && Host.Account?.HasDb != true
        && (s.Status == GameStatus.NeedsRecording || RtAfter || s.Status == GameStatus.Unsupported && !NoAntiCheat && (s.StatusReason.StartsWith("needs a recording, which", StringComparison.Ordinal) || ScsKiller.AntiCheatHeld(s) || s.StatusReason.StartsWith(ScsKiller.AntiCheatNoRecordingStart, StringComparison.Ordinal)));
    public string DbTeaser => $"社区数据库中有其他玩家录制的 {s.CommunityDbPsos:N0} 条管线{(s.CommunityDbPsos == 1 ? "" : "")}。Patreon 支持者无需录制即可编译。";
    public string DbTeaserLink => Host.Account?.SignedIn == true ? "Patreon 会员" : "使用 Patreon 登录";
    public bool CanCompile => s.Status is GameStatus.Ready or GameStatus.Warmed or GameStatus.Stale || RtPartial;
    public bool ShowMaxCompile => CanCompile && ScsKiller.MaxCompileAdds(Host.Core.Vendor.Caps, s.Engine);
    public bool ShowRtOnly => CanCompile && !RtPartial && ScsKiller.RtOnlyAdds(s);
    public const string MaxCompileWhy = "编译此游戏着色器的每一种组合，而不是每种只编译一次。耗时更长、占用更多磁盘空间，但游玩时可能需要编译的着色器更少。似乎对 AMD 帮助最大。";
    public string? GuessNote => s.Engine is { } e && s.Status is GameStatus.Ready or GameStatus.Warmed or GameStatus.Stale ? ScsKiller.GuessNote(e) : null;
    public bool HasGuessNote => GuessNote != null;
    public string? DeliveryLine => ScsKiller.DeliveryLine(s);
    public bool HasDeliveryLine => DeliveryLine != null;
    // read live: a game's state keeps the flag until its next refresh
    public string? CleanupLine => D3DSCache.CleanupKept() == true ? null : ScsKiller.CleanupLine(s);
    public bool HasCleanupLine => CleanupLine != null;
    public bool CanKeepShaders { get; private set; } = true;
    public async Task KeepShadersAsync()
    {
        CanKeepShaders = false;
        Changed();
        var r = await SettingsVm.KeepShadersAsync(true);
        CanKeepShaders = true;
        if (r is { Ok: false }) { Error = "未更改：" + r.Message; Changed(); }
        else RefreshGame();
    }
    public string? VulkanRtNote => ScsKiller.VulkanRtNote(s, GpuBackends.OwnDriver(Host.Core.Vendor));
    public bool HasVulkanRtNote => VulkanRtNote != null;
    // warmed: nothing urgent, unless only partly (the careful compile is offered); RT: Record comes first; no shader stutter: not needed
    public bool CompileAccent => (OffersCareful || s.Status != GameStatus.Warmed && !RtPartial) && s.NoStutter == null;
    bool Queued => Host.Core.Queue.Any(q => q.GameId == id && Fmt.Active(q) && !q.PlanCheck);
    public bool CanAdd => !Queued && !BaInProgressAny;
    /// <summary>A pair before Done: a compile or clear of the user's own would spoil a run (Core refuses them too).</summary>
    bool BaInProgressAny => s.BeforeAfter is { Stage: not BeforeAfterStage.Done };
    const string BaBusyTip = "前后录制正在进行中";
    public string? CompileTip => BaInProgressAny ? BaBusyTip : null;
    public bool CanClearCache => HasCache && !BaInProgressAny;
    bool CompilingSoon => Fmt.CompilingSoon(Host.Core.Queue, id);
    public bool ShowPlay => Row.CanLaunch;
    public bool CanPlay => !s.Playing && !CompilingSoon;
    public string PlayText => s.Playing ? "运行中" : "启动";
    public string PlayTip => s.Playing ? $"{s.Game.Name} 正在运行" : CompilingSoon ? Fmt.CompilingTip : Fmt.PlayVia(s.Game);
    public bool IsManual => s.Game.Store == Store.Manual;
    public bool CanChooseExe => !IsManual && Collections.Member(s.Game.Id) == null;
    public bool ExeChosen => CanChooseExe && s.ExeChosen;
    public string RemoveTip => IsManual ? "忘记此游戏。其记录器会离开游戏文件夹，游戏自身的文件会保留。"
        : "隐藏此游戏。其文件、存档和已编译的缓存会保留。";
    /// <summary>The confirmation's text for a store's game, which is hidden.</summary>
    public string HideNote => "它不会再出现在列表中，也不会再被自动编译。其文件、存档和已编译的缓存会保留。在游戏库页面显示已隐藏项即可找回。"
        + (BaInProgressAny ? "进行中的前后录制将被取消。" : "")
        + (s.RecorderInstalled || s.RecorderUnarmed ? "记录器会先关闭并离开游戏文件夹。" : "");
    public async void Play()
    {
        Error = await BeforeAfterUi.PlayAsync(s);
        Changed();
    }
    // Before and after recording (doc 6.5): one button in the status card, its note under it, a menu beside it once started
    // off the UI thread: it loads game records and lists processes. Until the first read is in, the button is off with no note
    string? blocked;
    bool blockedKnown;
    BackgroundRead<string?>? blockedRead;
    void ReadBlocked() => (blockedRead ??= new("game blocked", () => Blocked(id), b =>
    {
        if (blockedKnown && b == blocked) return;
        (blocked, blockedKnown) = (b, true);
        Changed();
    })).Request();
    static string? Blocked(string id)
    {
        if (!Host.Core.Settings.BeforeAfter) return null;
        try
        {
            var why = Host.Core.BeforeAfterBlocked(id);
            return why != null && why == Host.Core.Games.First(g => g.Game.Id == id).StatusReason ? Sentence(why) : why;   // a status reason starts lowercase
        }
        catch (Exception e) { return BeforeAfterUi.Describe(e); }
    }
    BeforeAfterInfo? Ba => s.BeforeAfter;
    (bool Shown, bool Enabled, string Note) BaStart => BeforeAfter.StartButton(Host.Core.Settings.BeforeAfter, blockedKnown ? blocked : "");
    public bool ShowBa => Ba != null ? Host.Core.Settings.BeforeAfter : BaStart.Shown;
    public bool BaHasMenu => ShowBa && Ba != null;
    public bool BaVoided => BeforeAfterUi.Voided(Ba);
    public bool BaInProgress => Ba is { Stage: not BeforeAfterStage.Done };   // voided too: Cancel stays
    public bool BaDone => Ba is { Stage: BeforeAfterStage.Done } && !BaVoided;
    bool BaLaunchable => BeforeAfterUi.Launchable(s);
    public string BaText => Ba switch
    {
        null => "前后录制",
        _ when BaVoided => "重新开始",
        { Stage: BeforeAfterStage.BeforeArmed, ClearPending: true } => "清除缓存后重试",
        { Stage: BeforeAfterStage.BeforeArmed or BeforeAfterStage.AfterArmed } => BaLaunchable ? $"启动 {Name}" : $"像平常一样启动 {Name}",
        { Stage: BeforeAfterStage.BeforePlaying or BeforeAfterStage.AfterPlaying } p => p.Minutes * 60 - p.PlayedSeconds is var left and > 0
            ? $"前后录制：游玩中，剩余 {(int)left / 60}:{(int)left % 60:00}" : "前后录制：时间到，请关闭游戏",
        { Stage: BeforeAfterStage.Compiling or BeforeAfterStage.AfterReady } => "继续进行后段录制",
        _ => "查看前后录制",
    };
    public bool BaEnabled => Ba?.Stage switch
    {
        null => BaStart.Enabled,
        _ when BaVoided || Ba!.ClearPending => !s.Playing,
        BeforeAfterStage.BeforeArmed or BeforeAfterStage.AfterArmed => BaLaunchable && !s.Playing,
        BeforeAfterStage.AfterReady => !s.Playing,
        BeforeAfterStage.Done => true,
        _ => false,
    };
    public bool BaAccent => BaVoided || Ba?.Stage is BeforeAfterStage.BeforeArmed or BeforeAfterStage.AfterArmed or BeforeAfterStage.AfterReady;
    public string? BaTip => Ba == null ? blocked ?? "冷启动测量此游戏、编译、再次测量，并得到一份可分享的报告"
        : Ba.Stage is BeforeAfterStage.BeforeArmed or BeforeAfterStage.AfterArmed && !BaLaunchable ? $"像平常一样启动 {Name}：SCSKiller 会检测到它启动" : null;
    public string BaNote => Ba switch
    {
        null => BaStart.Note,
        { Problem: { } problem } => problem,
        { Stage: BeforeAfterStage.BeforeArmed } b => $"从一个可再次载入的存档游玩 {b.Minutes} 分钟，然后关闭游戏",
        { Stage: BeforeAfterStage.AfterArmed } a => $"载入同一存档，再游玩同样的 {a.Minutes} 分钟",
        { Stage: BeforeAfterStage.BeforePlaying or BeforeAfterStage.AfterPlaying } => "时间到时请关闭游戏",
        { Stage: BeforeAfterStage.Compiling } => "正在等待编译",
        { Stage: BeforeAfterStage.AfterReady } => "已编译：接下来进行后段测试",
        _ => "",
    };
    public bool HasBaNote => ShowBa && BaNote.Length > 0;
    /// <summary>The compile failed or was stopped: the note says why.</summary>
    public bool BaCompileAgain => Ba is { Stage: BeforeAfterStage.Compiling, Problem: not null } && !BaVoided && !Queued;

    public string AddText => Queued ? "已在队列中" : OffersCareful ? "谨慎编译" : RtPartial ? "编译但不含光线追踪（部分）" : "编译";
    /// <summary>Partly warmed by a fast compile: the compile button turns the careful compile on first.</summary>
    public bool OffersCareful => ScsKiller.IsPartlyWarmed(s) && s.Careful is { On: false };

    // AMD's careful compile: a switch in the compile card, off while the game is queued (a running compile keeps its schedule)
    public bool HasCareful => s.Careful != null && CanCompile;
    public bool? CarefulPending { get; set; }
    public bool LeftOutOn => LeftOutPending ?? s.LeftOutOfAddAll;
    public bool? LeftOutPending { get; set; }
    public bool SharedOn => SharedPending ?? s.SharedHashes;
    public bool? SharedPending { get; set; }
    public string SharedNote => s.SharedChoice == null ? "跟随设置" : "已为此游戏单独设置";
    public bool ShowSharedFollow => SharedPending == null && s.SharedChoice != null;
    public bool CarefulOn => CarefulPending ?? s.Careful?.On == true;
    public bool CanToggleCareful => CarefulPending == null && !Queued;
    public string CarefulNote => "仅用于编译会失败的游戏。" + (s.Careful is { Recorded: 0 } ? "将游戏中录制的管线以少量线程分批编译，让驱动保留更多。此游戏尚无录制，因此没有可谨慎编译的内容。"
        : $"将游戏中录制的管线以 {ScsKiller.AmdCarefulThreads} 个线程分批编译，让驱动保留更多。其余部分以全速编译。"
          + (s.Careful?.Estimate is { } est ? $" 约 {Format.Duration(est)}" + (s.Careful.On || Fmt.CompileTime(s) is not { } fast ? "。" : $"，而非 {Format.Duration(fast)}。") : ""))
        + (s.Careful?.LaunchCompiled is { } lc ? $" 上次编译后，游戏首次启动仍编译了 {lc * 100:0}% 的管线。" : "");
    // AMD: the compiled cache kept aside, in the compile card
    public bool HasKept => s.Kept != null && Host.Core.Vendor.Vendor == GpuVendor.Amd && Host.Core.Settings.KeepCachesAside;
    public string KeptSize => s.Kept is { } k ? Fmt.KeptSize(k) : "";
    public string KeptState => s.Kept switch
    {
        null => "",
        { Unkept: > 0 and var n, HeldBy: var who } => $"{n} 个文件{(n == 1 ? "" : "")}正被 {who ?? "另一个应用"}占用，{(n == 1 ? "未" : "未")}被另行保留。",
        { Frees: > 0 } when s.AntiCheat != AntiCheat.None => "游戏启动后几秒内放回。请保持 SCSKiller 运行。",   // no Play button, VAC keeps it
        { Frees: > 0 } => "驱动移除了其中一部分。按下启动时放回。",
        { PutBackAt: { } at, Late: true } => $"{Fmt.When(at)}放回，晚于游戏启动。部分着色器可能重新编译了。",
        { PutBackAt: { } at } => $"{Fmt.When(at)}放回，早于游戏启动。",
        _ => "现已位于驱动缓存中。",
    };

    // 0 = the global setting, then NewShadersMode's values in order
    public static readonly string[] NewShadersLabels = ["关闭", "通知我", "自动编译并通知我", "自动编译（静默）"];
    public List<string> NewShadersChoices => Same(ref newShadersChoices, [$"使用全局设置（{NewShadersLabels[(int)NewShaders.Global(Host.Core.Settings)]}）", .. NewShadersLabels]);
    // the same list while its labels hold: a new one resets the selection
    List<string>? newShadersChoices, rayTracingChoices, cacheCapChoices, traversalChoices;
    static List<string> Same(ref List<string>? kept, List<string> now) => kept != null && kept.SequenceEqual(now) ? kept : kept = now;
    public int NewShadersIndex => s.NewShadersOverride is { } m ? (int)m + 1 : 0;
    // 0 = the global setting, 1 = on, 2 = off
    public List<string> RayTracingChoices => Same(ref rayTracingChoices, [$"默认（{(Host.Core.Settings.CompileRayTracing ? "开" : "关")}）", "开", "关"]);
    public int RayTracingIndex => s.RayTracingOverride switch { null => 0, true => 1, false => 2 };
    public bool ShowTraversal => TraversalFix.Applies(s, Host.Core.Vendor.Caps);
    public List<string> TraversalChoices => Same(ref traversalChoices, TraversalFix.GameChoices(Host.Core.Settings, s));
    public int TraversalIndex => TraversalFix.GameIndex(s);
    public string TraversalNote => TraversalFix.GameNote(s, Host.Core.Vendor.Gpu);
    public bool TraversalLowVram => TraversalFix.LowVram(Host.Core.Vendor.Gpu) == true;
    public string TraversalWarning => TraversalFix.LowVramWarning;
    // no plan, or one from before inline ray tracing was counted: unknown, so the choice shows
    public bool ShowRayTracing => P?.RtInline == null || s.RayTracingOverride != null || ScsKiller.HasRayTracing(P);
    public List<string> CacheCapChoices => Same(ref cacheCapChoices, ["无限制", .. ScsKiller.CacheCapsGB.Select(gb => $"{gb} GB")]);
    public int CacheCapIndex => s.CacheCapGB is { } gb ? Array.IndexOf(ScsKiller.CacheCapsGB, gb) + 1 : 0;
    public bool ShowCacheCap => ScsKiller.HasCacheCap(s);
    // Windows' shader cache of the game, read off the UI thread: Clear cache clears it too, so it can be the only cache to clear.
    BackgroundRead<long>? otherCaches;
    long otherBytes;
    public void ReadCaches() => (otherCaches ??= new("game caches", OtherCacheBytes, b => { otherBytes = b; Changed(); })).Request();
    long OtherCacheBytes()
    {
        try { return Host.Core.GameCaches(id).Where(p => p.Name != ScsKiller.DriverPart).Sum(p => p.Bytes); }
        catch (Exception) { return 0; }   // e.g. the game is gone after a rescan
    }
    // read off the UI thread: it reads Steam's localconfig.vdf and the placement's manifest
    string? steamCacheNote;
    BackgroundRead<string?>? steamNoteRead;
    void ReadSteamCacheNote()
    {
        if (!Host.Platform.SteamCacheNote) return;
        (steamNoteRead ??= new("Steam note", () =>
        {
            try { return Host.Core.SteamCacheNote(id); }
            catch (Exception) { return null; }   // e.g. the game is gone after a rescan
        }, n =>
        {
            if (n == steamCacheNote) return;
            steamCacheNote = n;
            Changed();
        })).Request();
    }
    public string SteamCacheNote => steamCacheNote ?? "";
    public bool HasSteamCacheNote => steamCacheNote != null;

    public bool HasCache => s.DiskBytes != null || otherBytes > 0 || s.Status is GameStatus.Warmed or GameStatus.Stale;
    // compiled before a verdict changed: the cache stays until the user clears it
    public string? KeptCacheNote => (s.Status == GameStatus.Unsupported || s.NoStutter != null) && s.WarmedAt != null && s.CacheOnDisk > 0
        ? s.StartFirst ? $"SCSKiller 此前已为此游戏编译了 {DiskValue} 缓存。"
        : $"SCSKiller 在此游戏被标记为{(s.NoStutter != null ? "无着色器卡顿" : "不受支持")}之前，已为其编译了 {DiskValue} 缓存。"
        : null;
    public bool HasKeptCache => KeptCacheNote != null;

    // Maximum mode suggested for this game on this GPU vendor (Games.MaximumMode), or on for it alone
    public bool? MaximumPending { get; set; }
    public bool MaximumSuggested => s.MaximumSuggestion != null;
    // on for every game in Settings: the per-game switch changes nothing, so it isn't offered
    public bool HasMaximumLine => MaximumSuggested || s.MaximumForGame == true && !Host.Core.Settings.MaximumPlans;
    public string MaximumLine => MaximumSuggested
        ? "最大模式对此游戏有帮助：它还会编译此游戏着色器的每一种组合，因此游玩时需要编译的更少。"
        : "此游戏已开启最大模式。其编译耗时更长、占用更多磁盘空间。";
    public string? MaximumNote => string.Join(" ", new[] { s.MaximumSuggestion is { Length: > 0 } note ? note : null,
        MaximumSuggested && s.MaximumAddsBytes is { } adds ? $"在 AMD 上，它最多会向驱动的 {Format.Bytes(AmdAppCache.DxcCacheCap)} 共享缓存增加 {Format.Bytes(adds)}。" : null }.OfType<string>()) is { Length: > 0 } t ? t : null;
    public bool HasMaximumNote => MaximumNote != null;
    public string MaximumAction => MaximumSuggested ? "为此游戏开启" : "为此游戏关闭";
    public bool CanSetMaximum => MaximumPending == null && !Queued;
    public bool ShowClearCache => HasCache || CanCompile;
    // as Disk space shows it
    public string ClearCacheText => s.DiskBytes != null ? $"清除缓存（{DiskValue}）" : "清除缓存";
    public string ClearCacheTip => BaInProgressAny ? BaBusyTip : HasCache ? $"删除此游戏的着色器缓存（驱动的{(Host.Platform.SystemShaderCache ? "和 Windows 的" : "")}，以及你选择删除的游戏自身缓存），使其下次运行从冷启动开始"
        : "SCSKiller 会在首次编译此游戏或看到它运行时，了解此游戏的驱动缓存文件";

    // Rate this compile (CompatReport) and what players with a similar system reported (CompatFeed)
    string? ReportWhyNot => CompatReport.WhyNot(s, ScsKiller.PackGpu(Host.Core.Vendor.Vendor), Host.Core.LinuxReportsRefused);
    public bool CanReport => ReportWhyNot == null;
    public string ReportTip => ReportWhyNot ?? "告诉其他玩家 SCSKiller 编译后此游戏运行得如何";
    public bool HasSimilar => s.Similar is SimilarSystems.Helped or SimilarSystems.NoHelp;
    public bool HasSimilarWarning => s.Similar == SimilarSystems.Warning;
    public bool HasRuntimeWarning => s.RuntimeWarning != null;
    public string RuntimeWarning => s.RuntimeWarning ?? "";
    public string SimilarText => s.Similar switch
    {
        SimilarSystems.Helped => "配置相似的用户反馈：编译消除或减轻了此游戏的卡顿。",
        SimilarSystems.NoHelp => "配置相似的用户反馈：编译对此游戏没有帮助。",
        SimilarSystems.Warning => $"配置相似的用户反馈：{Name} 在编译后运行更差。",
        _ => "",
    };

    public bool IsKnownStutter => s.KnownStutter != null;
    public string StutterReason => s.KnownStutter?.Reason ?? "";
    public bool IsNoStutter => s.NoStutter != null;
    public string NoStutterReason => s.NoStutter ?? "";
    /// <summary>The source as a link when it's a URL; otherwise <see cref="StutterCitation"/> shows it as text.</summary>
    public Uri? StutterUri => Uri.TryCreate(s.KnownStutter?.Source, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u : null;
    public string StutterLinkText => StutterUri != null ? $"来源（{s.KnownStutter!.Date}）" : "";
    public string StutterCitation => s.KnownStutter is { } k && StutterUri == null ? $"来源：{k.Source}（{k.Date}）" : "";
    public string EngineChip => s.Engine is { } e ? Format.Engine(e, e.Family == "Unreal" ? "Unreal Engine" : null) + Fmt.Guessed(e) + (e.Fork != null ? " · 自定义分支" : "") : "未知引擎";
    public string ApiChip => s.Engine?.GraphicsApi.Replace("D3D", "DirectX ") ?? "";
    public bool HasApi => ApiChip.Length > 0 && !ShowApiPicker;

    // DirectX picker: every DirectX game (GameState.ApiDetected), or a choice is kept; the chip's place
    // or a carved game whose API nothing tells, packed shaders too: it may run Vulkan at run time (SDL)
    public bool ShowApiPicker => s is { ApiDetected: not null } or { ApiOverride: not null } && s.Engine is { Unsupported: null, Encrypted: false }
        || s.Engine is { Encrypted: false } e && Core.Carved.CarvedReader.Carved(e) && s.ApiDetected == Core.Unreal.UnrealRhi.Ambiguous;
    public string ApiTip => id == ScsKiller.FortniteId ? "SCSKiller 为其编译的 Fortnite 模式。" : "SCSKiller 为其编译的 DirectX。自动使用检测到的那个。";
    public bool? ApiPending { get; set; }
    public bool CanPickApi => ApiPending == null && !Host.Core.Queue.Any(q => q.GameId == id && Format.Running(q));   // its plan check too: the core refuses a change meanwhile
    IReadOnlyList<DxApi> ApiChoices => ScsKiller.ApiChoices(s, Host.Core.Vendor.Caps);
    public int ApiIndex => id == ScsKiller.FortniteId ? (ScsKiller.FortnitePerformance(s) ? 1 : 0) : s.ApiOverride is { } a ? ApiChoices.ToList().IndexOf(a) + 1 : 0;
    /// <summary>The API of the picker's item <paramref name="i"/>; null = Auto.</summary>
    public DxApi? ApiAt(int i) => id == ScsKiller.FortniteId ? (i == 1 ? DxApi.D3D11 : DxApi.D3D12) : i <= 0 ? null : ApiChoices[i - 1];
    string[]? apiItems;
    public string[] ApiItems
    {
        get
        {
            var auto = s.Engine?.GraphicsApi.EndsWith(ScsKiller.RecordingSuffix, StringComparison.Ordinal) == true ? $"自动（{Dx(s.Engine.GraphicsApi)}，来自你的录制）"
                : s.ApiDetected == Core.Unreal.UnrealRhi.Ambiguous ? "自动（DirectX 11 和 12）"
                : s.ApiDetected is { } d ? $"自动（{Dx(d)}）" : "自动";
            string[] items = id == ScsKiller.FortniteId ? ["普通", "性能"] : [auto, .. ApiChoices.Select(ScsKiller.ApiName)];
            if (apiItems == null || !apiItems.SequenceEqual(items)) apiItems = items;   // the same array while the labels hold: a new one resets the selection
            return apiItems;
        }
    }
    static string Dx(string api) => ScsKiller.ApiName(api);
    public string? ApiOverruled => ScsKiller.ApiOverruled(s);
    public bool HasApiOverruled => ApiOverruled != null;
    public string? ApiKept => ScsKiller.KeptApisNote(s);
    public bool HasApiKept => ApiKept != null;
    public string AntiCheatChip => NoAntiCheat ? "无反作弊" : ScsKiller.AntiCheatHeld(s) ? ScsKiller.HeldAntiCheatTitle : ScsKiller.AntiCheatOpen(s) ? ScsKiller.CheckingAntiCheatTitle : ScsKiller.AntiCheatName(s);
    public string AntiCheatTip => NoAntiCheat ? "未发现反作弊：此游戏可以录制"
        : ScsKiller.AntiCheatHeld(s) ? ScsKiller.HeldAntiCheatNote
        : ScsKiller.AntiCheatOpen(s) ? ScsKiller.CheckingAntiCheatNote
        : s.RiskEligible ? $"此游戏包含 {ScsKiller.AntiCheatName(s)}。记录器仅在你确认的场次中运行，风险自负。"
        : !Host.Platform.RecordAntiCheatGames ? NoAntiCheatSessions
        : ScsKiller.RiskNotHere(s) ?? "此游戏包含反作弊：不会被录制";
    static string NoAntiCheatSessions => $"在 {Host.Platform.Name} 上尚不支持录制含反作弊的游戏。";
    public bool HasMiddleware => s.Middleware is { Count: > 0 };
    public bool HasModChip => GameRow.ModChip(s) != null;
    public string ModChip => GameRow.ModChip(s)?.Text ?? "";
    public string ModTip => GameRow.ModChip(s)?.Tip ?? "";
    public bool HasDeliveryChip => ScsKiller.DeliveryChip(s) != null;
    public string DeliveryChip => ScsKiller.DeliveryChip(s) ?? "";
    public string DeliveryTip => ScsKiller.DeliveryTip;
    IReadOnlyList<MiddlewareTag> Mw => s.Middleware ?? [];
    /// <summary>"Also compiles" only names tags with known pipelines (and DLSS on NVIDIA, by the driver) of a game that compiles; else what was detected.</summary>
    public string MiddlewareChip => Mw.Where(t => t.Pipelines > 0).Select(t => t.Label)
            .Concat(Mw.Any(t => t.Label == "DLSS") && GpuBackends.OwnDriver(Host.Core.Vendor) == GpuVendor.Nvidia ? ["DLSS（由驱动）"] : []).ToList() is { Count: > 0 } also
            && s.Status != GameStatus.Unsupported
        ? "还会编译：" + string.Join(" · ", also) : "检测到：" + string.Join(" · ", Mw.Select(t => t.Label));
    public string MiddlewareTip => "游戏旁的超分 DLL。一旦任意游戏的录制见过该 DLL 版本，其管线就会随之一同编译"
        + "：在本机上如此；FSR 和 XeSS 则在任何共享它们的 PC 上都如此"
        + (s.Plan is { MiddlewareItems: > 0 } p ? $"（此方案：其中 {Fmt.N(p.MiddlewareItems)} 条）" : "") + "。\n\n"
        + string.Join("\n", Mw.Select(Fmt.TagTip));

    // The one tip left: a game that can't compile before a recording, or not its ray tracing (a plan's other gaps are the
    // coverage card's "what's left").
    public bool HasRecordTip => CanRecord && (s.Status == GameStatus.NeedsRecording && !ScsKiller.NeedsSteamStart(s) || RtAfter) && !RecordOn && s.NoStutter == null;
    bool Enough => ScsKiller.RecordedEnough(s);
    public string RecordTipTitle => Enough ? "录制更多游玩" : "录制 5 分钟游玩";
    public string RecordTip => RtPartial || RtAfter ? $"{TurnOnAndPlay}，并开启光线追踪{(Enough ? "" : "约 5 分钟")}。" + ScsKiller.RtWhy(Host.Core.Vendor.Caps, s.Engine)
            + (RtAfter ? " 其余部分已编译。" : " 其余部分已能从游戏文件编译。")
        : Enough ? Sentence(s.StatusReason) + "。"
        : $"{TurnOnAndPlay}，照常游玩约 5 分钟。SCSKiller 会从中了解此游戏的着色器布局，之后就能编译。";
    // a collection's other game is recorded by its first game's recorder, switched on that game's page
    string TurnOnAndPlay => Collections.RecordedBy(s.Game.Id) is { } by ? $"在 {Collections.Member(by)!.Value.Game.Name} 的页面上开启录制并游玩此游戏" : "开启录制并游玩";
    public bool ShowRecordAction => !RecordOn && CanToggleRecord && NoAntiCheat;   // an anti-cheat game records only through its session card

    // Shader coverage (PlanStats), in words: how much of what the planner found gets compiled, where it comes from, what's
    // left and what helps; every count in Details. No plan yet on a game that can compile: the card says when it's measured.
    PlanStats? P => s.Plan;
    // a compile that didn't help: the ring is greyed out, nothing of it counts for the game
    int? Pct => DriverChanged ? null : ScsKiller.ShownPercent(s);
    // the last compile filled the old driver's cache, which the new driver doesn't use
    bool DriverChanged => s.Status == GameStatus.Stale && s.StatusReason.StartsWith("driver changed:", StringComparison.Ordinal);
    public bool HasCoverageCard => ScsKiller.ShowsCoverage(s) || CanCompile;
    public bool HasPlan => P != null;
    public bool HasMeter => Pct != null;
    public bool LowCoverage => !s.CompileUnreached && (Pct is { } pct ? pct < 90 : Partial);   // more than 10% left out, like ScsKiller.IsPartial (the test for a plan without the count)
    public bool GoodCoverage => HasMeter && !LowCoverage;
    bool Dx11Only => P is { StageSets: 0, Generated: 0, D3D11Shaders: > 0 };
    public string CoverageHeadline => P == null ? "首次编译开始后可知"
        : s.CompileUnreached ? "此游戏并未使用其编译结果"
        : DriverChanged ? $"其上次编译针对的是驱动 {s.WarmedDriverVersion}"
        : Dx11Only && Pct is null or 100 ? (Compiled ? "已覆盖" : "将覆盖") + "游戏文件中的每一个 DirectX 11 着色器" + (Core.Planning.Planner.D3D11CoversVs(Host.Core.Vendor.Caps) || P.D3D11Draws > 0 ? "" : "（顶点着色器除外）")
        : Pct is not { } pct ? "下次编译时重新测量"
        : (Compiled || s.NewOnly ? "已覆盖" : s.WarmedAt != null ? "其上次编译覆盖了" : "将覆盖")
          + ScsKiller.CoverageWhat(s, pct, RecordOn);
    // the headline's own words under the ring while no recording has confirmed the engine rule
    public bool HasRuleNote => Pct == 100 && !Dx11Only && ScsKiller.RuleUnconfirmed(s);
    bool Rt => ScsKiller.NeedsRtRecording(s);
    bool RtUnseen => s.RtUnseen;
    bool RtInline => ScsKiller.RtInlineCovers(P);
    bool Compiled => s.WarmedAt != null && s.Status != GameStatus.Stale;
    VendorCaps Caps => Host.Core is ScsKiller k ? k.CapsOf(s.Game.Id) : Host.Core.Vendor.Caps;

    /// <summary>Where the plan's pipelines come from, in a player's words; the recording's row carries the community note,
    /// and without one the community database's status is its own row. Ray tracing: covered or what it needs.</summary>
    public IReadOnlyList<DetailRow> Sources => P is not { } p ? [] : new DetailRow?[]
    {
        p.Generated > 0 ? new(ScsKiller.BothApis(p) ? "DirectX 12，来自游戏文件" : "在游戏文件中找到", Fmt.N(p.Generated),
            Pct == 100 && s.ShaderCount is { } n && p.Generated > n ? "每个着色器会用于多种组合，因此这些的数量多于游戏中的着色器数。" : null) : null,
        !Vk && FromPlay > 0 ? new(RecordingSource, Fmt.N(FromPlay), CommunityLine) : null,
        p.FromDownload > 0 ? new("来自 Steam 的着色器下载", Fmt.N(p.FromDownload)) : null,
        Vk && p.FromCommunity > 0 ? new("来自社区数据库", Fmt.N(p.FromCommunity)) : null,
        Vk && FromPlay > 0 ? new(FromPlayLabel, Fmt.N(FromPlay), CommunityLine) : null,
        p.CacheBudgetLeftOut is > 0 and var left ? new(ScsKiller.CacheBudgetTitle(Caps, s.Engine, s.PlanCacheCapGB), Fmt.N(left), ScsKiller.CacheBudgetNote(Caps, s.Engine, s.PlanCacheCapGB)) : null,
        s.Official is { } o ? new("官方录制", $"{o.Psos:N0} 条管线", $"由 SCSKiller 为此版本发布，下载于 {o.DownloadedAt.LocalDateTime:d}") : null,
        s.Community == null && s.InCommunityDb is { } inDb && !HasDbTeaser ? new("社区数据库", inDb ? "已有此版本的录制" : "暂无") : null,
        p.MiddlewareItems > 0 ? new(p.MiddlewareSharedItems == 0 ? "其超分组件，从录制中学习"
            : p.MiddlewareSharedItems == p.MiddlewareItems ? "其超分组件，来自共享包" : "其超分组件，来自录制和共享包", Fmt.N(p.MiddlewareItems)) : null,
        p.D3D11Shaders > 0 ? new(ScsKiller.BothApis(p) ? "DirectX 11，来自游戏文件" : "DirectX 11 着色器", Fmt.N(p.D3D11Shaders)) : null,
        p.RtLibraries > 0 && s.RtSkipped ? new("光线追踪", "已跳过（你的设置）")
        : p.RtLibraries > 0 ? new("光线追踪特效", s.Engine?.NoRayTracing == true ? "此游戏中已关闭" : s.Engine?.NoRtPipelines == true ? "内联，来自游戏文件" : p.RtUncovered == 0 ? "已覆盖" : s.RtToPlan ? "正在检查" : RtUnseen ? "录制时未见" : RtInline ? "内联部分已覆盖" : !Rt ? "大部分已覆盖" : CanRecord ? "需要录制" : "未编译") : null,
    }.OfType<DetailRow>().ToList();
    public bool HasSources => Sources.Count > 0;

    // a Vulkan plan reads no game files: its pipelines are Steam's download and what play on this PC recorded, also a
    // DirectX game's under Proton made of Steam's alone. A DirectX plan with Steam's counts its download with its recorded
    bool Vk => s.Engine is { } e && Core.Vulkan.VulkanPlan.IsVulkan(e) || P is { FromDownload: not null, Generated: 0, SynthesizedTemplates: 0 };
    long FromPlay => P is { } p ? p.Recorded - (p.FromDownload ?? 0) - (p.FromCommunity ?? 0) : 0;
    string FromPlayLabel => P?.FromDownload == null ? "已录制的管线" : "来自你的游玩";   // a plan from before the split was counted
    // the recording as of its last import, which a plan built before that session doesn't count yet
    long Recorded => s.RecordedPipelines ?? FromPlay;

    /// <summary>The recorded row's label: whose recordings the plan's recorded pipelines come from.</summary>
    string RecordingSource
    {
        get
        {
            if (UsedCommunity == null && s.Official == null) return "你的游玩录制";
            string[] from = [.. (UsedCommunity ?? s.Official)!.WithLocalRecording ? ["你的录制"] : Array.Empty<string>(),
                .. UsedCommunity != null ? ["社区数据库"] : Array.Empty<string>(), .. s.Official != null ? ["官方录制"] : Array.Empty<string>()];
            var text = from.Length == 1 ? from[0] : string.Join("、", from[..^1]) + "和" + from[^1];
            return char.ToUpperInvariant(text[0]) + text[1..];
        }
    }
    /// <summary>The community recording the plan uses: not one that doesn't match the game's files.</summary>
    CommunityInfo? UsedCommunity => s.Community is { OtherVersion: false } c ? c : null;
    /// <summary>The community database's line under the recording row; null = nothing to say.</summary>
    string? CommunityLine => UsedCommunity is { } c
        ? $"社区录制下载于 {c.DownloadedAt.LocalDateTime:d}" + (s.RecordingSharedAt is { } at ? $" · 你的录制分享于 {at.LocalDateTime:d}" : "")
        : s.RecordingSharedAt is { } at2 ? $"已分享给社区数据库 {at2.LocalDateTime:d}" : null;

    // What's left and what helps. LeftOut is mostly Uncovered (root signature slots SCSKiller can't build) or, with a learned
    // lookup, stage sets no recording showed yet (no root signature / template): a longer recording is what helps there.
    // Ray tracing the plan can't compile comes first, in the status's words (ScsKiller.RtNote).
    enum Left { AntiCheat, EngineSlots, UnknownSlots, NotSeen, Recording, PlayedClean, PlayedCompiles, NothingKnown, PlayOnly }
    long Missing => P is { } m ? Math.Max(m.LeftOut, m.Uncovered) : 0;   // a plan without LeftOut: Uncovered only
    Left LeftCase => !CanRecord ? Left.AntiCheat
        : Missing > 0 ? P!.Uncovered * 2 >= Missing ? P.Recorded == 0 ? Left.EngineSlots : Left.UnknownSlots : Left.NotSeen
        : RecordOn ? Left.Recording
        : L is { } l ? PlayCompiles(l) == 0 ? Left.PlayedClean : Left.PlayedCompiles   // what was measured beats what's likely
        : P is { Recorded: > 0 } ? Left.NothingKnown
        : Left.PlayOnly;
    public string LeftText => LeftSentences(tipAsks: false);
    /// <summary>tipAsks: the record tip already gives the ray tracing note and the recording advice.</summary>
    string LeftSentences(bool tipAsks) => string.Join(" ", new string?[]
    {
        RtUnseen ? Sentence(ScsKiller.RtUnseenNote) + "。" : RtInline ? Sentence(ScsKiller.RtInlineNote) + "。" : !Rt || tipAsks ? null : CanRecord ? (HasDbTeaser ? ScsKiller.RtNeedsRecording : ScsKiller.RtNote(s.InCommunityDb, Host.Account?.HasDb == true)) + "。"
            : $"光线追踪特效未编译：它们需要录制{(s.RiskEligible ? "，" + ScsKiller.RiskNoRecording : !NoAntiCheat ? ScsKiller.AntiCheatHeld(s) ? "，且此游戏未能完成反作弊检查" : ScsKiller.AntiCheatOpen(s) ? "，" + ScsKiller.AntiCheatCheckNoRecording : "。此游戏包含反作弊" : "，" + (s.RootUnconfirmed ? ScsKiller.ManualNoRecording : s.PreloadHeld is { } held ? ScsKiller.PreloadHeldNoRecording(held, s.PreloadHeldByOptions) : ScsKiller.StreamlineNoRecording))}。",
        LeftCase switch
        {
            Left.AntiCheat => s.RiskEligible ? "其余部分会在游玩时编译：此页面上的一个场次可以录制它，风险自负。"
                : ScsKiller.AntiCheatHeld(s) ? "其余部分会在游玩时编译。" + ScsKiller.HeldAntiCheatNote
                : ScsKiller.AntiCheatOpen(s) ? "其余部分会在游玩时编译：其反作弊检查尚未完成，录制要等到检查完成。"
                : !NoAntiCheat ? "其余部分会在游玩时编译。此游戏包含反作弊。"
                : s.RootUnconfirmed ? "其余部分会在游玩时编译：确认游戏文件夹（上方的“游戏文件夹…”）即可录制缺失部分。"
                : s.PreloadHeld != null ? "其余部分会在游玩时编译。"
                : "其余部分会在游玩时编译：NVIDIA 上的 DLSS 阻止了本可发现它们的录制。",
            Left.EngineSlots => ScsKiller.EngineSlotsNote(RecordOn),
            Left.UnknownSlots => "其余部分使用的着色器槽位 SCSKiller 尚无法重建，因此它们仍会在游玩时编译。",
            Left.NotSeen => ScsKiller.NotSeenNote(RecordOn, P),
            Left.Recording => "录制已开启：你新游玩到的任何内容都会在下一次编译时加入。",
            Left.PlayedCompiles => $"上次游玩时，仍有 {PlayCompiles(L!):N0} 项需要编译。" + (tipAsks ? "" : " 录制可以捕捉它们。"),
            _ when Rt || RtUnseen || RtInline => null,   // ray tracing is what's left
            Left.PlayedClean => "上次游玩时没有缺失任何内容。",
            Left.NothingKnown => "就 SCSKiller 所知，没有缺失任何内容。",
            _ => "少数特效只在游玩时才会组合起来。" + (tipAsks ? "" : " 一段简短的录制即可捕捉它们。"),
        },
        !Guessed ? null : tipAsks ? "部分着色器状态是推测的。" : "部分着色器状态是推测的。更长的录制可以让它们变得精确。",
    }.OfType<string>());
    // per-stage plans (AMD): the planner warns that a wrong guess costs a compile in game
    bool Guessed => P is { } g && g.GuessedUnits > 0.1 * (g.ExactUnits + g.InferredUnits + g.GuessedUnits);
    // not twice: a game that needs a recording has the button in its tip
    public bool ShowLeftRecord => ShowRecordAction && !HasRecordTip && s.NoStutter == null && (LeftCase is Left.EngineSlots or Left.NotSeen or Left.PlayedCompiles or Left.PlayOnly || Guessed || Rt || RtUnseen || RtInline);
    // before the first compile there's nothing left yet. Recording on: what's missing comes from playing, without the button
    public bool ShowLeftCallout => HasPlan && (ShowLeftRecord || RecordOn && s.NoStutter == null && LeftCase is Left.NotSeen or Left.EngineSlots);
    public string LeftTitle => Rt || RtUnseen ? "光线追踪特效尚未编译" : "部分着色器仍会在游玩时编译";

    public string CoverageValue => Pct is { } pct ? $"{pct}%" : Dx11Only && !s.CompileUnreached ? "全部" : Format.Dash;
    public string CoverageLabel => s.CompileUnreached ? "未使用" : Pct != null || Dx11Only ? "已覆盖" : "尚未";
    /// <summary>The ring's arc as path data, in a 120 px box with a 12 px stroke; null when it isn't drawn in that colour.</summary>
    public string? GoodArc => GoodCoverage ? Arc() : null;
    public string? LowArc => LowCoverage ? Arc() : null;
    string? Arc()
    {
        const double c = 60, r = 54;
        double a = Math.Min(Pct ?? 0, 99.99) / 100 * 2 * Math.PI;
        if (a == 0) return null;   // a zero-length arc's round caps would draw a dot
        return string.Create(CultureInfo.InvariantCulture, $"M {c},{c - r} A {r},{r} 0 {(a > Math.PI ? 1 : 0)} 1 {c + r * Math.Sin(a):0.###},{c - r * Math.Cos(a):0.###}");
    }
    public bool HasTimeValue => Fmt.CompileTime(s) != null;
    public string TimeLabel => s.LastWarmTime != null ? "编译耗时" : "编译耗时（预估）";
    public string TimeValue => s.LastWarmTime is { TotalMinutes: >= 1, TotalHours: < 1 } t ? $"{t.Minutes} 分 {t.Seconds} 秒"   // measured: to the second
        : Format.Duration(Fmt.CompileTime(s));
    public bool HasDiskValue => s.DiskBytes != null || s.EstimatedCacheBytes != null;
    public string DiskLabel => s.DiskBytes != null ? "磁盘空间" : "磁盘空间（预估）";
    public string DiskValue => Format.Bytes(OnDisk ?? s.EstimatedCacheBytes);
    public bool HasDiskInUse => s.CacheOnDisk != null && Format.InUseShown(OnDisk, InUse);
    public string DiskInUse => $"{Format.Bytes(InUse)} 在用";
    // Windows' dbs counted whole: what they hold isn't read
    long? InUse => s.CacheInUse + (s.WindowsCache ?? 0);
    // the kept-aside copy of what the driver removed takes disk space too
    long? OnDisk => s.DiskBytes + (s.Kept?.Frees ?? 0);
    public bool HasShaderCount => s.ShaderCount != null;
    public string ShaderCount => Fmt.N(ScsKiller.PageShaders(s));
    public string? ShaderCountTip => ScsKiller.AllShadersTip(s);
    // measured frames when the recorder timed them, else the compile count
    long? StutterCount => F is { } f ? Format.NoPlay(f.Low1PctFps) ? null : ShaderHitches.Count : L is { } l ? PlayCompiles(l) : null;
    public bool HasStutterTile => HasSession && StutterCount != null;
    public string StuttersValue => StutterCount is not { } n ? "" : n == 0 ? "无" : Fmt.N(n);
    public bool HasStutters => StutterCount > 0;
    public bool NoStutters => StutterCount == 0;
    // without frame times the count is of compiles, which may or may not have stuttered
    public string StuttersLabel => F != null ? "上次游玩时的卡顿" : "游玩期间的编译";

    /// <summary>Every count, for power users.</summary>
    public IReadOnlyList<DetailRow> Details => new DetailRow?[]
    {
        P is { } p ? new("方案中的管线", Fmt.N(p.Recorded + p.Generated + p.MiddlewareItems + p.Variants)) : null,
        s.ShaderCount != null ? new("游戏文件中的着色器", ShaderCount) : null,
        P is { StageSets: > 0 } ? new("找到的着色器组合", Fmt.N(P.StageSets)) : null,
        P is { LeftOut: > 0 } ? new("未纳入", ScsKiller.LeftOutValue(P)) : null,
        P is { StageSets: 0, Uncovered: > 0 } ? new("因 SCSKiller 无法重建的着色器槽位而未纳入", Fmt.N(P.Uncovered)) : null,
        P != null && !Vk ? new("已录制的管线", Fmt.N(Recorded)) : null,
        P is { FromDownload: { } dl } && (Vk || dl > 0) ? new("来自 Steam 的着色器下载", Fmt.N(dl)) : null,
        Vk && P != null ? new(FromPlayLabel, Fmt.N(Recorded)) : null,
        P != null && !Vk ? new("由游戏文件构建", Fmt.N(P.Generated)) : null,
        P is { MiddlewareItems: > 0 } ? new("超分管线", Fmt.N(P.MiddlewareItems)) : null,
        P is { Variants: > 0 } ? new("在其他纹理过滤设置下", Fmt.N(P.Variants)) : null,
        P is { D3D11Shaders: > 0 } ? new("DirectX 11 着色器", Fmt.N(P.D3D11Shaders)) : null,
        P is { RtLibraries: > 0 } ? new("光线追踪着色器库", $"{P.RtLibraries:N0}" + (s.Engine?.NoRtPipelines == true ? "（游戏未使用）" : s.RtSkipped ? "（已跳过，你的设置）" : P.RtUncovered > 0 ? $"（{P.RtUncovered:N0} 条未编译）" : "")) : null,
        P is { RtInline: > 0 } ? new("内联光线追踪的着色器", Fmt.N(P.RtInline)) : null,
        s.Status != GameStatus.Unsupported && s.InCommunityDb != null && !HasDbTeaser ? new("你的版本的社区数据", ScsKiller.CommunityDbValue(s, Host.Account?.DbEntitled == true)) : null,
        s.Community is { } c ? new("已下载的社区录制", c.OtherVersion ? "与你的游戏文件不匹配" : $"{c.Psos:N0} 条管线") : null,
        s.Official is { } o ? new("官方录制", $"{o.Psos:N0} 条管线") : null,
        P != null && !Vk ? new("合成的管线模板", Fmt.N(P.SynthesizedTemplates)) : null,
        P != null && !Vk ? new("着色器布局", $"{P.RootSignatures:N0} · {RootSigSource(P)}") : null,
        P is { } u && u.ExactUnits + u.InferredUnits + u.GuessedUnits > 0 ? new("阶段单元（精确 / 推断 / 推测）", $"{u.ExactUnits:N0} / {u.InferredUnits:N0} / {u.GuessedUnits:N0}") : null,
        P is { LayoutCoverage: > 0 } ? new("来自录制的顶点布局", $"{P.LayoutCoverage:P1}") : null,
        s.CacheOnDisk is { } disk ? new("驱动缓存", Format.Bytes(disk) + "（实测）" + (Format.InUseShown(disk, s.CacheInUse) ? $"，{Format.Bytes(s.CacheInUse)} 在用" : "")) : s.EstimatedCacheBytes is { } est ? new("驱动缓存", "≈ " + Format.Bytes(est) + "（预估）") : null,
        s.WindowsCache is { } w ? new(ScsKiller.WindowsPart, Format.Bytes(w)) : null,
    }.OfType<DetailRow>().ToList();

    // "Shipped with the shaders" is the carver's case.
    string RootSigSource(PlanStats p) =>
        p.RootSigRuleVerified ? "已在此引擎版本上测试"
        : ScsKiller.ApiKey(s.Engine?.GraphicsApi) == "11" ? "不需要（DirectX 11）"
        : p is { RootSignatures: 0, Recorded: > 0 } ? "在已录制的管线中"
        : s.Engine?.Family == "Unreal" ? (p.Recorded > 0 ? "从录制中学习" : "尚未在此引擎版本上测试")
        : p.Recorded > 0 ? "从录制中学习" : "随着色器一同附带";

    // Last compile: when, how long, what failed or was skipped and what to do; never compiled: what the first one costs.
    public bool HasCompileCard => CanCompile || s.LastWarmTime != null;
    public string CompileTitle => s.LastWarmTime == null ? "首次编译" : "上次编译";
    long Failed => s.LastWarmFailed ?? 0;
    long Skipped => s.LastWarmSkipped ?? 0;
    long Crashed => s.LastWarmCrashed ?? 0;
    long DriverSkipped => s.LastWarmDriverSkipped ?? 0;
    long Known => Math.Min(DriverSkipped, s.LastWarmKnown ?? 0);   // of DriverSkipped
    long CrashedBefore => Math.Min(Crashed, s.LastWarmCrashedBefore ?? 0);   // of Crashed
    long Unbuilt => s.LastWarmUnbuilt ?? 0;
    long Invalid => s.LastWarmInvalid ?? 0;
    long Unsupported => s.LastWarmUnsupported ?? 0;
    public string CompileText => s.WarmedAt is { } at
        ? $"编译于 {Fmt.When(at)}" + (s.LastWarmTime is { } t ? $"，耗时 {Format.Duration(t)}" : "") + $"，针对驱动 {s.WarmedDriverVersion}。"
          + (ScsKiller.NothingFailed(s) ? " 没有失败。" : "")   // null = not known (a warm from before these were kept): no claim
          + (ScsKiller.RefusedLine(s) is { } refused ? " " + refused : "")
          + (OnDisk is { } c ? $" 它占用 {Format.Bytes(c)} 磁盘空间。" : "")
          + " " + OwnShaderStep
        : s.LastWarmTime is { } last ? $"其着色器缓存已被清除。上次编译耗时 {Format.Duration(last)}。"
        : (s.EstimatedWarmTime is { } est ? $"大约需要 {Format.Duration(est)}，并在后台运行，因此你可以继续使用电脑。" : "它在后台运行，因此你可以继续使用电脑。")
          + (s.EstimatedCacheBytes is { } b ? $" 预计会为驱动的着色器缓存增加 {Format.Bytes(b)}。" : "");
    // no game file says whether a game runs its own precompile at startup: said on every compiled game's page, not its row
    public const string OwnShaderStep = "游戏启动时可能仍会运行其自身的着色器步骤。";
    public string SkippedTitle => $"{Skipped + Crashed + DriverSkipped + Unbuilt + Invalid + Unsupported:N0} 项已跳过";
    long NeedsRecording => Math.Min(Skipped, s.LastWarmNeedsRecording ?? 0);   // of Skipped: flagged by the community recording
    long NotInGame => Skipped - NeedsRecording;
    public string? SkippedText => s.WarmedAt == null || Skipped + Crashed + DriverSkipped + Unbuilt + Invalid + Unsupported == 0 ? null
        : string.Join(" ", new[]
        {
            NotInGame > 0 && P is { FromDownload: > 0, Generated: 0 } ? $"{NotInGame:N0} 项已跳过：{ScsKiller.DownloadSkipped}。开启记录器游玩可补上它们。"
            : NotInGame > 0 ? $"{NotInGame:N0} {(NotInGame == 1 ? "项" : "项")}已跳过：{(NotInGame == 1 ? "其着色器不在" : "其着色器不在")}此版本的游戏中。无需处理。" : null,
            Crashed - CrashedBefore is > 0 and var crashedNow ? $"{crashedNow:N0} {(crashedNow == 1 ? "项" : "项")}已跳过：它们会使 GPU 驱动崩溃。如果游戏用到{(crashedNow == 1 ? "它们" : "它们")}，会自行编译{(crashedNow == 1 ? "它们" : "它们")}。" : null,
            DriverSkipped - Known is > 0 and var now ? $"{now:N0} {(now == 1 ? "项" : "项")}已跳过：GPU 驱动在{(now == 1 ? "它们或它们所构建的内容" : "它们或它们所构建的内容")}上崩溃、挂起或内存耗尽，或者{(now == 1 ? "未" : "未")}在某个持续失败的位置之后继续尝试。" : null,
            Known + CrashedBefore > 0 ? ScsKiller.KnownNote(Known + CrashedBefore) + "。" : null,
            Unbuilt > 0 ? $"{Unbuilt:N0} {(Unbuilt == 1 ? "项" : "项")}已跳过：SCSKiller 尚无法构建{(Unbuilt == 1 ? "它们" : "它们")}。" : null,
            Invalid > 0 ? $"{Invalid:N0} 项已跳过，{(Invalid == 1 ? "其着色器未通过" : "其着色器未通过")} Vulkan 验证（Steam 也会跳过{(Invalid == 1 ? "它们" : "它们")}）。" : null,
            Unsupported > 0 ? $"{Unsupported:N0} 项已跳过，此 GPU 不支持{(Unsupported == 1 ? "它们所需" : "它们所需")}的内容。" : null,
            NeedsRecording > 0 ? $"{NeedsRecording:N0} {(NeedsRecording == 1 ? "项" : "项")}需要在本机上录制：游戏或模组会在运行时构建{(NeedsRecording == 1 ? "其着色器" : "其着色器")}，因此它们不在任何游戏文件中。开启“游戏时录制”后游玩即可捕捉它们。" : null,
        }.OfType<string>());
    public bool HasSkipped => SkippedText != null;

    // Last play session (SessionStats, no timestamp): one bar, three segments, under the Record switch. Ready = from the
    // game's library or the driver cache; ray tracing state objects have their own line, so Requests isn't used.
    SessionStats? L => s.LastSession;
    public bool HasSession => L != null;
    public bool ShowSessionHint => L == null && CanRecord && SessionHint != null && !FramesOnly;
    public string? SessionHint => Format.SessionHint(s);
    public string SessionHeader => L is { } l ? $"上次游玩 · {Format.Duration(F?.Duration ?? l.Duration)}" : "";
    // ray tracing state objects included, so the headline agrees with the ray tracing line under it
    static long PlayCompiles(SessionStats l) => l.Compiles + l.StateObjectsCompiled;
    public string SessionVerdict
    {
        get
        {
            if (L is not { } l) return "";
            long play = PlayCompiles(l), started = l.StartupCompiles + l.StateObjectsStartupCompiled, ready = l.FromGameLibrary + l.CacheHits + l.StateObjectsReady;
            if (play + started == 0)
                return ready > 0 ? $"游玩期间无需编译任何内容：游戏用到的全部 {ready:N0} 个着色器组合都已就绪。" : "游玩期间无需编译任何内容。";
            // the longest compile call, of the PSOs only; whether a frame stuttered is the frame report's to say
            string worst = l.StateObjectsCompiled > 0 ? "" : play == 1 ? $"（耗时 {l.WorstCompileMs:0} 毫秒）" : $"（最长耗时 {l.WorstCompileMs:0} 毫秒）";
            return (play == 0 ? "游玩期间无需编译任何内容。" : $"游玩期间有 {play:N0} 项需要编译{worst}。")
                + (started > 0 ? $" {started:N0} 项在游戏启动或加载时编译。" : "")
                + (ready > 0 ? $" 其余 {ready:N0} 项已就绪。" : "");
        }
    }
    public bool HasStateObjectLine => L is { } l && l.StateObjectsReady + l.StateObjectsCompiled + l.StateObjectsStartupCompiled > 0;
    public string StateObjectLine => L is { } l ? $"光线追踪管线：{l.StateObjectsReady:N0} 条已就绪，{l.StateObjectsCompiled:N0} 条在游玩时编译"
        + (l.StateObjectsStartupCompiled > 0 ? $"，{l.StateObjectsStartupCompiled:N0} 条在游戏启动或加载时。" : "。") : "";
    public bool HasRayQueryLine => L?.RayQueryRecompiles > 0;
    public string RayQueryLine => L is { RayQueryRecompiles: > 0 and var n }
        ? $"{n:N0} 条光线追踪管线，驱动每次启动都会部分重新编译{(n == 1 ? "" : "")}。" : "";

    // The last launch's frame times (FrameLog): its shader stutters, other hitches and loading, the 1% low of play.
    public FrameReport? F => s.LastFrames;
    List<Hitch> ShaderHitches => F?.Hitches.Where(h => h.Cause == HitchCause.Shader).ToList() ?? [];
    public bool HasFrames => F != null;
    public string FramesVerdict => F is not { } f ? "" : Format.Frames(f, ShaderHitches, Unsplit);
    // a DirectX 11 session (the frames-only recorder's too) has no creates: its hitches aren't split by cause
    bool Unsplit => FramesOnly || F is { D3D11: true };
    public bool SplitFrames => !Unsplit;
    public string OtherLegend => Unsplit ? "卡顿" : "其他卡顿";
    public string GraphEnd => Format.Duration(F?.Duration);
    public string FramesSummary => F is not { } f ? ""
        : $"启动（游戏自身的着色器预编译和首次加载）：{Format.Duration(f.Startup)}。游玩中：{InPlay(f).Count(h => h.Ms >= 50):N0} 帧达到 50 毫秒以上，"
          + $"{InPlay(f).Count(h => h.Ms >= 100):N0} 帧达到 100 毫秒以上。帧时间只保留在本机上。";
    // the same list while the report is the same: a new one makes the slow-frames list rebuild every row at each refresh
    public IReadOnlyList<DetailRow> HitchRows => hitchRows.Report == F ? hitchRows.Rows
        : (hitchRows = (F, F?.Hitches.Select(h => new DetailRow($"{Clock(h.At)}  {(Unsplit && h.Cause == HitchCause.Other ? "卡顿" : Cause(h.Cause))}", Ms(h.Ms))).ToList() ?? [])).Rows;
    (FrameReport? Report, IReadOnlyList<DetailRow> Rows) hitchRows = (null, []);
    public static string HitchTip(Hitch h, bool neutral = false) => $"{Clock(h.At)} · {Ms(h.Ms)} · {(neutral ? "卡顿" : Cause(h.Cause))}";
    static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    public bool HasHitchRows => F?.Hitches.Count > 0;

    // Earlier launches with frame times (FrameSessions), newest first; the one the section shows is left out. Read again
    // only when the game's state changes.
    public IReadOnlyList<DetailRow> EarlierSessions => earlier.For == s ? earlier.Rows
        : (earlier = (s, Host.Core.FrameHistory(id).Where(e => e.LaunchUnixMs != F?.LaunchUnixMs).Select(e => SessionRow(e, FramesOnly)).ToList())).Rows;   // each row's own D3D11 decides the rest
    (GameState? For, IReadOnlyList<DetailRow> Rows) earlier = (null, []);
    public bool HasEarlierSessions => EarlierSessions.Count > 0;
    // a cleared recording takes the last launch's frames, not the history
    public bool HasFramesCard => HasFrames || HasEarlierSessions;
    static DetailRow SessionRow(FrameSession e, bool unsplit) => new(
        $"{Fmt.Time(DateTimeOffset.FromUnixTimeMilliseconds(e.LaunchUnixMs).LocalDateTime, "g")} · {Format.Duration(e.Duration)}",
        Format.Session(e, unsplit));

    // the frames of play, between startup and quitting, whatever slowed them (a load in play too)
    static IEnumerable<Hitch> InPlay(FrameReport f) => f.Hitches.Where(h => FrameLog.InPlay(h.At.TotalMilliseconds, h.Ms, f.Startup.TotalMilliseconds) && h.Cause != HitchCause.Quitting);
    static string Ms(double ms) => Format.Ms(ms);
    static string Cause(HitchCause c) => c switch { HitchCause.Shader => "着色器编译", HitchCause.Other => "其他卡顿", HitchCause.Quitting => "退出",
        HitchCause.LoadingShaders => "加载、编译着色器", _ => "加载" };

    /// <summary>The session bar's three segments, as star widths.</summary>
    public double LibraryWidth => L?.FromGameLibrary ?? 0;
    public double HitsWidth => L?.CacheHits ?? 0;
    public double CompilesWidth => L?.Compiles ?? 0;
    public double CompilesMinWidth => L?.Compiles > 0 ? 4 : 0;
    public string FromLibrary => Fmt.N(L?.FromGameLibrary);
    public string CacheHits => Fmt.N(L?.CacheHits);
    public string Compiles => Fmt.N(L?.Compiles);
    public string SessionLabel => L is { } l
        ? $"{l.Requests:N0} 次请求：{l.FromGameLibrary:N0} 条来自游戏自带的库，{l.CacheHits:N0} 条已在驱动缓存中，{l.Compiles:N0} 条在游玩时编译"
          + (l.StartupCompiles > 0 ? $"，{l.StartupCompiles:N0} 条在游戏启动或加载时" : "")
          + (l.RayQueryRecompiles > 0 ? $"，{l.RayQueryRecompiles:N0} 条驱动每次启动都会部分重新编译的光线追踪管线" : "")
          + (l.StateObjectsReady + l.StateObjectsCompiled + l.StateObjectsStartupCompiled > 0 ? $"。光线追踪管线：{l.StateObjectsReady:N0} 条已就绪，{l.StateObjectsCompiled:N0} 条在游玩时编译" : "")
          + (l.StateObjectsStartupCompiled > 0 ? $"，{l.StateObjectsStartupCompiled:N0} 条在游戏启动或加载时" : "")
        : "";

    public bool? RecordPending { get; set; }   // the state a recorder change that is running (off the UI thread) asked for
    // NVIDIA, DirectX 11: the recorder times frames and records nothing
    public bool FramesOnly => ScsKiller.Frames11Play(s.Engine, Host.Core.Vendor.Caps);
    // its switch waits for the pair to end
    bool PairFramesOnly => ScsKiller.Frames11(s.Engine, Host.Core.Vendor.Caps) && s.BeforeAfter is { Stage: not BeforeAfterStage.Done };
    public bool RecordOn => ShowOffline ? OfflineOn : !PairFramesOnly && (RecordPending ?? s.RecorderEffective);
    public bool CanToggleRecord => ShowOffline ? OfflinePending == null && !s.OfflineRunning
        : RecordPending == null && AlongsidePending == null && NoAntiCheat && s.RecorderSkip == null && !PairFramesOnly;
    public bool ShowUseDefault => RecordPending == null && s.RecorderOverride != RecorderOverride.Default && s.RecorderSkip == null && !PairFramesOnly && !s.RecorderForeign;
    public string UseDefaultText => $"使用默认（{(ScsKiller.RecordsByDefault(Host.Core.Settings, s) ? "开" : "关")}）";
    string RecorderDll => Fmt.RecorderDll(s);
    string Kept => ScsKiller.SessionKeptNote(s) is { } kept ? " " + kept : "";
    // another copy's recorder is said from the folder on every line the switch shows: the reconcile's note may not be there yet
    public string RecordNote => RecordNoteText is var t && s.RecorderForeign
        ? $"{t} {ScsKiller.ForeignHereNote}{(!ShowOffline && CanToggleRecord && !RecordOn ? $" {ScsKiller.TakeOverNote}." : "")}" : t;
    string RecordNoteText => ScsKiller.CrashNote(s.Game) is { } crash ? crash
        : Risk
        ? (s.AlwaysRecord ? "每次启动都会录制。" : "每场录制开始前都会请求确认。") + Kept
        : ScsKiller.AntiCheatHeld(s) ? ScsKiller.HeldRecordNote
        : ScsKiller.AntiCheatOpen(s) ? ScsKiller.AntiCheatOpenRecordNote
        : ShowOffline
        ? ScsKiller.OfflineSessionNote + Kept
        : s.RiskNoLaunch ? ScsKiller.NoLaunchNote(Fmt.StoreName(s.Game))
        : !NoAntiCheat
        ? Host.Platform.RecordAntiCheatGames ? ScsKiller.RiskNotHere(s) ?? "不可用：该游戏含反作弊，只能依据其文件编译。" : NoAntiCheatSessions
        : PairFramesOnly ? "仅记录帧时间，用于“之前/之后”录制。"
        : s.RecorderSkip == ScsKiller.SkipManual ? "确认游戏文件夹（上方“游戏文件夹…”）后才可用：SCSKiller 录制前会检查整个文件夹的反作弊。"
        : s.RecorderSkip == ScsKiller.SkipModNotChainable ? $"不可用：{ScsKiller.NotChainableReason(s.RecorderMod, RecorderDll)}。"
        : s.RecorderSkip == ScsKiller.SkipAgsMod && s.AgsMod is { } agsMod && !(agsMod == "ReShade" && s.ShaderModLayer && s.ShaderModAsD3D12) ? $"不可用：{ScsKiller.SkipAgsMod}。{ScsKiller.AgsModHint(agsMod)}"   // else ShaderModNote says it
        : s.RecorderSkip is { } skip ? $"不可用：{skip}。"
        : (s.Engine is { } vk && Core.Vulkan.VulkanPlan.IsVulkan(vk) ? ScsKiller.VulkanRecorderNote
            : FramesOnly ? $"在游戏旁加入一个小的 {Fmt.RecorderFiles(s)}，仅为上方图表记录每帧时间，不录制其他内容。"
            : $"在游戏旁加入一个小的 {Fmt.RecorderFiles(s)}，补录方案遗漏的管线并记录每帧时间，让本页显示什么在卡顿。")
          + " 可随时移除。如果游戏含有 SCSKiller 未检测到的反作弊，请勿开启。"
          + (s.RecorderNote is { } note && note != s.RecorderFailure && note != ScsKiller.ForeignNote ? $" ({Sentence(note)})" : "")
          + (s.RecorderRefused is { } why && !(s.Status == GameStatus.NeedsRecording && ScsKiller.NeverRecorded(s)) ? " " + why : "");   // else the status says it

    // A Flatpak launcher whose sandbox can't see the recorder folder: one confirmed override lets it read that folder
    public string? GrantLauncher => Host.Core.FlatpakGrantLauncher(id);
    public bool GrantWrites => Host.Core.FlatpakGrantWrites(id);
    public bool ShowGrant => GrantNote == null && GrantLauncher != null;
    public string GrantText => GrantLauncher is { } l ? ScsKiller.FlatpakGrantButton(l) : "";
    public string? GrantNote { get; private set; }
    public bool HasGrantNote => GrantNote != null;
    public async Task GrantAsync()
    {
        if (GrantLauncher is not { } launcher) return;
        var why = await Task.Run(() => Host.Core.GrantFlatpak(id));
        if (why == null) GrantNote = ScsKiller.FlatpakGrantDone(launcher);
        else Error = "未更改：" + why;
        Changed();
        RefreshGame();
    }

    // A mod's d3d12.dll where the recorder goes (ReShade, a wrapper): off = the game isn't recorded; on = the recorder chains to it
    public bool HasMod => s.RecorderMod != null && s.RecorderSkip is not (ScsKiller.SkipAntiCheat or ScsKiller.SkipShaderMod or ScsKiller.SkipManual or ScsKiller.SkipUnsupported or ScsKiller.SkipNotDx12 or ScsKiller.SkipStreamline or ScsKiller.SkipCrashes);
    public string AlongsideTitle => $"与 {s.RecorderMod} 共存录制";
    public string AlongsideNote => ScsKiller.IsOptiScaler(s.RecorderMod) ? ScsKiller.OptiAlongsideNote
        : $"记录器放入期间，{s.RecorderMod} 的 d3d12.dll 会被改名，移除时原样恢复。";
    public bool? AlongsidePending { get; set; }
    public bool AlongsideOn => AlongsidePending ?? s.RecordAlongsideMod;
    public bool CanToggleAlongside => AlongsidePending == null && RecordPending == null;

    // An eligible EasyAntiCheat game (Games.OfflineEac) or an anti-cheat game allowed in Settings (RiskEligible): the card's
    // switch allows sessions, each confirmed before it starts
    public bool ShowOffline => s.OfflineEligible || s.RiskEligible;
    public bool Risk => s.RiskEligible;
    public bool? OfflinePending { get; set; }
    public bool OfflineOn => OfflinePending ?? s.OfflineRecord;
    public bool ShowOfflineOn => ShowOffline && OfflineOn;
    public bool OfflineStarting { get; set; }
    public bool CanStartOffline => s.OfflineRecord && OfflinePending == null && !s.OfflineRunning && !s.Playing && !OfflineStarting;
    // a game a session is offered for, with its switch on: every launch records without the per-launch confirmation
    public bool ShowAlways => Risk && OfflineOn;
    public bool? AlwaysPending { get; set; }
    public bool AlwaysOn => AlwaysPending ?? s.AlwaysRecord;
    public bool CanToggleAlways => AlwaysPending == null && OfflinePending == null && !OfflineStarting;
    public bool ShowSessionButton => !(Risk && AlwaysOn) || s.OfflineRunning;
    public string OfflineButtonText => Risk
        ? s.OfflineRunning ? "录制场次进行中…" : "录制一场（风险自负）"
        : s.OfflineRunning ? "离线场次进行中…" : "离线运行，不用 EasyAntiCheat（风险自负）";
    public string OfflineRiskText => Risk ? RiskText(s) : OfflineRisk;
    public const string OfflineRisk = "离线启动游戏并跳过 EasyAntiCheat。游戏退出后 SCSKiller 会移除其文件（若崩溃：下次登录时移除）。"
        + "只有当下方按钮重新显示“离线运行，不用 EasyAntiCheat（风险自负）”后才能联机游玩，在此之前联机风险自负。Steam 必须处于运行状态。场次进行中请勿卸载 SCSKiller。";
    public static string RiskText(GameState g) => $"{(ScsKiller.AntiCheatOpen(g) ? "其反作弊检查未完成" : $"该游戏含有 {ScsKiller.AntiCheatName(g)}")}。若启用记录器，风险由你自行承担。"
        + "如果账号因此被封，SCSKiller 概不负责。";
    public string? SessionNote => s.SessionNote;
    public bool HasSessionNote => s.SessionNote != null;
    public string? RecorderFailure => s.RecorderFailure is { } f ? $"记录器{f}。" : null;
    public bool HasRecorderFailure => s.RecorderFailure != null;

    public bool HasRecording => s.RecordingBytes > 0;
    public string RecordingSize => $"录制占用 {Format.Bytes(s.RecordingBytes)}（含游戏文件夹中的和 SCSKiller 的副本）";
    public bool RecordingPaused => s.RecordingPaused;
    public string RecordingPausedText => ScsKiller.PausedNote(Host.Core.Settings) + "。可在设置中提高上限，或清除录制。";
    public bool ClearingRecording { get; set; }
    public bool CanClearRecording => !ClearingRecording && !s.Playing;
    public string ClearRecordingTip => s.Playing ? "请先关闭游戏" : "删除已录制的管线。记录器保留在游戏中";

    public bool Playing => s.Playing;
    public bool Refreshing { get; private set; }
    public bool CanRefresh => !Refreshing;
    public async void RefreshGame()
    {
        if (Refreshing) return;
        Refreshing = true;
        Changed();
        try { await Task.Run(() => Host.Core.RefreshGame(id)); Error = null; }
        catch (Exception e) { Error = e.Message; }
        Refreshing = false;
        ReadCaches();
        Refresh();
    }
}

/// <summary>A label and its value (the game page's sources and Details); Note: a line under the label.</summary>
public sealed record DetailRow(string Label, string Value, string? Note = null)
{
    public bool HasNote => Note != null;
}
