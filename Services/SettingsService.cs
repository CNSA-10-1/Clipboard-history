using System.IO;
using System.Text.Json;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>
/// 用户设置的读写。字段定义见 docs/04-数据规范.md。
/// </summary>
public sealed class SettingsService
{
    private readonly Lock _fileLock = new();

    /// <summary>当前生效的设置。任何时候都不会是 null，读不出来就是一份默认值。</summary>
    public AppSettings Current { get; private set; } = new();

    /// <summary>设置变了（比如主题要从浅色换成深色）。界面据此跟着调整。</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 从磁盘读设置。启动时调一次。
    ///
    /// 读不出来、或者某个值超出范围，一律**回退到默认值**而不是报错——
    /// settings.json 是纯文本，用户可能手改过，别的程序也可能写过，
    /// 拿着一个 0 天或 -5MB 去跑清理逻辑会出大问题。
    /// </summary>
    public void Load()
    {
        if (!File.Exists(AppPaths.SettingsFile))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonConfig.Options);

            if (loaded is null)
            {
                Current = new AppSettings();
                return;
            }

            // 存进文件之前先夹紧：手改出来的 0 天、-5MB 在这里就被拉回合法区间
            var wasOutOfRange = loaded.RetentionDays < AppSettings.MinRetentionDays
                                || loaded.RetentionDays > AppSettings.MaxRetentionDays
                                || loaded.MaxTotalMB < AppSettings.MinMaxTotalMB
                                || loaded.MaxTotalMB > AppSettings.MaxMaxTotalMB;

            loaded.Clamp();
            Current = loaded;

            if (wasOutOfRange)
            {
                Log.Error($"settings.json 里有超出范围的值，已拉回合法区间（期限 {Current.RetentionDays} 天，容量 {Current.MaxTotalMB} MB）");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Error("settings.json 读取失败，本次使用默认设置", ex);
            Current = new AppSettings();
        }
    }

    /// <summary>保存设置并立即生效。</summary>
    public void Save(AppSettings settings)
    {
        // 兜底再夹紧一次。调用方已经校验过，但"绝不把坏值写进文件"这条
        // 值得在最后一道关口再确认——这个函数是唯一的写入口。
        settings.Clamp();

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonConfig.Options);

            lock (_fileLock)
            {
                AppPaths.EnsureCreated();
                AtomicFile.Write(AppPaths.SettingsFile, json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去也让本次会话按新设置跑，下次启动再读旧的
            Log.Error("settings.json 写入失败，本次运行仍按新设置生效", ex);
        }

        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
