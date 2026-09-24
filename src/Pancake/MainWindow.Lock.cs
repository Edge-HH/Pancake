using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pancake.Services;

namespace Pancake;

public sealed partial class MainWindow
{
    private Action? _refreshLockSettings;

    /// <summary>锁定页：总开关、密码与两步验证管理，以及需要验证的操作范围。</summary>
    private StackPanel BuildLockPage()
    {
        StackPanel page = SettingsStack();
        page.Children.Add(Toggle("启用锁定", _settings.Lock.Enabled, value => _settings.Lock.Enabled = value,
            () => { SettingChanged(); _refreshLockSettings?.Invoke(); }));
        page.Children.Add(Note("锁定只在设置过密码或两步验证后生效；都没有配置时不锁定，避免把自己关在门外。密码只保存哈希，不保存明文。"));

        page.Children.Add(Heading("密码"));
        StackPanel passwordArea = new() { Spacing = 8 };
        page.Children.Add(passwordArea);

        page.Children.Add(Heading("两步验证"));
        StackPanel totpArea = new() { Spacing = 8 };
        page.Children.Add(totpArea);

        page.Children.Add(Heading("需要验证的操作"));
        page.Children.Add(Toggle("打开设置前要求验证", _settings.Lock.RequireAuthForSettings, value => _settings.Lock.RequireAuthForSettings = value));
        page.Children.Add(Toggle("编辑看板前要求验证", _settings.Lock.RequireAuthForEditing, value => _settings.Lock.RequireAuthForEditing = value));

        void Refresh()
        {
            passwordArea.Children.Clear();
            if (LockService.HasPassword(_settings.Lock))
            {
                TextBlock status = new() { Text = "已设置密码。", TextWrapping = TextWrapping.Wrap };
                passwordArea.Children.Add(status);
                Button change = new() { Content = "修改密码", HorizontalAlignment = HorizontalAlignment.Left };
                change.Click += async (_, _) => await ChangePasswordAsync();
                passwordArea.Children.Add(change);
                Button remove = new() { Content = "删除密码", HorizontalAlignment = HorizontalAlignment.Left };
                remove.Click += async (_, _) => await RemovePasswordAsync();
                passwordArea.Children.Add(remove);
            }
            else
            {
                passwordArea.Children.Add(new TextBlock { Text = "尚未设置密码。", TextWrapping = TextWrapping.Wrap });
                Button create = new() { Content = "设置密码", HorizontalAlignment = HorizontalAlignment.Left };
                create.Click += async (_, _) => await CreatePasswordAsync();
                passwordArea.Children.Add(create);
            }

            totpArea.Children.Clear();
            if (LockService.HasTotp(_settings.Lock))
            {
                totpArea.Children.Add(new TextBlock { Text = "已绑定验证器应用。", TextWrapping = TextWrapping.Wrap });
                Button rebind = new() { Content = "重新绑定", HorizontalAlignment = HorizontalAlignment.Left };
                rebind.Click += async (_, _) => await BindTotpAsync();
                totpArea.Children.Add(rebind);
                Button remove = new() { Content = "解绑", HorizontalAlignment = HorizontalAlignment.Left };
                remove.Click += async (_, _) => await RemoveTotpAsync();
                totpArea.Children.Add(remove);
            }
            else
            {
                totpArea.Children.Add(new TextBlock { Text = "尚未绑定验证器应用。", TextWrapping = TextWrapping.Wrap });
                Button bind = new() { Content = "绑定验证器", HorizontalAlignment = HorizontalAlignment.Left };
                bind.Click += async (_, _) => await BindTotpAsync();
                totpArea.Children.Add(bind);
            }
        }

        _refreshLockSettings = Refresh;
        Refresh();
        return page;
    }

    /// <summary>设置新密码：两次输入一致且非空才写入哈希。</summary>
    private async Task CreatePasswordAsync()
    {
        if (await AskPasswordAsync("设置密码", requireCurrent: false) is not { } password) return;
        LockService.SetPassword(_settings.Lock, password);
        SettingChanged();
        _refreshLockSettings?.Invoke();
    }

    /// <summary>修改密码需先通过当前密码（或验证器）身份验证。</summary>
    private async Task ChangePasswordAsync()
    {
        if (!await AuthorizeAsync()) return;
        if (await AskPasswordAsync("修改密码", requireCurrent: false) is not { } password) return;
        LockService.SetPassword(_settings.Lock, password);
        SettingChanged();
        _refreshLockSettings?.Invoke();
    }

    private async Task RemovePasswordAsync()
    {
        if (!await AuthorizeAsync()) return;
        // 只剩密码一种验证方式时，删除后锁定会失效，需要明确确认。
        bool locksOut = !LockService.HasTotp(_settings.Lock);
        string message = locksOut
            ? "删除密码后锁定将不再生效（没有密码也没有两步验证）。确定删除吗？"
            : "确定删除密码吗？仍可通过两步验证解锁。";
        if (await ShowConfirmAsync("删除密码", message) != ContentDialogResult.Primary) return;
        LockService.ClearPassword(_settings.Lock);
        SettingChanged();
        _refreshLockSettings?.Invoke();
    }

    /// <summary>绑定验证器：生成新密钥并展示 otpauth 链接与密钥文本，验证一次通过后才保存。</summary>
    private async Task BindTotpAsync()
    {
        if (LockService.HasTotp(_settings.Lock) && !await AuthorizeAsync()) return;
        string secret = LockService.CreateTotpSecret();
        TextBox code = new() { Header = "验证器中显示的 6 位验证码", PlaceholderText = "000000", MaxLength = 8 };
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "在验证器应用（如 Microsoft Authenticator、Google Authenticator）中扫码或手动输入密钥：",
            TextWrapping = TextWrapping.Wrap
        });
        TextBox uri = new() { IsReadOnly = true, Text = LockService.TotpProvisioningUri(secret), TextWrapping = TextWrapping.Wrap, MaxHeight = 80 };
        content.Children.Add(uri);
        TextBox key = new() { IsReadOnly = true, Text = secret };
        content.Children.Add(new TextBlock { Text = "密钥", FontSize = 12 });
        content.Children.Add(key);
        content.Children.Add(code);
        ContentDialog dialog = new()
        {
            Title = "绑定验证器", Content = content, PrimaryButtonText = "绑定", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = RootShell.XamlRoot
        };
        code.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = code.Text.Trim().Length > 0;
        dialog.IsPrimaryButtonEnabled = false;
        if (_dialogOpen) return;
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return;
        if (!LockService.VerifyTotp(secret, code.Text, DateTimeOffset.Now))
        {
            await ShowMessageAsync("绑定失败", "验证码不正确或已过期，请重试。", "知道了");
            return;
        }
        _settings.Lock.TotpSecret = secret;
        SettingChanged();
        _refreshLockSettings?.Invoke();
    }

    private async Task RemoveTotpAsync()
    {
        if (!await AuthorizeAsync()) return;
        bool locksOut = !LockService.HasPassword(_settings.Lock);
        string message = locksOut
            ? "解绑后锁定将不再生效（没有密码也没有两步验证）。确定解绑吗？"
            : "确定解绑两步验证吗？仍可通过密码解锁。";
        if (await ShowConfirmAsync("解绑两步验证", message) != ContentDialogResult.Primary) return;
        _settings.Lock.TotpSecret = "";
        SettingChanged();
        _refreshLockSettings?.Invoke();
    }

    /// <summary>密码输入对话框：新建/修改用两次输入校验，验证用单框。</summary>
    private async Task<string?> AskPasswordAsync(string title, bool requireCurrent)
    {
        PasswordBox current = new() { Header = "当前密码", PlaceholderText = "输入当前密码" };
        PasswordBox first = new() { Header = requireCurrent ? "新密码" : "密码", PlaceholderText = "输入密码" };
        PasswordBox second = new() { Header = "确认新密码", PlaceholderText = "再次输入密码" };
        StackPanel content = new() { Spacing = 10 };
        if (requireCurrent) content.Children.Add(current);
        content.Children.Add(first);
        content.Children.Add(second);
        ContentDialog dialog = new()
        {
            Title = title, Content = content, PrimaryButtonText = "确定", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = RootShell.XamlRoot
        };
        if (_dialogOpen) return null;
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return null;
        if (requireCurrent && !LockService.VerifyPassword(_settings.Lock, current.Password))
        {
            await ShowMessageAsync("验证失败", "当前密码不正确。", "知道了");
            return null;
        }
        if (first.Password.Length == 0)
        {
            await ShowMessageAsync("无法设置", "密码不能为空。", "知道了");
            return null;
        }
        if (!string.Equals(first.Password, second.Password, StringComparison.Ordinal))
        {
            await ShowMessageAsync("无法设置", "两次输入的密码不一致。", "知道了");
            return null;
        }
        return first.Password;
    }

    /// <summary>
    /// 打开设置前的身份验证：锁定未生效时直接放行；
    /// 生效且要求设置验证时弹出解锁对话框（密码或两步验证码任一通过即可）。
    /// </summary>
    private async Task<bool> AuthorizeAsync()
    {
        if (!LockService.IsEnforced(_settings.Lock) || !_settings.Lock.RequireAuthForSettings) return true;
        return await UnlockAsync();
    }

    /// <summary>编辑看板前的身份验证：本次会话验证通过一次后不再重复询问。</summary>
    private bool _editAuthGranted;
    private bool _editAuthPending;

    private bool NeedsEditAuth() =>
        !_editAuthGranted && LockService.IsEnforced(_settings.Lock) && _settings.Lock.RequireAuthForEditing;

    /// <summary>需要验证时先弹出解锁对话框，通过后再进入编辑；同步调用方只管触发。</summary>
    private void RequestEditAuthThenEnter()
    {
        if (!NeedsEditAuth()) { EnterEditing(); return; }
        // 弹窗尚未结束时重复点击不叠新对话框。
        if (_editAuthPending) return;
        _editAuthPending = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (await UnlockAsync())
                {
                    _editAuthGranted = true;
                    EnterEditing();
                }
            }
            finally { _editAuthPending = false; }
        });
    }

    /// <summary>解锁对话框：密码或两步验证码任一通过即可。</summary>
    private async Task<bool> UnlockAsync()
    {
        TextBox input = new() { Header = "密码或验证码", PlaceholderText = "输入密码或 6 位验证码" };
        ContentDialog dialog = new()
        {
            Title = "已锁定", Content = input, PrimaryButtonText = "解锁", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = RootShell.XamlRoot
        };
        if (_dialogOpen) return false;
        _dialogOpen = true;
        ContentDialogResult result;
        try { result = await dialog.ShowAsync(); }
        finally { _dialogOpen = false; }
        if (result != ContentDialogResult.Primary) return false;
        string text = input.Text.Trim();
        bool ok = LockService.VerifyPassword(_settings.Lock, text) || LockService.VerifyTotp(_settings.Lock.TotpSecret, text, DateTimeOffset.Now);
        if (!ok) await ShowMessageAsync("解锁失败", "密码或验证码不正确。", "知道了");
        return ok;
    }

    /// <summary>启动参数直接进设置页的异步入口，验证失败停留在看板。</summary>
    private async Task TryShowSettingsAsync()
    {
        if (await AuthorizeAsync()) ShowSettings();
    }
}
