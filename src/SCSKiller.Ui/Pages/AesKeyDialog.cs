using Avalonia.Controls;
using Avalonia.Media;
using SCSKiller.Core.App;
using SCSKiller.Ui.Controls;
using SCSKiller.Ui.Models;

namespace SCSKiller.Ui.Pages;

/// <summary>The user's own AES keys for an encrypted Unreal game, or a BlackSpace game's key: checked against the game's
/// files and stored on this PC only.</summary>
static class AesKeyDialog
{
    /// <summary>True once a key that opens the game's files is stored; the caller then re-reads the game.
    /// <paramref name="reason"/>: the Library's Why?, shown above the key.</summary>
    public static async Task<bool> ShowAsync(string id, string title, string? reason = null, bool aes = true)
    {
        var (dialog, box) = Dialog(title, reason, aes);
        if (await App.ShowAsync(dialog) != ContentDialogResult.Primary) return false;
        var key = (box.Text ?? "").Trim();
        (bool Stored, IReadOnlyList<string> Refused) result;
        try { result = await Task.Run(() => Host.Core.SetEncryptionKey(id, key)); }   // opens the game's files
        catch (Exception ex) { result = (false, [ex.Message]); }
        if (result.Refused.Count > 0) await App.ShowAsync(Refused(result.Stored, result.Refused));
        return result.Stored;
    }

    public static (ContentDialog Dialog, TextBox Box) Dialog(string title, string? reason, bool aes)
    {
        var box = new TextBox { PlaceholderText = aes ? MultiKeyHint : "0x 开头的 64 位十六进制数字", Classes = { "clearButton" }, AcceptsReturn = aes, MaxHeight = 160 };
        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { TextWrapping = TextWrapping.Wrap, Text = (aes ? AesIntro : GameKeyIntro) +
                    "如果你有密钥，请粘贴到这里：它会与游戏文件核对，并且只保存在这台电脑上。" },
                new StackPanel { Spacing = 8, Children = { new TextBlock { Text = aes ? "AES 密钥" : "游戏密钥" }, box } },
            },
        };
        if (reason != null) panel.Children.Insert(0, new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap });
        return (new ContentDialog { Title = title, Content = panel, CloseButtonText = "关闭", PrimaryButtonText = "解锁", DefaultButton = ContentDialogButton.Close }, box);
    }

    /// <summary>The lines that didn't work, each with why. <paramref name="stored"/>: the others were kept.</summary>
    public static ContentDialog Refused(bool stored, IReadOnlyList<string> refused)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.AddRange(refused.Select(r => new TextBlock { Text = Format.Sentence(r.TrimEnd('.')) + ".", TextWrapping = TextWrapping.Wrap }));
        return new ContentDialog { Title = stored ? "部分密钥无效" : refused.Count > 1 ? "这些密钥无效" : "该密钥无效", Content = panel, CloseButtonText = "确定" };
    }

    const string MultiKeyHint = "0x 开头的 64 位十六进制数字，每行一个密钥";
    const string AesIntro = "SCSKiller 没能在该游戏的 exe 中找到它的密钥（受保护的 exe 会隐藏密钥）。";
    const string GameKeyIntro = "有了该游戏的密钥，SCSKiller 无需记录即可编译它。";
}
