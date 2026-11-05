using System.Collections.ObjectModel;
using SCSKiller.Core;
using SCSKiller.Core.App;
using SCSKiller.Core.Vendors;

namespace SCSKiller.Ui.Models;

/// <summary>A queue row: the running item (1), a waiting item (2..) or a finished one (Number 0).</summary>
public sealed record QueueRow(string Id, int Number, string Name, string Exe, string Note, string Time, string Cache, QueueStage Stage,
    bool First = false, bool Last = false)
{
    public object? Icon { get; } = Host.Icons.Get(Exe);   // a field: an icon that failed makes the row unequal, so Sync replaces it
    public bool NoIcon => Icon is null;
    /// <summary>The initials' tile colour, ARGB.</summary>
    public uint Tile => GameRow.TileOf(Name);
    public string Initials => Name.Length > 0 ? GameRow.InitialsOf(Name) : "";
    public bool CanUp => !First;
    public bool CanDown => !Last;
    public string TopLabel => $"将 {Name} 移到最前";
    public string BottomLabel => $"将 {Name} 移到最后";
    public string UpLabel => $"将 {Name} 上移";
    public string DownLabel => $"将 {Name} 下移";
    public string RemoveLabel => $"从队列中移除 {Name}";
    public override string ToString() => Number > 0 ? $"{Number}. {Name}, {Note}, {Time}, {Cache}" : $"{Name}, {Note}";   // screen readers
}

public sealed class QueueVm : Bindable
{
    QueueItem? cur;
    long planned;
    List<(string Game, long Bytes)> growth = [];
    List<(long Growth, long Estimate)> sizes = [];
    (CacheUsage Usage, CacheLimit? Limit, long? Capped)? driver;
    readonly BackgroundRead<(CacheUsage, CacheLimit?, long?)> cache;

    public QueueVm() => cache = new("Queue cache", () => (Host.Core.Vendor.GetCacheUsage(), Host.Core.Vendor.GetCacheLimit(), (Host.Core.Vendor as AmdBackend)?.AppCache.DxcBytes()),
        c => { driver = c; ShowCache(); Changed(); });
    static Settings S { get => Host.Core.Settings; set => Host.Core.Settings = value; }

    public ObservableCollection<QueueRow> Waiting { get; } = [];
    public ObservableCollection<QueueRow> Finished { get; } = [];
    /// <summary>Folds the queue doesn't run (the app's start, the game's exit): the compile's last step.</summary>
    public ObservableCollection<QueueRow> Folds { get; } = [];
    public bool HasFolds => Folds.Count > 0;
    public bool Dragging { get; set; }   // don't rebuild Waiting under a drag in progress

    public QueueRow Current { get; private set; } = new("", 1, "", "", "", "", "", QueueStage.Waiting);
    public bool HasCurrent => cur != null;
    public bool Idle => cur == null && !Host.Core.QueueRunning;   // a running queue's button is Stop, also while it waits to start an item
    public bool CanStop => cur != null || Host.Core.QueueRunning;
    public bool Empty => cur == null && Waiting.Count == 0 && Folds.Count == 0;
    public bool HasFinished => Finished.Count > 0;
    public bool CanClear => Waiting.Count > 0 || Finished.Count > 0;
    public bool CanStart => !Host.Core.QueueRunning && Waiting.Count > 0;
    public bool CanRaiseLimit => ScsKiller.CanRaiseCacheLimit(Host.Core.Vendor.Caps, driver?.Limit);
    public string Summary { get; private set; } = "";
    public string? PlanChecks { get; private set; }   // ScsKiller.PlanCheckLine
    public bool HasPlanChecks => PlanChecks != null;
    public bool CanCheckNow => !Host.Core.Compiling && Host.Core.Queue.Any(q => q is { PlanCheck: true, Note: ScsKiller.WhenIdleNote });

    public string CurrentDetail => cur is null ? "" : Fmt.Stage(cur) + (Fmt.ProgressText(cur) is { } p ? " · " + p : "") + (Fmt.Started(cur) is { } at ? " · " + at : "");
    public bool Indeterminate => Fmt.Indeterminate(cur);
    public double Percent => Fmt.Percent(cur);
    public string PauseText => cur?.Stage == QueueStage.Paused ? "继续" : "暂停";
    public bool CacheWarn { get; private set; }
    public string CacheWarnText { get; private set; } = "";
    public string RuntimeWarnText { get; private set; } = "";
    public bool RuntimeWarn => RuntimeWarnText.Length > 0;

    public double Threads { get => Used; set { if ((int)value != Used) { S = S with { Threads = (int)value }; Changed(); } } }
    static int Used => ScsKiller.UsedThreads(S.Threads);
    public string ThreadsText => $"{Used} / {Environment.ProcessorCount}";
    public double MaxThreads => Environment.ProcessorCount;
    public int Priority { get => (int)S.Priority; set { if (value >= 0 && value != (int)S.Priority) { S = S with { Priority = (WarmPriority)value }; Changed(); } } }
    public string CpuSummary => $"CPU 占用：{Environment.ProcessorCount} 个线程中的 {Used} 个，{(S.Priority == WarmPriority.BelowNormal ? "低于正常" : "空闲")}优先级";

    public void Refresh()
    {
        var core = Host.Core;
        var all = core.Queue;
        var queue = all.Where(q => !q.PlanCheck).ToList();
        PlanChecks = ScsKiller.PlanCheckLine(all);
        var games = core.Games.ToDictionary(g => g.Game.Id);

        TimeSpan? Left(QueueItem q) => Fmt.Left(q, games.GetValueOrDefault(q.GameId));
        QueueRow Row(QueueItem q, int number, bool first = false, bool last = false)
        {
            var g = games.GetValueOrDefault(q.GameId);
            var time = q.Stage == QueueStage.Done ? Format.Duration(g?.LastWarmTime) : Left(q) is { } t ? Format.Duration(t) + (q.Progress?.PerSecond > 0 ? "剩余" : "") : Format.Dash;
            return new(q.GameId, number, g?.Game.Name ?? q.GameId, g?.Game.ExePath ?? "", NoteOf(q, g), time, g is null ? Format.Dash : Fmt.Cache(g), q.Stage, first, last);
        }

        (cur, var waiting) = Fmt.RunOrder(queue);
        if (cur != null) Current = Row(cur, 1);
        int first = cur != null ? 2 : 1;
        if (!Dragging) Sync(Waiting, waiting.Select((q, i) => Row(q, first + i, i == 0, i == waiting.Count - 1)));
        Sync(Finished, queue.Where(q => !Fmt.Active(q)).Select(q => Row(q, 0)));
        Sync(Folds, Fmt.FoldingGames.Select(games.GetValueOrDefault).OfType<GameState>()
            .Select(g => new QueueRow(g.Game.Id, 0, g.Game.Name, g.Game.ExePath, Fmt.FoldLine(g.Game.Id) ?? "", Format.Dash, Fmt.Cache(g), QueueStage.Merging)));

        var pending = queue.Where(Fmt.Active).ToList();
        var total = TimeSpan.FromTicks(pending.Sum(q => Left(q)?.Ticks ?? 0));
        bool unknown = pending.Any(q => Left(q) is null && !ScsKiller.Stalled(q));
        Summary = pending.Count == 0 ? "队列中没有项目"
            : $"{pending.Count} 个游戏{(pending.Count == 1 ? "" : "")} · "
              + (total > TimeSpan.Zero ? $"总计约 {Format.Duration(total)}{(unknown ? "，另有待构建的方案" : "")}" : "构建方案后才知预计时间")
              + (cur == null && !core.QueueRunning ? " · 未开始" : "");

        planned = pending.Sum(q => games.GetValueOrDefault(q.GameId)?.EstimatedCacheBytes ?? 0);
        var queued = pending.Select(q => games.GetValueOrDefault(q.GameId)).OfType<GameState>().ToList();
        RuntimeWarnText = ScsKiller.QueueRuntimeWarning(queued);
        growth = [.. queued.Select(g => (g.Game.Name, ScsKiller.CacheGrowth(g)))];
        sizes = [.. queued.Select(g => (ScsKiller.CacheGrowth(g), g.EstimatedCacheBytes ?? 0))];
        ShowCache();
        Changed();
        cache.Request();
    }

    void ShowCache()
    {
        if (driver is not { } d) return;   // not read yet
        var vendor = GpuBackends.OwnDriver(Host.Core.Vendor);   // RADV has no AMD driver cache
        CacheWarnText = (d.Capped is { } used
            ? ScsKiller.LargeCompileWarning(sizes, vendor) ?? AmdAppCache.QueueWarning(used, growth)   // one game over the cap is always over what's free too
            : ScsKiller.QueueCacheWarning(sizes, d.Usage.BytesOnDisk, planned, d.Limit?.Bytes, vendor, Host.Core.Vendor.Caps.PerGameCacheCap != null)) ?? "";
        CacheWarn = CacheWarnText.Length > 0;
    }

    static void Sync(ObservableCollection<QueueRow> target, IEnumerable<QueueRow> rows)
    {
        var list = rows.ToList();
        if (list.SequenceEqual(target)) return;
        target.Clear();
        foreach (var r in list) target.Add(r);
    }

    static string NoteOf(QueueItem q, GameState? g) => q.Stage switch
    {
        QueueStage.Failed => "失败：" + q.Error,
        QueueStage.Stopped when (Fmt.FoldLine(q.GameId) ?? (ScsKiller.NotReached(g?.StatusReason) ? Format.Sentence(g!.StatusReason) : null)) is { } fold => fold,
        QueueStage.Stopped when q.Progress is { } p => $"停止于 {p.Done:N0} / {p.Total:N0}：重新添加以继续" + (q.Note is { } n ? " · " + n : ""),
        QueueStage.Stopped => "已停止",
        QueueStage.Done => q.Note is { } n ? "完成 · " + n : "完成",
        _ when q.Note != null => q.Note,
        _ => g?.Plan is { } plan ? $"{plan.Recorded + plan.Generated + plan.MiddlewareItems:N0} 条管线{(plan.D3D11Shaders > 0 ? $" + {plan.D3D11Shaders:N0} 个 DirectX 11 着色器" : "")}" : "将先构建方案",
    };

    public void PauseOrResume() => Fmt.PauseOrResume(cur);

    /// <summary>The waiting order after a drag: the <paramref name="moved"/> rows, in their order, go to the gap before row
    /// <paramref name="gap"/> of <paramref name="order"/> (its count: the end).</summary>
    public static List<string> Dropped(IReadOnlyList<string> order, IReadOnlySet<string> moved, int gap)
    {
        var rest = order.Where(id => !moved.Contains(id)).ToList();
        rest.InsertRange(order.Take(gap).Count(id => !moved.Contains(id)), order.Where(moved.Contains));
        return rest;
    }
}
