using SCSKiller.Core;
using SCSKiller.Core.App;

namespace SCSKiller.Ui.Models;

/// <summary>A notification as each OS shows it: its two lines, the arguments a click on its body hands to the app, and
/// its buttons. <paramref name="Lifetime"/>: gone from the notification list after it.</summary>
public sealed record Toast(string Title, string Text, IReadOnlyList<KeyValuePair<string, string>> Args, IReadOnlyList<ToastButton> Buttons,
    TimeSpan? Lifetime = null);

public sealed record ToastButton(string Label, IReadOnlyList<KeyValuePair<string, string>> Args);

/// <summary>The app's notifications, as the WinUI app words them. Clicks reach Activation with their arguments.</summary>
public static class Toasts
{
    static KeyValuePair<string, string> Arg(string key, string value) => new(key, value);

    // Toast.dc.html. Windows draws the buttons, so "Compile now" can't be accent-coloured.
    public static Toast Driver(IScsKiller core, IReadOnlyList<GameState> stale)
    {
        var time = TimeSpan.FromTicks(stale.Sum(g => (g.EstimatedWarmTime ?? TimeSpan.Zero).Ticks));
        string games = stale.Count == 1 ? "1 个游戏需要" : $"{stale.Count} 个游戏需要";
        return new($"{Fmt.Vendor(core.Vendor.Vendor)} 驱动已更新",
            $"驱动 {core.Vendor.Gpu.DriverVersion} 清除了着色器缓存。{games}重新编译：" +
            $"{string.Join("、", stale.Select(g => g.Game.Name))}" + (time > TimeSpan.Zero ? $"，大约 {Format.Duration(time)}。" : "。"),
            [Arg("action", "open")],
            [new("立即编译", [Arg("action", "now")]), new("空闲时", [Arg("action", "idle")]), new("跳过", [Arg("action", "skip")])]);
    }

    public static Toast NewShaders(IReadOnlyList<GameState> games, bool compiling = false)
    {
        var ids = string.Join('|', games.Select(g => g.Game.Id));
        var n = SCSKiller.Core.App.NewShaders.Count(games[0]);
        var one = $"{games[0].Game.Name} 有 {n:N0} 条新管线{(n == 1 ? "" : "")}。";
        var (title, text) = compiling
            ? ("正在编译新着色器", $"{(games.Count == 1 ? one : $"{games.Count} 个游戏有新着色器。")} {SCSKiller.Core.App.NewShaders.When(Host.Core.Settings)}")
            : ("有新着色器待编译", games.Count == 1 ? $"{one} 立即编译，避免卡顿。" : $"{games.Count} 个游戏有新着色器待编译");
        return new(title, text, [Arg("action", "shaders-show"), Arg("games", ids)],
            [new("立即编译", [Arg("action", "shaders-compile"), Arg("games", ids)]), new("查看", [Arg("action", "shaders-show"), Arg("games", ids)])]);
    }

    /// <summary>An unread news item: its title and the first line of its body. A click opens the bell.</summary>
    public static Toast News(NewsItem item) => new(item.Title, NewsState.FirstLine(item.Body), [Arg("action", "news")], []);

    public static Toast NewsMore(int n) => new("SCSKiller 新闻", NewsState.MoreLine(n), [Arg("action", "news")], []);

    /// <summary>The close button hid the window to the notification area, the first time ever.</summary>
    public static Toast TrayNotice { get; } = new("SCSKiller 仍在运行", "它位于通知区域，右键点击图标即可退出。",
        [Arg("action", "open")], []);

    /// <summary>A compile the user started finished or failed (<see cref="ScsKiller.EndedNotice"/>): a click opens its page.</summary>
    public static Toast Ended(string gameId, (string Title, string Text) n) => new(n.Title, n.Text, [Arg("action", "shaders-show"), Arg("games", gameId)], []);

    /// <summary>A step the user takes in another app, shown over it, stale after a few minutes.</summary>
    public static Toast Hint(string title, string text) => new(title, text, [], [], TimeSpan.FromMinutes(5));
}
