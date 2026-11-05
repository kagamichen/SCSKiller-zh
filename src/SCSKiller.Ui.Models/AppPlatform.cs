namespace SCSKiller.Ui.Models;

/// <summary>What the app offers where the OSes differ, as the view models show it: the UI sets <see cref="Host.Platform"/>
/// from its Platform/Features.*.cs, a test sets its own. The stores and GPUs are ScsKiller.CreateDefault's and
/// GpuBackends.For's on that OS.</summary>
public sealed record AppPlatform
{
    /// <summary>The OS, as the texts name it.</summary>
    public required string Name { get; init; }
    /// <summary>The stores the scan reads, in the first scan's order.</summary>
    public required IReadOnlyList<string> Stores { get; init; }
    /// <summary>The stores whose game updates have the game read again.</summary>
    public required string UpdatingStores { get; init; }
    /// <summary>The theme choice that follows the system's.</summary>
    public required string SystemTheme { get; init; }
    /// <summary>What builds a game's pipelines besides the driver.</summary>
    public required string Runtime { get; init; }
    /// <summary>Games added by hand (ManualSource).</summary>
    public bool AddGames { get; init; }
    /// <summary>Folders of games added by hand (ManualSource's folders).</summary>
    public bool GameFolders { get; init; }
    /// <summary>The add dialog's line on which exe a game added by hand takes; null: none.</summary>
    public string? AddGamesNote { get; init; }
    public bool StartAtSignIn { get; init; }
    /// <summary>The notification-area icon: closing the window can leave SCSKiller running. On Linux only while a
    /// StatusNotifierItem host shows it, otherwise closing quits.</summary>
    public bool Tray { get; init; }
    /// <summary>The driver-update setting's sign-in and idle check, which notifies or compiles (ScheduledTask).</summary>
    public bool DriverUpdateModes { get; init; }
    /// <summary>The Library's line for the "When idle" rebuild after a driver update.</summary>
    public string IdleRebuildNote { get; init; } = "在你离开电脑时自动重新编译";
    /// <summary>NVIDIA App's Automatic Shader Compilation, a setting of the Windows driver.</summary>
    public bool AutoShaderCompilation { get; init; }
    /// <summary>The OS's own shader cache of each game (Windows' D3DSCache), which clearing a game's caches deletes too.</summary>
    public bool SystemShaderCache { get; init; }
    /// <summary>Sessions that record games with anti-cheat (Settings.RecordAntiCheatGames, IScsKiller.StartRiskSession).</summary>
    public bool RecordAntiCheatGames { get; init; }
    /// <summary>The empty library's line, read when the library is empty; null: no empty state.</summary>
    public Func<string>? NoGamesNote { get; init; }
    /// <summary>The game page's line on the cache SCSKiller puts in Steam's own folder (IScsKiller.SteamCacheNote).</summary>
    public bool SteamCacheNote { get; init; }
    /// <summary>The Cache page's line under where the driver keeps its cache.</summary>
    public string CacheFolderNote { get; init; } = "游戏从此处读取缓存，新缓存也写入此处。驱动没有更改此位置的设置。";

    public static readonly AppPlatform Windows = new()
    {
        Name = "Windows", Stores = ["Steam", "Epic", "Xbox", "EA", "GOG", "Ubisoft Connect", "Battle.net", "PURPLE", "HoYoPlay", "GRYPHLINK", "WeGame", "Gaijin", "Riot Client"],
        UpdatingStores = "Steam 或 Xbox 应用", SystemTheme = "使用 Windows 设置", Runtime = "Windows",
        AddGames = true, GameFolders = true, StartAtSignIn = true, Tray = true, DriverUpdateModes = true, AutoShaderCompilation = true, SystemShaderCache = true,
        RecordAntiCheatGames = true,
    };

    /// <summary><paramref name="steam"/>: a Steam is installed, the native one or the Flatpak or Snap one.</summary>
    public static AppPlatform Linux(Func<bool> steam) => new()
    {
        // the flags left off are features Linux doesn't have yet, each turned on once it does
        Name = "Linux", Stores = ["Steam", "Heroic", "Lutris", "Faugus"], UpdatingStores = "Steam", SystemTheme = "使用系统设置", Runtime = "Proton", Tray = true, StartAtSignIn = true, DriverUpdateModes = true,
        NoGamesNote = () => steam() ? "SCSKiller 会列出你通过 Steam、Heroic、Lutris 或 Faugus 安装的 Windows 游戏，以及你通过 Proton 添加到 Steam 的游戏。"
            : "SCSKiller 会列出 Steam、Heroic、Lutris 和 Faugus 的 Windows 游戏。",
        // no input idle time: the background compile starts at once and pauses only for a game
        IdleRebuildNote = "在后台重新编译，游戏运行时暂停",
        SteamCacheNote = true, RecordAntiCheatGames = true, AddGames = true,
        CacheFolderNote = "游戏从此处读取缓存，新缓存也写入此处。Steam 为其游戏选择此文件夹，其余游戏使用驱动的默认文件夹。",
        AddGamesNote = "游戏的 exe 需要位于其 Wine prefix 内。",
    };

    /// <summary>The UI on the Linux build's core, with no game scan, compile, recorder or Vulkan layer yet.</summary>
    public static readonly AppPlatform MacOS = new()
    {
        Name = "macOS", Stores = ["Steam"], UpdatingStores = "Steam", SystemTheme = "使用系统设置", Runtime = "游戏自带的运行时",
        NoGamesNote = () => "在 macOS 上尚不支持查找和编译游戏。",
    };
}
