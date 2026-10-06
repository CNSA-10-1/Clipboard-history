namespace ClipboardHistory.Models;

/// <summary>用户设置。存成 settings.json，字段定义见 docs/04-数据规范.md。</summary>
public sealed class AppSettings
{
    public const int MinRetentionDays = 1;
    public const int MaxRetentionDays = 365;
    public const int MinMaxTotalMB = 50;
    public const int MaxMaxTotalMB = 10240;

    public const int DefaultRetentionDays = 3;
    public const int DefaultMaxTotalMB = 500;

    public int Version { get; set; } = 1;

    /// <summary>保留天数。置顶项不受它影响。</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>最大总容量（MB），超出后先删最旧的非置顶项。</summary>
    public int MaxTotalMB { get; set; } = DefaultMaxTotalMB;

    /// <summary>开机自动启动。</summary>
    public bool AutoStart { get; set; }

    /// <summary>主题。</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>
    /// 把超出范围的字段拉回合法区间。
    ///
    /// 从文件读出来时也要调一次：settings.json 是纯文本，用户可能手改过，
    /// 或者被别的程序写坏。宁可回退到默认值，也不能拿着个 0 天或 -5MB 去跑清理逻辑。
    /// </summary>
    public void Clamp()
    {
        RetentionDays = Math.Clamp(RetentionDays, MinRetentionDays, MaxRetentionDays);
        MaxTotalMB = Math.Clamp(MaxTotalMB, MinMaxTotalMB, MaxMaxTotalMB);

        if (!Enum.IsDefined(Theme))
        {
            Theme = AppTheme.System;
        }
    }

    /// <summary>复制一份，用于设置窗口的"取消"按钮丢弃改动。</summary>
    public AppSettings Clone() => new()
    {
        Version = Version,
        RetentionDays = RetentionDays,
        MaxTotalMB = MaxTotalMB,
        AutoStart = AutoStart,
        Theme = Theme,
    };
}
