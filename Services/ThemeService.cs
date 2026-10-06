using System.Windows;
using ClipboardHistory.Models;
using ClipboardHistory.Ui;
using Microsoft.Win32;

namespace ClipboardHistory.Services;

/// <summary>
/// 主题切换。
///
/// 做法是替换 Application.Resources 里的一份配色字典：把旧的移除、新的插到最前面。
/// 前提是所有界面都通过 DynamicResource 引用主题色——用 StaticResource 的话，
/// 资源在加载时就固化了，切主题不会有任何反应。
/// </summary>
public static class ThemeService
{
    /// <summary>当前选择的主题（可能是 System）。</summary>
    public static AppTheme Current { get; private set; } = AppTheme.System;

    /// <summary>实际生效的是不是深色。选 System 时取决于系统设置。</summary>
    public static bool IsDark { get; private set; }

    private static ResourceDictionary? _activeDictionary;

    /// <summary>应用指定主题。设置界面切换主题、以及启动时都会调用。</summary>
    public static void Apply(AppTheme theme)
    {
        Current = theme;
        IsDark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => IsSystemDark(),
        };

        SwapDictionary(IsDark ? "Dark" : "Light");

        // 标题栏是系统画的，不会跟着上面的配色字典走，得单独告诉它一声。
        // 放在这里是因为所有切主题的路径最后都会走到这个方法。
        WindowChrome.ApplyToAllWindows(IsDark);
    }

    /// <summary>
    /// 每次窗口显示时调用一次。
    ///
    /// 为什么不监听系统主题变化事件？因为那需要额外引入 Microsoft.Win32.SystemEvents 这个
    /// NuGet 包，而本项目要求零依赖。窗口隐藏时主题变了也没人看得见，所以"显示时读一次注册表"
    /// 既够用又零成本。
    /// </summary>
    public static void RefreshIfFollowingSystem()
    {
        if (Current == AppTheme.System)
        {
            Apply(AppTheme.System);
        }
    }

    /// <summary>
    /// 读注册表判断系统当前是不是深色。
    /// AppsUseLightTheme：1 = 浅色，0 = 深色。读不到就按浅色处理。
    /// </summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            // 注册表读取失败不该让程序起不来，退回浅色即可
            return false;
        }
    }

    private static void SwapDictionary(string name)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{name}.xaml", UriKind.Absolute),
        };

        if (_activeDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_activeDictionary);
        }

        // 插到最前面：主题色优先级低于后面加载的通用样式，但高于系统默认
        app.Resources.MergedDictionaries.Insert(0, dictionary);
        _activeDictionary = dictionary;
    }
}
