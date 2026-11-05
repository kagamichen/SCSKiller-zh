using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using SCSKiller.Core;
using SCSKiller.Core.App;
using SCSKiller.Ui.Controls;
using SCSKiller.Ui.Models;

namespace SCSKiller.Ui;

/// <summary>The first-run welcome, or a later start's one-time questions and What's new for a new version, once the
/// window first shows.</summary>
static class StartDialogs
{
    public static void WhenShown(Window main)
    {
        var core = Host.Core;
        Func<Task> run;
        if (!core.Settings.WelcomeSeen)
        {
            WhatsNew.AtStart(Host.Store, firstRun: true, AppVersion.Current);
            var fetch = WelcomeContent.FetchAsync();   // at most 3 s, from now rather than from the first show
            run = async () =>
            {
                if (await StartFlow.WelcomeAsync(core, () => AskWelcomeAsync(fetch))) App.SignInWithPatreon();
                await StartFlow.TraversalAsync(core, AskTraversalAsync);
            };
        }
        else run = async () =>
        {
            await StartFlow.QuestionsAsync(core, AskActiveCheckAsync, AskDiagnosticsAsync);
            await StartFlow.TraversalAsync(core, AskTraversalAsync);
            // recorded before it shows: if another dialog is up, About shows it
            if (WhatsNew.AtStart(Host.Store, firstRun: false, AppVersion.Current) is { } news) await TryShowAsync(WhatsNewDialog.Build(news));
        };
        main.Opened += Once;

        async void Once(object? _, EventArgs __)
        {
            main.Opened -= Once;
            await run();
        }
    }

    static async Task<ContentDialogResult?> TryShowAsync(ContentDialog dialog) => App.Main.TryShowDialog(dialog) is { } shown ? await shown : null;

    static async Task<WelcomeAnswer?> AskWelcomeAsync(Task<WelcomeContent?> fetch)
    {
        var (dialog, answer) = Welcome(await fetch ?? WelcomeContent.Default);
        return await TryShowAsync(dialog) is { } result ? answer(result) : null;
    }

    /// <summary>The server's text or the bundled one, as plain text.</summary>
    public static (ContentDialog Dialog, Func<ContentDialogResult, WelcomeAnswer> Answer) Welcome(WelcomeContent c)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var p in c.Paragraphs) panel.Children.Add(new TextBlock { Text = p, TextWrapping = TextWrapping.Wrap });
        if (c.Link != null) panel.Children.Add(new HyperlinkButton { Content = c.Link.Text, NavigateUri = new Uri(c.Link.Url), Padding = new(0) });
        var share = Box(c.Share);
        var diagnostics = Box("共享诊断数据以帮助修复问题");
        var active = Box("发送匿名每日活跃检查", "帮助我们统计活跃安装量，以便合理配置服务器规模。每天一次，仅包含应用版本和 GPU 厂商，不含任何标识符。");
        panel.Children.AddRange([share, diagnostics, active]);
        var dialog = new ContentDialog { Title = c.Title, Content = panel, PrimaryButtonText = c.SignIn, CloseButtonText = c.Dismiss, DefaultButton = ContentDialogButton.Primary };
        return (dialog, result => new(share.IsChecked == true, diagnostics.IsChecked == true, active.IsChecked == true, result == ContentDialogResult.Primary));
    }

    // never pre-ticked: that isn't consent
    static CheckBox Box(string text, string? caption = null)
    {
        var label = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } } };
        if (caption != null) label.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Classes = { "Caption" } });
        var box = new CheckBox { IsChecked = false, Content = label };
        AutomationProperties.SetName(box, text);
        return box;
    }

    static async Task<bool?> AskActiveCheckAsync() => await TryShowAsync(ActiveCheckDialog()) is { } r ? r == ContentDialogResult.Primary : null;

    public static ContentDialog ActiveCheckDialog() => new()
    {
        Title = "匿名每日活跃检查",
        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "SCSKiller 每天可以向我们的服务器报告一次该安装处于活跃状态，仅附带版本和 GPU 厂商，不含任何标识符。"
                        + "这有助于我们统计活跃安装量，以便合理配置服务器规模。该功能默认关闭，需要你手动开启。",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = "可随时在设置中更改。", TextWrapping = TextWrapping.Wrap, Classes = { "Caption" } },
            },
        },
        PrimaryButtonText = "开启", CloseButtonText = "保持关闭", DefaultButton = ContentDialogButton.None,   // no nudge either way
    };

    static async Task<TraversalMode?> AskTraversalAsync() => await TryShowAsync(TraversalDialog(TraversalFix.LowVram(Host.Core.Vendor.Gpu))) is { } r
        ? r == ContentDialogResult.Primary ? TraversalMode.All : TraversalMode.Off : null;

    /// <summary>Primary: all games, Keep it off or Esc: off.</summary>
    public static ContentDialog TraversalDialog(bool? lowVram)
    {
        var panel = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = TraversalFix.AskIntro, TextWrapping = TextWrapping.Wrap }, new TextBlock { Text = TraversalFix.AskBody, TextWrapping = TextWrapping.Wrap } } };
        panel.Children.Add(lowVram == true
            ? new TextBlock { Text = TraversalFix.LowVramWarning, TextWrapping = TextWrapping.Wrap, [!TextBlock.ForegroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("WarnBrush") }
            : new TextBlock { Text = TraversalFix.VramLine, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = TraversalFix.AskFooter, TextWrapping = TextWrapping.Wrap, Classes = { "Caption" } });
        return new()
        {
            Title = TraversalFix.Title, Content = panel, PrimaryButtonText = TraversalFix.AskAll,
            CloseButtonText = TraversalFix.AskNotNow, DefaultButton = lowVram == true ? ContentDialogButton.Close : ContentDialogButton.None,
        };
    }

    static bool askingOldPlans;

    /// <summary>A game with a lot of cache its version and plan don't use (<see cref="ScsKiller.OldPlansNotice"/>), asked once:
    /// Delete and compile clears it as Clear cache does, then compiles, Delete takes only its earlier versions' folders. Keep it or
    /// Esc keeps it.</summary>
    public static async Task AskOldPlansAsync(ScsKiller k)
    {
        if (askingOldPlans) return;
        askingOldPlans = true;
        try
        {
            var (id, notice) = await Task.Run(() => k.Games.Select(s => (s.Game.Id, k.OldPlansNotice(s.Game.Id))).FirstOrDefault(a => a.Item2 != null));
            if (notice is not { } n || await TryShowAsync(OldPlansDialog(n)) is not { } result) return;   // another dialog is open: asked again later
            var clear = result == ContentDialogResult.Primary;
            await Task.Run(() => k.OldPlansAnswered(id, keep: !clear));
            if (!clear) return;
            string? error;
            try { error = n.Compile ? await Task.Run(() => k.ClearGameCache(id)) ? null : "未找到该游戏的着色器缓存文件。" : await Task.Run(() => k.DeleteOrphanedD3DSCache(id)) > 0 ? null : "未能删除早期版本的着色器缓存。"; }
            catch (Exception e) { error = e.Message; }
            if (error != null) await TryShowAsync(new ContentDialog { Title = "无法清除缓存", Content = error, CloseButtonText = "确定" });
            else if (n.Compile) k.Compile(id);
        }
        finally { askingOldPlans = false; }
    }

    public static ContentDialog OldPlansDialog((string Title, string Text, bool Compile) n) => new()
    {
        Title = n.Title, Content = new TextBlock { Text = n.Text, TextWrapping = TextWrapping.Wrap },
        PrimaryButtonText = n.Compile ? ScsKiller.OldPlansClear : ScsKiller.OldVersionsClear, CloseButtonText = ScsKiller.OldPlansKeep, DefaultButton = ContentDialogButton.Close,
    };

    static async Task<bool?> AskDiagnosticsAsync() => await TryShowAsync(new ContentDialog
    {
        Title = "共享诊断数据以帮助修复问题？",
        Content = "当编译失败时，发送其错误代码，每次编译一条。你可以在设置中更改此选项。",
        PrimaryButtonText = "共享诊断数据", CloseButtonText = "暂不", DefaultButton = ContentDialogButton.Close,
    }) is { } r ? r == ContentDialogResult.Primary : null;
}
