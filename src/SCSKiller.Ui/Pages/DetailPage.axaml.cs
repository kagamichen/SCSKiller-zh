using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using SCSKiller.Core;
using SCSKiller.Core.App;
using SCSKiller.Core.Games;
using SCSKiller.Core.Vendors;
using SCSKiller.Ui.Controls;
using SCSKiller.Ui.Models;

namespace SCSKiller.Ui.Pages;

public sealed partial class DetailPage : UserControl, INavigable
{
    public DetailVm Vm { get; private set; } = null!;
    readonly Coalesced refresh, progress;

    public DetailPage()
    {
        InitializeComponent();
        refresh = new(Host.Ui, () => Vm.Refresh());
        progress = new(Host.Ui, () => Vm.RefreshProgress());
        Header.LayoutUpdated += (_, _) => FitHeader();
    }

    /// <summary>The buttons go under the title when beside it they'd leave the title less than 8 em, as with a large Text size.</summary>
    void FitHeader()
    {
        var shown = HeaderButtons.Children.Where(c => c.IsVisible).ToList();
        var buttons = shown.Sum(c => c.DesiredSize.Width) + HeaderButtons.ItemSpacing * Math.Max(0, shown.Count - 1);
        bool below = Header.Bounds.Width - 56 - 2 * Header.ColumnSpacing - buttons < TitleText.FontSize * 8;
        if (below == (Grid.GetRow(HeaderButtons) == 1)) return;
        Grid.SetRow(HeaderButtons, below ? 1 : 0);
        Grid.SetColumn(HeaderButtons, below ? 1 : 2);
        HeaderButtons.Margin = below ? new(0, 12, 0, 0) : default;
    }

    void OnChanged(GameState s) { if (s.Game.Id == Vm?.Row.Id) refresh.Request(); }   // a scan raises one per game
    // a warm raises a tick every progress report: only this game's move the page, and only its progress
    void OnQueue(QueueItem q)
    {
        if (!Format.ProgressTick(q)) refresh.Request();
        else if (q.GameId == Vm?.Row.Id) progress.Request();
    }

    // Refresh too: a change raised while the page wasn't loaded (e.g. the game exited) would show only at the next one
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Host.Core.GameChanged += OnChanged; Host.Core.QueueChanged += OnQueue; Host.Icons.Changed += refresh.Request;
        if (Host.Core is ScsKiller k) k.ScanProgressed += progress.Request;   // a D3DSCache fold's share (FoldLine)
        if (Host.Account is { } account) account.Changed += refresh.Request;
        Vm.Refresh();
        Check();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Host.Core.GameChanged -= OnChanged; Host.Core.QueueChanged -= OnQueue; Host.Icons.Changed -= refresh.Request;
        if (Host.Core is ScsKiller k) k.ScanProgressed -= progress.Request;
        if (Host.Account is { } account) account.Changed -= refresh.Request;
    }

    public void NavigatedTo(object? arg)
    {
        Vm = new DetailVm((string)arg!);
        Vm.ReadCaches();
        CrumbName.Text = Vm.Name;
        Vm.PropertyChanged += (_, _) => Shown();
        DataContext = Vm;
        Shown();
    }

    // what bindings can't set: the frame graph and the session bar's segments
    void Shown()
    {
        Graph.Show(Vm.F is { } f ? FrameData.Of(f) with { Neutral = Vm.FramesOnly } : null);
        var columns = SessionBar.ColumnDefinitions;
        columns[0].Width = new(Vm.LibraryWidth, GridUnitType.Star);
        columns[1].Width = new(Vm.HitsWidth, GridUnitType.Star);
        columns[2].Width = new(Vm.CompilesWidth, GridUnitType.Star);
        columns[2].MinWidth = Vm.CompilesMinWidth;
    }

    // a game the scan hasn't read yet is read now, its state comes back as GameChanged
    void Check()
    {
        if (Host.Core is not ScsKiller k) return;
        var (id, name) = (Vm.Row.Id, Vm.Name);
        _ = Task.Run(() =>
        {
            try { k.CheckGame(id); }
            catch (Exception e) { k.Log?.Report($"checking {name} failed: {e.Message}"); }
        });
    }

    static TextBlock Text(string t) => new() { Text = t, TextWrapping = TextWrapping.Wrap };

    static TextBlock Warn(TextBlock t)
    {
        t.Bind(TextBlock.ForegroundProperty, t.GetResourceObservable("WarnBrush"));
        return t;
    }

    public ContentDialog PartlyWhyDialog() => new() { Title = $"为什么 {Vm.Name} 只编译了一部分？", CloseButtonText = "关闭", Content = Vm.PartlyCompiledWhy };

    async void OnPartlyWhy(object? _, RoutedEventArgs __) => await App.ShowAsync(PartlyWhyDialog());

    async void OnSetAesKey(object? _, RoutedEventArgs __)
    {
        var vm = Vm;
        if (await AesKeyDialog.ShowAsync(vm.Row.Id, $"设置 {vm.Name} 的 AES 密钥")) vm.RefreshGame();
    }

    async void OnChangeAesKey(object? _, RoutedEventArgs __)
    {
        var vm = Vm;
        if (await AesKeyDialog.ShowAsync(vm.Row.Id, $"更改 {vm.Name} 的 AES 密钥")) vm.RefreshGame();
    }

    async void OnRemoveAesKey(object? _, RoutedEventArgs __)
    {
        var vm = Vm;
        var dialog = new ContentDialog
        {
            Title = $"移除 {vm.Name} 的 AES 密钥？", PrimaryButtonText = "移除", CloseButtonText = "取消",
            Content = new TextBlock { Text = DetailVm.RemoveAesKeyNote, TextWrapping = TextWrapping.Wrap },
        };
        if (await App.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        Host.Core.RemoveEncryptionKey(vm.Row.Id);
        vm.RefreshGame();
    }

    async void OnSetGameKey(object? _, RoutedEventArgs __)
    {
        var vm = Vm;
        if (await AesKeyDialog.ShowAsync(vm.Row.Id, $"设置 {vm.Name} 的游戏密钥", aes: false)) vm.RefreshGame();
    }

    void OnRefreshGame(object? _, RoutedEventArgs __) => Vm.RefreshGame();
    async void OnKeepShaders(object? _, RoutedEventArgs __) => await Vm.KeepShadersAsync();
    async void OnGrantFlatpak(object? _, RoutedEventArgs __)
    {
        if (Vm.GrantLauncher is not { } launcher) return;
        if (await App.ConfirmAsync($"允许 {launcher} 读取 SCSKiller 的记录器文件夹？", ScsKiller.FlatpakGrantConfirm(launcher, Vm.GrantWrites), ScsKiller.FlatpakGrantButton(launcher)))
            await Vm.GrantAsync();
    }
    void OnPlay(object? _, RoutedEventArgs __) => Vm.Play();

    static readonly (CompatChoice Choice, string Text)[] ReportChoices =
    [
        (CompatChoice.Gone, "着色器编译卡顿消失了。"),
        (CompatChoice.Better, "着色器编译卡顿有所改善，但仍有一些卡顿。"),
        (CompatChoice.NoHelp, "游戏编译完成，但没有改善卡顿。"),
        (CompatChoice.Worse, "游戏比编译前运行得更差了。"),
        (CompatChoice.NotCompiled, "游戏无法编译。"),
        (CompatChoice.NoStutterBefore, "游戏在编译前也没有着色器编译卡顿。"),
    ];

    /// <summary>Rate this compile's popup for this PC's earlier report of the build (<paramref name="sent"/>), or its thanks
    /// (<paramref name="thanks"/>); built apart so --screenshots renders each state without a click. Only its Send rating
    /// button sends. What a report carries shows until one was sent from this install (<see cref="Settings.CompatReportSent"/>).</summary>
    public ContentDialog ReportDialog(CompatSent? sent, bool thanks = false)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        var dialog = App.Confirm($"为 {vm.Name} 评价这次编译", null, "发送评价");
        if (thanks) return Thanked(dialog);
        var group = $"rating-{Guid.NewGuid():N}";
        // WinUI's RadioButtons: 32 px rows 8 apart, 4 px below its empty header (measured)
        var choices = new StackPanel { Spacing = 8, Margin = new(0, 4, 0, 0) };
        foreach (var (_, t) in ReportChoices) choices.Children.Add(new RadioButton { Content = Text(t), GroupName = group });
        int Picked() => choices.Children.Cast<RadioButton>().ToList().FindIndex(r => r.IsChecked == true);
        static CheckBox Box(string t) => new() { Content = Text(t) };
        var (frameGen, upscaler, mods) = (Box("开启了帧生成"), Box("开启了超分辨率（DLSS、FSR 或 XeSS）"),
            Box("安装了模组（例如 ReShade、OptiScaler 或 RenoDX）"));
        var problem = Warn(new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false });
        var panel = new StackPanel { Spacing = 12, Children = { Text($"告诉其他玩家 {vm.Name} 在 SCSKiller 编译后的运行情况。评价是匿名的。") } };
        if (sent != null) panel.Children.Add(new TextBlock { Text = "你已评价过这个版本的游戏。再次评价将覆盖之前的评价。", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
        panel.Children.Add(choices);
        panel.Children.Add(new StackPanel { Children = { frameGen, upscaler, mods } });
        if (!Host.Core.Settings.CompatReportSent)
            panel.Children.Add(new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = CompatReport.WhatIsSent, TextWrapping = TextWrapping.Wrap, Classes = { "Secondary" } },
                    new HyperlinkButton { Content = "共享的内容", NavigateUri = new Uri(PrivacyReports), Padding = new(0) },
                },
            });
        panel.Children.Add(problem);
        dialog.Content = panel;
        dialog.IsPrimaryButtonEnabled = false;   // until a choice is picked
        foreach (var choice in choices.Children.Cast<RadioButton>()) choice.IsCheckedChanged += (_, _) => dialog.IsPrimaryButtonEnabled = Picked() >= 0;
        dialog.PrimaryButtonClick = async () =>
        {
            dialog.IsPrimaryButtonEnabled = false;
            string? why;
            // read here: the send runs off the UI thread, where the controls can't be read
            var (choice, fg, up, md) = (ReportChoices[Picked()].Choice, frameGen.IsChecked == true, upscaler.IsChecked == true, mods.IsChecked == true);
            try { why = await Task.Run(() => Host.Core.SendReportAsync(id, choice, fg, up, md)); }
            catch (Exception ex) { why = Describe(ex); }
            if (why == null)
            {
                Thanked(dialog);
                try { Host.Core.Settings = Host.Core.Settings with { CompatReportSent = true }; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // unsaved: the disclosure shows once more
            }
            else
            {
                (problem.Text, problem.IsVisible) = (why, true);
                dialog.IsPrimaryButtonEnabled = true;
            }
            return false;   // the popup stays for the answer
        };
        return dialog;

        static ContentDialog Thanked(ContentDialog d)
        {
            (d.Content, d.PrimaryButtonText, d.CloseButtonText) = (Text("感谢，你的评价已发送。"), "", "关闭");
            return d;
        }
    }

    /// <summary>The privacy page's section on compatibility reports.</summary>
    public const string PrivacyReports = "https://scskiller.com/privacy#compatibility-reports";

    /// <summary>The privacy page's section on problem reports.</summary>
    public const string PrivacyProblems = "https://scskiller.com/privacy#problem-reports";

    /// <summary>"Report a problem": the user's words, then the whole report as it will be sent. Nothing is sent before Send.
    /// <paramref name="sentId"/>: the thanks with that report id (screenshots); <paramref name="message"/> fills the box.</summary>
    public ContentDialog ProblemDialog(string? sentId = null, string? message = null)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        var store = Fmt.StoreName(vm.Row.State.Game);
        var page = new (string, string)[]
        {
            ("状态", $"{vm.StatusTitle}. {vm.StatusReason}"),
            ("覆盖", vm.CoverageHeadline),
            (vm.TimeLabel, vm.HasTimeValue ? vm.TimeValue : ""),
            (vm.DiskLabel, vm.HasDiskValue ? vm.DiskValue + (vm.HasDiskInUse ? ", " + vm.DiskInUse : "") : ""),
            (vm.StuttersLabel, vm.HasStutterTile ? vm.StuttersValue : ""),
            (vm.CompileTitle, vm.HasCompileCard ? vm.CompileText : ""),
            ("最近场次", vm.FramesVerdict),
        }.Concat(vm.Details.Select(d => (d.Label, d.Value))).ToList();
        var dialog = App.Confirm($"报告 {vm.Name} 的问题", null, "发送");
        if (sentId != null) return Thanked(dialog, sentId);
        var words = new TextBox { Text = message ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = ProblemReport.MaxMessageChars };
        var withLog = new CheckBox { Content = Text("包含此应用关于该游戏的日志行"), IsChecked = true };
        var report = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 240,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, DejaVu Sans Mono, monospace"), [!FontSizeProperty] = new DynamicResourceExtension("TextSize.12"),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(report, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        var problemText = Warn(new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false });
        var built = "";   // what is sent
        void Build()
        {
            try { report.Text = built = Host.Core.ProblemReport(id, store, page, withLog.IsChecked == true); }
            catch (Exception ex) { report.Text = built = ""; (problemText.Text, problemText.IsVisible) = (Describe(ex), true); }
            Enable();
        }
        void Enable() => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(words.Text) && built.Length > 0;
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "发生了什么？" }, words } },
                new StackPanel { Spacing = 8, Children = { Text("随你的消息一起发送的内容"), report } }, withLog,
                new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = "发送到 SCSKiller 服务器，保留 90 天，仅用于修复该问题。", TextWrapping = TextWrapping.Wrap, Classes = { "Secondary" } },
                        new HyperlinkButton { Content = "发送的内容", NavigateUri = new Uri(PrivacyProblems), Padding = new(0) },
                    },
                },
                problemText,
            },
        };
        Build();
        withLog.Click += (_, _) => Build();
        words.TextChanged += (_, _) => Enable();
        dialog.PrimaryButtonClick = async () =>
        {
            dialog.IsPrimaryButtonEnabled = false;
            var (text, body) = ((words.Text ?? "").Trim().ReplaceLineEndings("\n"), built);
            string? sent, why;
            try { (sent, why) = await Task.Run(() => Host.Core.SendProblemAsync(id, text, body)); }
            catch (Exception ex) { (sent, why) = (null, Describe(ex)); }
            if (sent != null) Thanked(dialog, sent);
            else
            {
                (problemText.Text, problemText.IsVisible) = (why ?? "未发送。请稍后重试。", true);
                dialog.IsPrimaryButtonEnabled = true;
            }
            return false;   // the popup stays for the answer
        };
        return dialog;

        static ContentDialog Thanked(ContentDialog d, string reportId)
        {
            (d.PrimaryButtonText, d.CloseButtonText) = ("", "关闭");
            d.Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "感谢，你的报告已发送。", TextWrapping = TextWrapping.Wrap },
                    new SelectableTextBlock { Text = $"报告 ID {reportId}", FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = "在 Discord 或 GitHub 上提到它，以便我们找到你的报告。它也显示在关于页面上。", TextWrapping = TextWrapping.Wrap, Classes = { "Secondary" } },
                },
            };
            return d;
        }
    }

    async void OnReportProblem(object? _, RoutedEventArgs __) => await App.ShowAsync(ProblemDialog());

    async void OnSendReport(object? _, RoutedEventArgs __)
    {
        var id = Vm.Row.Id;
        CompatSent? sent = null;
        try { sent = await Task.Run(() => Host.Core.ReportSent(id)); }
        catch (Exception) { }   // not known: the popup says nothing about an earlier report
        await App.ShowAsync(ReportDialog(sent));
    }

    void OnDbTeaser(object? _, Run __)
    {
        if (Host.Account?.SignedIn == true) App.Main.NavigateToPatreon();
        else App.SignInWithPatreon();
    }

    void OnStutterLink(object? _, Run __)
    {
        if (Vm.StutterUri is { } uri) _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(uri);
    }

    async void OnOpenExeFolder(object? _, RoutedEventArgs __)
    {
        try { if (ShowExe.Open(Vm.Row.State.Game.ExePath)) return; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { }
        await App.ShowAsync(new ContentDialog { Title = "无法打开文件夹", Content = "游戏的文件夹不存在。", CloseButtonText = "确定" });
    }

    async void OnGameFolder(object? _, RoutedEventArgs __)
    {
        var game = Vm.Row.State.Game;
        if (await GameFolderDialog.ShowAsync(game, $"{game.Name} 的文件夹", "保存") is not { } folder) return;
        ManualAdd added;
        try { added = await Task.Run(() => Host.Core.AddManualGame(game.ExePath, folder)); }   // checks the folder again, then the game
        catch (Exception ex)
        {
            await App.ShowAsync(new ContentDialog { Title = "无法设置游戏文件夹", Content = ex.Message, CloseButtonText = "确定" });
            return;
        }
        if (added.Game.Id != game.Id) App.Main.Navigate(typeof(LibraryPage));   // a moved exe is another game id
        else Vm.Refresh();
    }

    async void OnGameExe(object? _, RoutedEventArgs __)
    {
        string? path;
        try
        {
            var picked = await App.Main.StorageProvider.OpenFilePickerAsync(new() { Title = "使用此 exe", FileTypeFilter = [new(".exe") { Patterns = ["*.exe"] }] });
            path = picked.FirstOrDefault()?.TryGetLocalPath();
        }
        catch (Exception ex)
        {
            await App.ShowAsync(new ContentDialog { Title = "无法打开文件选择器", Content = ex.Message, CloseButtonText = "确定" });
            return;
        }
        if (path != null) await SetExe(path);
    }

    async void OnAutoExe(object? _, RoutedEventArgs __) => await SetExe(null);

    async Task SetExe(string? path)
    {
        var id = Vm.Row.Id;
        try { await Task.Run(() => Host.Core.SetGameExe(id, path)); }   // reads the game again
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await App.ShowAsync(new ContentDialog { Title = "无法更改游戏的 exe", Content = ex.Message, CloseButtonText = "确定" });
            return;
        }
        Vm.Refresh();
    }

    // Only the name shown: the game's id, which everything SCSKiller keeps of it is stored under, stays.
    async void OnRename(object? _, RoutedEventArgs __)
    {
        var box = new TextBox { Text = Vm.Name, MaxLength = 200 };
        var dialog = new ContentDialog
        {
            Title = $"重命名 {Vm.Name}", PrimaryButtonText = "重命名", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { new TextBlock { Text = "名称" }, box, new TextBlock { Text = "只有 SCSKiller 显示的名称会改变。", Classes = { "Secondary" }, TextWrapping = TextWrapping.Wrap } },
            },
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = (box.Text ?? "").Trim().Length > 0;
        box.AttachedToVisualTree += (_, _) => box.SelectAll();
        if (await App.ShowAsync(dialog) != ContentDialogResult.Primary) return;
        try { Fmt.Rename(Vm.Row.Id, box.Text ?? ""); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await App.ShowAsync(new ContentDialog { Title = "无法重命名游戏", Content = ex.Message, CloseButtonText = "确定" });
            return;
        }
        Vm.Refresh();
        CrumbName.Text = Vm.Name;
    }

    async void OnRemoveGame(object? _, RoutedEventArgs __)
    {
        var (id, manual) = (Vm.Row.Id, Vm.IsManual);
        if (!await App.ConfirmAsync($"从游戏库中移除 {Vm.Name}？",
                manual ? "SCSKiller 会忘记该游戏，并把它的记录器从游戏文件夹中移除。游戏本身的文件会保留，你随时可以重新添加。" : Vm.HideNote,
                "移除")) return;
        // refused while a compile of it runs, and a store game while it runs or its recorder stays in
        try { await Task.Run(() => { if (manual) Host.Core.RemoveManualGame(id); else Host.Core.HideGame(id); }); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await App.ShowAsync(new ContentDialog { Title = "无法移除游戏", Content = ex.Message, CloseButtonText = "确定" });
            return;
        }
        App.Main.Navigate(typeof(LibraryPage));
    }

    /// <summary>Screenshots: the bottom of the page; false when it all fits.</summary>
    public bool ScrollToEnd()
    {
        if (Scroller.Extent.Height <= Scroller.Viewport.Height) return false;
        Scroller.ScrollToEnd();
        return true;
    }

    /// <summary>Screenshots: the coverage card's Details and the session's slow frames open.</summary>
    public void ShowDetails() => DetailsExpander.IsExpanded = SlowFramesExpander.IsExpanded = EarlierExpander.IsExpanded = true;

    /// <summary>Screenshots: the Details at the top.</summary>
    public void ScrollToDetails()
    {
        if (DetailsExpander.TranslatePoint(default, Scroller) is { } at) Scroller.Offset = new(0, Math.Max(0, Scroller.Offset.Y + at.Y - 12));
    }

    /// <summary>Screenshots: the frame times card at the top; false without one.</summary>
    public bool ScrollToFrames()
    {
        if (!Vm.HasFrames || FramesCard.TranslatePoint(default, Scroller) is not { } at) return false;
        Scroller.Offset = new(0, Math.Max(0, Scroller.Offset.Y + at.Y - 12));
        return true;
    }

    /// <summary>Screenshots: the traversal stutter fix's choice in the middle; false without it.</summary>
    public void ScrollToShared()
    {
        if (SharedSwitch.TranslatePoint(default, Scroller) is { } at) Scroller.Offset = new(0, Math.Max(0, Scroller.Offset.Y + at.Y - 300));
    }

    public bool ScrollToTraversal()
    {
        if (!Vm.ShowTraversal || TraversalBox.TranslatePoint(default, Scroller) is not { } at) return false;
        Scroller.Offset = new(0, Math.Max(0, Scroller.Offset.Y + at.Y - 400));
        return true;
    }

    void OnCrumb(object? _, RoutedEventArgs __) => App.Main.ShowLibrary();

    async void OnCompile(object? _, RoutedEventArgs __)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        if (vm.OffersCareful)
            try { await Task.Run(() => Host.Core.SetCarefulCompile(id, true)); }
            catch (Exception ex) { vm.Error = ex.Message; vm.Refresh(); return; }
        Host.Core.Compile(id);
        vm.Refresh();
    }

    async void OnMaxCompile(object? _, RoutedEventArgs __)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        if (!Host.Core.Settings.MaxCompileConfirmed)
        {
            if (!await App.ConfirmAsync($"极限编译 {vm.Name}？", DetailVm.MaxCompileWhy, "极限编译")) return;
            Host.Core.Settings = Host.Core.Settings with { MaxCompileConfirmed = true };
        }
        Host.Core.CompileMax(id);
        vm.Refresh();
    }

    void OnRtOnlyCompile(object? _, RoutedEventArgs __)
    {
        try { Host.Core.CompileRtOnly(Vm.Row.Id); }
        catch (InvalidOperationException ex) { Vm.Error = ex.Message; }
        Vm.Refresh();
    }

    // IsCheckedChanged also fires when the binding sets the switch: only a user's flip differs from the view model.
    void OnCarefulToggled(object? _, RoutedEventArgs __) { if (CarefulSwitch.IsChecked == true != Vm.CarefulOn) SetCareful(CarefulSwitch.IsChecked == true); }

    void OnLeftOutToggled(object? _, RoutedEventArgs __)
    {
        var on = LeftOutSwitch.IsChecked == true;
        if (on != Vm.LeftOutOn) Set((vm, p) => vm.LeftOutPending = p, on, id => Host.Core.SetGameLeftOutOfAddAll(id, on));
    }

    void OnSharedToggled(object? _, RoutedEventArgs __)
    {
        var on = SharedSwitch.IsChecked == true;
        if (on != Vm.SharedOn) Set((vm, p) => vm.SharedPending = p, on, id => Host.Core.SetGameUseShared(id, on));
    }

    void OnSharedFollow(object? _, RoutedEventArgs __) => Set((vm, p) => vm.SharedPending = p, Vm.SharedOn, id => Host.Core.SetGameUseShared(id, null));

    void SetCareful(bool on) => Set((vm, p) => vm.CarefulPending = p, on, id => Host.Core.SetCarefulCompile(id, on));

    async void OnCacheCapChanged(object? _, SelectionChangedEventArgs __)
    {
        var (vm, id, i) = (Vm, Vm.Row.Id, CacheCapBox.SelectedIndex);
        if (i < 0 || i == vm.CacheCapIndex) return;
        try
        {
            await Task.Run(() => Host.Core.SetGameCacheCap(id, i == 0 ? null : ScsKiller.CacheCapsGB[i - 1]));
            vm.Error = null;
        }
        catch (Exception ex) { vm.Error = Describe(ex); }
        vm.Refresh();
    }

    async void OnRayTracingChanged(object? _, SelectionChangedEventArgs __)
    {
        var (vm, id, i) = (Vm, Vm.Row.Id, RayTracingBox.SelectedIndex);
        if (i < 0 || i == vm.RayTracingIndex) return;
        try
        {
            await Task.Run(() => Host.Core.SetGameRayTracing(id, i switch { 1 => true, 2 => false, _ => null }));
            vm.Error = null;
        }
        catch (Exception ex) { vm.Error = Describe(ex); }
        vm.Refresh();
    }

    async void OnTraversalChanged(object? _, SelectionChangedEventArgs __)
    {
        var (vm, id, i) = (Vm, Vm.Row.Id, TraversalBox.SelectedIndex);
        if (i < 0 || i == vm.TraversalIndex) return;
        try
        {
            await Task.Run(() => Host.Core.SetGameTraversal(id, TraversalFix.OverrideAt(i)));
            vm.Error = null;
        }
        catch (Exception ex) { vm.Error = Describe(ex); }
        vm.Refresh();
    }

    async void OnNewShadersChanged(object? _, SelectionChangedEventArgs __)
    {
        var (vm, id, i) = (Vm, Vm.Row.Id, NewShadersBox.SelectedIndex);
        if (i < 0 || i == vm.NewShadersIndex) return;
        try
        {
            await Task.Run(() => Host.Core.SetNewShadersMode(id, i == 0 ? null : (NewShadersMode)(i - 1)));
            vm.Error = null;
        }
        catch (Exception ex) { vm.Error = Describe(ex); }
        vm.Refresh();
    }

    // A binding's own update selects ApiIndex: only a different pick goes to the core.
    void OnApiPicked(object? _, SelectionChangedEventArgs __)
    {
        var i = ApiBox.SelectedIndex;
        if (i < 0 || i == Vm.ApiIndex || !Vm.CanPickApi) return;
        var api = Vm.ApiAt(i);
        Set((vm, p) => vm.ApiPending = p, true, id => Host.Core.SetApiOverride(id, api));
    }

    void OnGameMaximum(object? _, RoutedEventArgs __)
    {
        var on = Vm.MaximumSuggested;
        Set((vm, p) => vm.MaximumPending = p, on, id => Host.Core.SetGameMaximum(id, on));
    }

    /// <summary>Never blank: an exception without a message shows its type, and its inner one's.</summary>
    static string Describe(Exception e) => !string.IsNullOrWhiteSpace(e.Message) ? e.Message
        : e.GetType().Name + (e.InnerException is { } inner ? ": " + Describe(inner) : $" (0x{e.HResult:X8})");

    // The core's switches do file I/O: off the UI thread, the switch disabled meanwhile (pending = the state asked for).
    async void Set(Action<DetailVm, bool?> setPending, bool pending, Action<string> change)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        setPending(vm, pending);
        vm.Refresh();
        try
        {
            await Task.Run(() => change(id));
            vm.Error = null;
        }
        catch (Exception ex) { vm.Error = Describe(ex); }
        setPending(vm, null);
        vm.Refresh();   // the switch shows the real state, even after a failure
    }

    async void OnClearCache(object? _, RoutedEventArgs __)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        var mesa = MesaLinux.Is(Host.Core.Vendor.Caps);
        var note = "在重新预热之前，游戏会在运行时再次编译着色器。"
            + (mesa ? "该游戏的 Steam 着色器预缓存文件也会一并删除，Steam 会重新处理它们。" : "")
            + (vm.Row.State.AntiCheat != AntiCheat.None ? "反作弊游戏：仅清除驱动缓存。" : "");   // VAC: as any game (ScsKiller.GameCaches)
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "正在测量缓存文件…\n\n" + note };
        var ownText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var alsoOwn = new CheckBox { Content = ownText, IsVisible = false };
        _ = Measure();
        if (await App.ShowAsync(App.Confirm($"清除 {vm.Name} 的着色器缓存？", new StackPanel { Spacing = 12, Children = { text, alsoOwn } }, "清除缓存"))
            != ContentDialogResult.Primary) return;
        var withOwn = alsoOwn.IsChecked == true;
        try { vm.Error = await Task.Run(() => Host.Core.ClearGameCache(id, withOwn)) ? null : "未清除任何内容：未找到该游戏的着色器缓存文件。"; }
        catch (Exception ex) { vm.Error = ex.Message; }
        vm.ReadCaches();
        vm.Refresh();

        // the sizes fill in while the dialog is open: reading the Windows cache folders can take seconds; Clear reads them again
        async Task Measure()
        {
            try
            {
                var all = await Task.Run(() => Host.Core.GameCaches(id, gamePrecache: true));
                var shared = await Task.Run(() => Host.Core.SharedCacheNote(id));
                static bool Own(CachePart p) => p.Name is ScsKiller.PipelinePart or ScsKiller.PrecachePart;
                var (parts, own) = (all.Where(p => !Own(p)).ToList(), all.Where(Own).ToList());
                text.Text = (parts.Count > 0 ? string.Join(" · ", parts.Select(p => $"{p.Name} {Format.Bytes(p.Bytes)}")) + ".\n\n" : "磁盘上目前没有缓存文件。\n\n")
                    + (shared != null ? shared + "\n\n" : "") + note;
                ownText.Text = $"同时删除该游戏自己的着色器缓存（{Format.Bytes(own.Sum(p => p.Bytes))}）。它会在下次启动时重建，届时启动会花更长时间。";
                alsoOwn.IsVisible = own.Count > 0;
            }
            catch (Exception ex) { text.Text = Describe(ex) + "\n\n" + note; }
        }
    }

    async void OnBeforeAfter(object? _, RoutedEventArgs __)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        var ba = vm.Row.State.BeforeAfter;
        string? error = ba?.Stage switch
        {
            null => await BeforeAfterDialogs.StartAsync(id),
            _ when BeforeAfterUi.Voided(ba) => await BeforeAfterDialogs.StartAsync(id, over: true),
            BeforeAfterStage.BeforeArmed when ba!.ClearPending => await BeforeAfterUi.ContinueAsync(id),
            BeforeAfterStage.BeforeArmed or BeforeAfterStage.AfterArmed => await BeforeAfterUi.LaunchAsync(id),
            BeforeAfterStage.AfterReady => await BeforeAfterUi.ContinueAsync(id),
            BeforeAfterStage.Done => await ReportWindow.OpenAsync(id) is null ? "报告已不存在：请重新开始。" : null,
            _ => null,
        };
        Shown(vm, error);
    }

    async void OnBaRecordAgain(object? _, RoutedEventArgs __) => Shown(Vm, await BeforeAfterDialogs.RecordAfterAgainAsync(Vm.Row.Id));
    async void OnBaStartOver(object? _, RoutedEventArgs __) => Shown(Vm, await BeforeAfterDialogs.StartAsync(Vm.Row.Id, over: true));
    async void OnBaCancel(object? _, RoutedEventArgs __) => Shown(Vm, await BeforeAfterDialogs.CancelAsync(Vm.Row.Id));
    async void OnBaHowItWorks(object? _, RoutedEventArgs __) => await BeforeAfterDialogs.IntroAsync(Vm.Name, BeforeAfterDialogs.Intro.HowItWorks);
    // Continue in Compiling clears and compiles again, first in the queue
    async void OnBaCompileAgain(object? _, RoutedEventArgs __) => Shown(Vm, await BeforeAfterUi.ContinueAsync(Vm.Row.Id));

    static void Shown(DetailVm vm, string? error)
    {
        vm.Error = error;
        vm.Refresh();
    }

    async void OnClearRecording(object? _, RoutedEventArgs __)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        if (!await App.ConfirmAsync($"清除 {vm.Name} 的记录？",
                "已记录的管线将被移除。只有当游戏在记录期间再次创建它们时才会重新编译，"
                + "编译方案也会回到游戏文件所决定的状态。\n\n记录器本身会保留，游戏文件不会被改动。",
                "清除记录")) return;
        vm.ClearingRecording = true;
        vm.Refresh();
        try { vm.Error = await Task.Run(() => Host.Core.ClearRecording(id)) ? null : "未清除任何内容：未找到该游戏的记录。"; }
        catch (Exception ex) { vm.Error = ex.Message; }
        vm.ClearingRecording = false;
        vm.Refresh();
    }

    void OnRecordToggled(object? _, RoutedEventArgs __)
    {
        var on = RecordSwitch.IsChecked == true;   // read here: the change runs off the UI thread, where the switch can't be read
        if (on == Vm.RecordOn) return;
        // an eligible anti-cheat game: the switch allows sessions (offline, or a confirmed session in a game with anti-cheat)
        if (Vm.ShowOffline) Set((vm, p) => vm.OfflinePending = p, on, id => Host.Core.SetOfflineRecording(id, on));
        else SetRecord(on);
    }

    // On only after the standard anti-cheat wording is confirmed, once: the core then records every launch as a session
    async void OnAlwaysToggled(object? _, RoutedEventArgs __)
    {
        var on = AlwaysSwitch.IsChecked == true;
        if (on == Vm.AlwaysOn) return;
        if (!on)
        {
            Set((vm, p) => vm.AlwaysPending = p, false, id => Host.Core.SetAlwaysRecord(id, false, confirmed: false, AntiCheat.None));
            return;
        }
        var (vm, id) = (Vm, Vm.Row.Id);
        vm.AlwaysPending = true;   // the switch stays on, and off for a second toggle, until the dialog is answered
        vm.Refresh();
        AntiCheat shown;
        bool open;
        try { (shown, open) = await Host.Core.CheckAntiCheatAsync(id); }
        catch (Exception ex)
        {
            (vm.Error, vm.AlwaysPending) = (Describe(ex), null);
            vm.Refresh();
            return;
        }
        var fresh = vm.Row.State with { AntiCheat = shown, AntiCheatUnfinished = open };   // VAC stays the state's: the check only names a client anti-cheat
        var ok = await App.ConfirmAsync($"始终自担风险地记录 {vm.Name}？",
            Text(DetailVm.RiskText(fresh) + (ScsKiller.VulkanPickWarning(fresh) is { } pick ? "\n\n" + pick : "")), "始终记录");
        vm.AlwaysPending = null;
        if (!ok)
        {
            vm.Refresh();   // the switch shows off again
            return;
        }
        Set((v, p) => v.AlwaysPending = p, true, i => Host.Core.SetAlwaysRecord(i, true, confirmed: true, shown));
    }

    void OnRecordTip(object? _, RoutedEventArgs __) => SetRecord(true);

    void OnUseDefault(object? _, RoutedEventArgs __) => SetRecord(null);

    void SetRecord(bool? on) => Set((vm, p) => vm.RecordPending = p, on ?? Host.Core.Settings.RecordAllGames,
        id => Host.Core.SetRecorderOverride(id, on switch { true => RecorderOverride.On, false => RecorderOverride.Off, null => RecorderOverride.Default }));

    void OnAlongsideToggled(object? _, RoutedEventArgs __) { if (AlongsideSwitch.IsChecked == true != Vm.AlongsideOn) SetAlongside(AlongsideSwitch.IsChecked == true); }

    void SetAlongside(bool on) => Set((vm, p) => vm.AlongsidePending = p, on, id => Host.Core.SetRecordAlongsideMod(id, on));

    // Every launch is confirmed here: the core starts a session only with confirmed: true
    async void OnRecordOffline(object? _, RoutedEventArgs __)
    {
        var (vm, id) = (Vm, Vm.Row.Id);
        if (vm.Risk) { await RecordRisk(vm, id); return; }
        if (!await App.ConfirmAsync($"离线启动 {vm.Name} 并跳过 EasyAntiCheat？", Text(DetailVm.OfflineRisk + "该场次无法进行在线游戏。"),
                "记录离线场次")) return;
        await StartSession(vm, () => Host.Core.StartOfflineSession(id, confirmed: true));
    }

    // A fresh check first, so the confirmation names the anti-cheat that is there now; the core refuses if it finds another.
    async Task RecordRisk(DetailVm vm, string id)
    {
        vm.OfflineStarting = true;   // before the check: the button stays off until the dialog is answered
        vm.Refresh();
        AntiCheat shown;
        bool open;
        try { (shown, open) = await Host.Core.CheckAntiCheatAsync(id); }
        catch (Exception ex)
        {
            (vm.Error, vm.OfflineStarting) = (Describe(ex), false);
            vm.Refresh();
            return;
        }
        var fresh = vm.Row.State with { AntiCheat = shown, AntiCheatUnfinished = open };   // VAC stays the state's: the check only names a client anti-cheat
        var ok = await App.ShowAsync(RiskDialog(vm, fresh)) == ContentDialogResult.Primary;
        if (!ok)
        {
            vm.OfflineStarting = false;
            vm.Refresh();
            return;
        }
        await StartSession(vm, () => Host.Core.StartRiskSession(id, confirmed: true, shown));
    }

    public ContentDialog RiskDialog(DetailVm vm, GameState fresh) => App.Confirm($"自担风险地记录 {vm.Name}？",
        Text(DetailVm.RiskText(fresh) + "SCSKiller 会在游戏退出时移除其文件；如果发生崩溃，则在你下次登录时移除。"
             + (ScsKiller.VulkanPickWarning(fresh) is { } pick ? "\n\n" + pick : "")), "记录场次");

    // The start runs off the UI thread; the task it returns ends when the game exited and its files are out.
    static async Task StartSession(DetailVm vm, Func<Task> start)
    {
        vm.OfflineStarting = true;
        vm.Refresh();
        Task session;
        try
        {
            session = await Task.Factory.StartNew(start, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
            vm.Error = null;
        }
        catch (Exception ex) { (vm.Error, session) = (Describe(ex), Task.CompletedTask); }
        vm.OfflineStarting = false;
        vm.Refresh();
        try { await session; }
        catch (Exception ex) { vm.Error = Describe(ex); }
        vm.Refresh();
    }
}
