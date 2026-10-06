using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipboardHistory.Models;
using ClipboardHistory.Services;
using ClipboardHistory.Ui;

namespace ClipboardHistory;

/// <summary>
/// 设置窗口。
///
/// 改动只在点"保存"时才写盘并生效，点"取消"或按 Esc 直接丢弃——
/// 和"边改边生效"相比，这样用户还能反悔。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly StorageService _storage;

    public SettingsWindow(SettingsService settings, StorageService storage)
    {
        _settings = settings;
        _storage = storage;

        InitializeComponent();

        // 标题栏是系统画的，得单独跟着主题走
        WindowChrome.FollowTheme(this);

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var current = _settings.Current;

        RetentionBox.Text = current.RetentionDays.ToString(CultureInfo.InvariantCulture);
        CapacityBox.Text = current.MaxTotalMB.ToString(CultureInfo.InvariantCulture);
        AutoStartSwitch.IsChecked = current.AutoStart;

        ThemeLight.IsChecked = current.Theme == AppTheme.Light;
        ThemeDark.IsChecked = current.Theme == AppTheme.Dark;
        ThemeSystem.IsChecked = current.Theme == AppTheme.System;

        LoadAbout();
    }

    /// <summary>
    /// 填"关于"里的版本号和当前 exe 路径。
    ///
    /// 为什么要显示路径：这个程序在同一台机器上可能存在好几份（开发目录里的 Debug 版、
    /// Release 版、打包出来的发布版…）。出问题时用户通常会说"我改了怎么没用"，
    /// 而真正的原因往往是双击到了旧的那一份。界面上直接写着路径，一眼就能对出来。
    ///
    /// 路径用 <see cref="Environment.ProcessPath"/>，不用 Assembly.Location——
    /// 单文件发布下后者是空字符串。
    /// </summary>
    private void LoadAbout()
    {
        var assembly = typeof(SettingsWindow).Assembly;

        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString(3)
                      ?? "未知";

        // 有些构建方式会往版本号后面接一段 "+<提交号>"，那部分对用户没意义
        version = version.Split('+')[0];

        AboutVersion.Text = $"剪贴板历史 {version}";
        AboutPath.Text = Environment.ProcessPath ?? "(取不到路径)";
    }

    // ── 输入限制 ──

    /// <summary>
    /// 数字框只吃数字。在这儿拦掉比等用户填完再报错友好得多——
    /// 报告一个"这里不能填字母"比报告一个"值不合法"具体得多。
    /// </summary>
    private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    /// <summary>粘贴也要拦，不然用户从别处粘个 "abc" 进来就绕过去了。</summary>
    private void NumberBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is not string pasted
            || !pasted.All(char.IsAsciiDigit))
        {
            e.CancelCommand();
        }
    }

    private void PresetDays_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            RetentionBox.Text = tag;
        }
    }

    // ── 保存 / 取消 ──

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var current = _settings.Current;

        if (!TryReadNumber(RetentionBox, AppSettings.MinRetentionDays, AppSettings.MaxRetentionDays,
                           "保留期限", current.RetentionDays, out var days)
            || !TryReadNumber(CapacityBox, AppSettings.MinMaxTotalMB, AppSettings.MaxMaxTotalMB,
                              "最大存储容量", current.MaxTotalMB, out var capacity))
        {
            return;
        }

        var updated = new AppSettings
        {
            Version = current.Version,
            RetentionDays = days,
            MaxTotalMB = capacity,
            AutoStart = AutoStartSwitch.IsChecked == true,
            Theme = SelectedTheme(),
        };

        // 开机自启要动注册表，得确认写成功了再存设置——
        // 存了却没写进去的话，下次打开设置会显示"已开启"，但实际开机并不启动，
        // 用户完全没法察觉。
        if (!StartupService.TrySet(updated.AutoStart, out var error))
        {
            MessageWindow.Info(this,
                "设置开机启动失败",
                (error ?? "写注册表失败。") + "\n\n其他设置没有被保存，请先解决这个问题。");

            AutoStartSwitch.IsChecked = current.AutoStart;
            return;
        }

        _settings.Save(updated);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    /// <summary>
    /// 读一个数字框。不合法就弹提示、把输入框**恢复成上一次的有效值**，
    /// 然后返回 false 让调用方中止保存。
    ///
    /// 关键点是"什么都不写、窗口也不关"——绝不能把坏值存进 settings.json，
    /// 否则清理逻辑会拿着一个 0 天到处删东西。
    /// </summary>
    private bool TryReadNumber(
        TextBox box, int min, int max, string label, int lastValid, out int value)
    {
        value = 0;

        if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            RejectNumber(box, lastValid);
            MessageWindow.Info(this, "输入有误",
                $"{label}要填一个数字，已经改回上一次的有效值（{lastValid}）。");
            return false;
        }

        if (parsed < min || parsed > max)
        {
            RejectNumber(box, lastValid);
            MessageWindow.Info(this, "输入有误",
                $"{label}要在 {min} 到 {max} 之间，已经改回上一次的有效值（{lastValid}）。");
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>把一个被拒的输入恢复成上一次的有效值，并把光标放回去方便直接重打。</summary>
    private static void RejectNumber(TextBox box, int lastValid)
    {
        box.Text = lastValid.ToString(CultureInfo.InvariantCulture);
        box.Focus();
        box.SelectAll();
    }

    private AppTheme SelectedTheme()
    {
        if (ThemeLight.IsChecked == true)
        {
            return AppTheme.Light;
        }

        if (ThemeDark.IsChecked == true)
        {
            return AppTheme.Dark;
        }

        return AppTheme.System;
    }

    // ── 数据操作 ──

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        ShellService.OpenFolder(AppPaths.Root);

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var count = _storage.Items.Count;

        if (count == 0)
        {
            MessageWindow.Info(this, "清空所有历史", "现在没有可清空的内容。");
            return;
        }

        // 二次确认。文案里把"置顶的也会删"单独列出来——那是最容易让人后悔的一点，
        // 混在一句话里用户会漏看。
        var confirmed = MessageWindow.Confirm(
            this,
            "清空所有历史",
            $"确定要清空全部 {count} 条历史吗？\n\n"
            + "· 置顶的内容也会一起删掉\n"
            + "· 图片文件会一并删除\n"
            + "· 删掉之后没法恢复\n\n"
            + "保留期限等设置不受影响。",
            confirmText: "清空",
            danger: true);

        if (!confirmed)
        {
            return;
        }

        _storage.Clear();

        MessageWindow.Info(this, "清空所有历史", $"已清空 {count} 条历史。");
    }
}
