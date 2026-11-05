using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SCSKiller.Core;
using SCSKiller.Core.App;
using SCSKiller.Ui.Controls;
using SCSKiller.Ui.Models;

namespace SCSKiller.Ui.Pages;

public sealed partial class SettingsPage : UserControl
{
    public SettingsVm Vm { get; } = new();
    public AccountVm Patreon { get; } = new();
    readonly Coalesced refresh;

    public SettingsPage()
    {
        InitializeComponent();
        DataContext = Vm;
        // before the bindings subscribe: a new channel list (sign-in, sign-out, a lapse) moves the selection by itself,
        // synchronously or a little later, and none of that is a pick. Other refreshes keep the list and every pick.
        Patreon.PropertyChanged += (_, _) =>
        {
            if (refreshing || ReferenceEquals(Patreon.ChannelLabels, shownChannels)) return;
            shownChannels = Patreon.ChannelLabels;
            refreshing = true;
            Dispatcher.UIThread.Post(() => refreshing = false, DispatcherPriority.Background);
        };
        AccountPart.DataContext = Patreon;
        RemoveRecordersPart.IsVisible = !OperatingSystem.IsMacOS();   // no recorder there yet
        refresh = new(Host.Ui, Vm.GamesChanged);   // reconcile changes the recorder count
        Vm.PropertyChanged += (_, _) => Patreon.Refresh();   // e.g. the share checkbox ticked: the prompt goes
        // a new channel list (sign-in) resets the selection after the index was read: show the chosen channel again
        Patreon.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(() => ChannelBox.SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, Patreon.ChannelIndex),
            DispatcherPriority.Background);
        Cards.SizeChanged += (_, e) => { if (e.WidthChanged) FitCards(); };
        PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) FitCards(); };
    }

    // two cards side by side need about 26 lines of text width each, so a large Text size stacks them
    void FitCards()
    {
        bool narrow = (Cards.Bounds.Width - Cards.ColumnSpacing) / 2 < FontSize * 26;
        if (narrow == Scroller.Classes.Contains("Narrow")) return;
        Scroller.Classes.Set("Narrow", narrow);
        Cards.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,*");
        Cards.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto,Auto,Auto" : "Auto,Auto");
        for (int i = 0; i < Cards.Children.Count; i++)
        {
            Grid.SetColumn(Cards.Children[i], narrow ? 0 : i % 2);
            Grid.SetRow(Cards.Children[i], narrow ? i : i / 2);
        }
    }

    void OnChanged(GameState _) => refresh.Request();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Patreon.Watch(true);
        Vm.Watch(true);
        Host.Core.GameChanged += OnChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Patreon.Watch(false);
        Vm.Watch(false);
        Host.Core.GameChanged -= OnChanged;
    }

    void OnSignIn(object? _, RoutedEventArgs __) => AccountVm.SignIn();
    void OnCancelSignIn(object? _, RoutedEventArgs __) => AccountVm.CancelSignIn();
    void OnRefreshAccount(object? _, RoutedEventArgs __) => AccountVm.RefreshStatus();
    void OnSignOut(object? _, RoutedEventArgs __) => AccountVm.SignOut();
    void OnBackToStable(object? _, RoutedEventArgs __) => AccountVm.BackToStable();

    bool refreshing;
    object? shownChannels;
    void OnChannelPicked(object? _, SelectionChangedEventArgs __)
    {
        if (!refreshing) Patreon.ChannelIndex = ChannelBox.SelectedIndex;
    }

    /// <summary>--screenshots: a pick as UI Automation makes it, without focus or the drop-down.</summary>
    public void PickChannel(int index) => ChannelBox.SelectedIndex = index;

    void OnShare(object? _, RoutedEventArgs __) => Answer(share: true);
    void OnNotNow(object? _, RoutedEventArgs __) => Answer(share: false);
    void Answer(bool share)
    {
        AccountVm.AnswerSharing(share);
        Vm.Refresh();   // the share checkbox; its PropertyChanged refreshes the prompt too
    }

    static Task<bool> Confirm(string title, string text, string yes) => App.ConfirmAsync(title, text, yes);
    async void OnApply(object? _, RoutedEventArgs __) => await Vm.ApplyLimitAsync(Confirm);
    async void OnApplyAuto(object? _, RoutedEventArgs __) => await Vm.ApplyAutoAsync(Confirm);
    async void OnShaderCleanup(object? _, RoutedEventArgs __) => await Vm.ApplyShaderCleanupAsync();

    // Stands in for the website's recorder-anti-cheat page until it exists.
    public static ContentDialog AntiCheatDetailsDialog() => new()
    {
        Title = "在带反作弊的游戏中记录", CloseButtonText = "关闭",
        Content = "在单次场次中，SCSKiller 会把它的记录器（一个 d3d12.dll）放到游戏的 exe 旁边。\n\n"
            + "游戏退出时，SCSKiller 会移除它放置的所有文件；如果发生崩溃，则在你下次登录时移除。\n\n"
            + "在带反作弊的游戏中启用记录器需自担风险。SCSKiller 不对封禁负责。",
    };

    async void OnAntiCheatDetails(object? _, RoutedEventArgs __) => await App.ShowAsync(AntiCheatDetailsDialog());

    async void OnRemoveRecorders(object? _, RoutedEventArgs __)
    {
        if (Host.Core is not ScsKiller real || !await App.ConfirmAsync("移除记录器？",
                "SCSKiller 会从每个游戏中移除它的记录器，然后退出。请先关闭你的游戏：正在运行的游戏会保留其记录器。SCSKiller 再次启动后记录会继续。", "移除并退出")) return;
        var removed = await Task.Run(real.RemoveRecorders);
        string[] left = [.. removed.Games.Where(r => r.Left != null).Select(r => $"{real.Games.FirstOrDefault(s => s.Game.Id == r.GameId)?.Game.Name ?? r.Folder}: {r.Left}"),
            .. removed.VulkanLeft is { } vulkan ? [$"Vulkan 记录器的注册项：{vulkan}"] : Array.Empty<string>()];
        if (left.Length == 0)
        {
            // a test identity's toast registration goes at quit
            ScsKiller.RemoveTraces(Program.Identity.Production ? Environment.ProcessPath : null, m => real.Log?.Report(m));
            App.Quit();
            return;
        }
        // SCSKiller stays open, so the user can remove them again once a game is closed
        await App.ShowAsync(new ContentDialog
        {
            Title = "部分记录器仍在原处",
            Content = new TextBlock { Text = string.Join("\n", left) + "\n\n关闭此处列出的所有游戏，然后再次选择移除记录器。", TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
        });
    }

    /// <summary>Also --screenshots: the games whose AMD cache is kept aside, each with its size and a Remove.</summary>
    public ContentDialog ManageKeptDialog()
    {
        var rows = new StackPanel { Spacing = 10 };
        foreach (var g in SettingsVm.KeptGames)
        {
            var row = new Grid { ColumnSpacing = 12, ColumnDefinitions = new("*,Auto") };
            row.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = g.Game.Name, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = Fmt.KeptSize(g.Kept!), Classes = { "Secondary" } },
                },
            });
            var remove = new Button { Content = "移除", VerticalAlignment = VerticalAlignment.Center };
            remove.Click += (_, _) => { Host.Core.DropKeptCache(g.Game.Id); rows.Children.Remove(row); Vm.Refresh(); };
            Grid.SetColumn(remove, 1);
            row.Children.Add(remove);
            rows.Children.Add(row);
        }
        return new()
        {
            Title = "另行保留", CloseButtonText = "关闭",
            Content = new StackPanel
            {
                Spacing = 14, MinWidth = 420,
                Children =
                {
                    new TextBlock { Text = "移除可释放空间。该游戏在下次编译后会被再次保留。", TextWrapping = TextWrapping.Wrap },
                    new ScrollViewer { MaxHeight = 360, Content = rows },
                },
            },
        };
    }

    async void OnManageKept(object? _, RoutedEventArgs __) => await App.ShowAsync(ManageKeptDialog());

    public void ScrollToEnd() => Scroller.ScrollToEnd();
    /// <summary>The Patreon card at the top.</summary>
    public void ScrollToPatreon() => ScrollTo(PatreonCard, -16);
    /// <summary>--screenshots: how far the page is scrolled.</summary>
    public double ScrolledBy => Scroller.Offset.Y;
    public void ScrollToNewShaders() => ScrollTo(NewShadersHeader, 40);
    public void ScrollToTraversal() => ScrollTo(TraversalLabel, 300);
    public void ScrollToCompactUi() => ScrollTo(CompactUiBox, 300);

    // WinUI's BringIntoView at the top with a VerticalOffset: the element <offset> px below the viewport's top (above it when negative)
    void ScrollTo(Control element, double offset)
    {
        if (element.TranslatePoint(new(0, 0), (Control)Scroller.Content!) is { } at)
            Scroller.Offset = new(0, Math.Clamp(at.Y - offset, 0, Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height)));
    }
}
