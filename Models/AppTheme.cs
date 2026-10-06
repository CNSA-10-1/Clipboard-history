namespace ClipboardHistory.Models;

/// <summary>
/// 主题选项。放在 Models 而不是 Services，是因为它同时被两个地方用到：
/// 用户设置（AppSettings.Theme）和界面主题切换（ThemeService）。
/// 放 Services 的话 Models 就得反过来依赖 Services。
/// </summary>
public enum AppTheme
{
    /// <summary>浅色淡蓝。</summary>
    Light,

    /// <summary>深色。</summary>
    Dark,

    /// <summary>跟随 Windows 的"应用模式"设置。</summary>
    System,
}
