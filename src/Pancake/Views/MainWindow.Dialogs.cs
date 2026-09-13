using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Pancake.Views;

/// <summary>
/// 轻量对话框：提示、确认与多选项共用同一块浮层，支持自定义内容（例如复选项）。
/// 自动排列失败、放弃修改、新建项目、应用项目外观等场景都走这里。
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>对话框按钮的返回值。</summary>
    private enum DialogResult
    {
        None,
        Primary,
        Secondary,
        Close
    }

    private TaskCompletionSource<DialogResult>? _dialogCompletion;

    /// <summary>显示一条提示，等待用户关闭。</summary>
    private Task ShowMessageAsync(string title, string message, string closeText) =>
        ShowDialogAsync(title, message, null, null, null, closeText, closeText);

    /// <summary>显示一条确认，返回用户是否选择了主要按钮。</summary>
    private async Task<bool> ShowConfirmAsync(string title, string message, string primaryText, string secondaryText) =>
        await ShowDialogAsync(title, message, null, primaryText, secondaryText, "取消", primaryText) == DialogResult.Primary;

    /// <summary>三选项对话框；关闭按钮返回 <see cref="DialogResult.Close"/>。</summary>
    private Task<DialogResult> ShowChoiceAsync(
        string title,
        string message,
        string primaryText,
        string? secondaryText,
        string closeText,
        Control? content = null,
        string? initialFocus = null) =>
        ShowDialogAsync(title, message, content, primaryText, secondaryText, closeText, initialFocus);

    /// <summary>
    /// 单行文本输入对话框：取消或空内容返回 null，调用方据此放弃本次修改。
    /// </summary>
    private async Task<string?> ShowInputAsync(
        string title,
        string header,
        string initialText,
        string placeholder,
        int maxLength,
        string primaryText)
    {
        TextBox input = new()
        {
            Text = initialText,
            Watermark = placeholder,
            MaxLength = maxLength,
            Width = 320,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
        };
        StackPanel content = new() { Spacing = 8 };
        content.Children.Add(new TextBlock { Text = header, FontSize = 14 });
        content.Children.Add(input);
        DialogResult result = await ShowDialogAsync(title, string.Empty, content, primaryText, null, "取消", null);
        if (result != DialogResult.Primary) return null;
        string value = (input.Text ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private async Task<DialogResult> ShowDialogAsync(
        string title,
        string message,
        Control? content,
        string? primaryText,
        string? secondaryText,
        string closeText,
        string? initialFocus)
    {
        this.FindControl<TextBlock>("DialogTitle")!.Text = title;
        TextBlock messageBlock = this.FindControl<TextBlock>("DialogMessage")!;
        messageBlock.Text = message;
        messageBlock.IsVisible = !string.IsNullOrEmpty(message);
        ContentControl host = this.FindControl<ContentControl>("DialogContentHost")!;
        host.Content = content;
        host.IsVisible = content is not null;

        Button primary = this.FindControl<Button>("DialogPrimaryButton")!;
        Button secondary = this.FindControl<Button>("DialogSecondaryButton")!;
        Button close = this.FindControl<Button>("DialogCloseButton")!;
        primary.Content = primaryText ?? closeText;
        primary.IsVisible = primaryText is not null;
        secondary.Content = secondaryText ?? string.Empty;
        secondary.IsVisible = secondaryText is not null;
        close.Content = closeText;
        _ = initialFocus;

        _dialogCompletion = new TaskCompletionSource<DialogResult>();
        DialogOverlay.IsVisible = true;
        try
        {
            return await _dialogCompletion.Task;
        }
        finally
        {
            DialogOverlay.IsVisible = false;
            host.Content = null;
            _dialogCompletion = null;
        }
    }

    private void DialogPrimary_Click(object? sender, RoutedEventArgs e) => _dialogCompletion?.TrySetResult(DialogResult.Primary);

    private void DialogSecondary_Click(object? sender, RoutedEventArgs e) => _dialogCompletion?.TrySetResult(DialogResult.Secondary);

    private void DialogClose_Click(object? sender, RoutedEventArgs e) => _dialogCompletion?.TrySetResult(DialogResult.Close);
}
