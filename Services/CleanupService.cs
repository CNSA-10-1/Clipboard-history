using System.Windows.Threading;

namespace ClipboardHistory.Services;

/// <summary>
/// 自动清理的调度：什么时候跑过期清理和容量淘汰。
///
/// 三个时机（见 docs/04-数据规范.md）：
///   1. 程序启动时
///   2. 每小时一次
///   3. 用户改了保留天数或容量上限之后立刻
///
/// 用 DispatcherTimer 而不是别的定时器：它跑在 UI 线程上，而清理要动
/// <see cref="StorageService.Items"/>（界面正绑着这个集合），跨线程改会直接抛异常。
/// 每小时一次的开销可以忽略，不值得为它搞一套线程同步。
/// </summary>
public sealed class CleanupService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly StorageService _storage;
    private readonly SettingsService _settings;

    private DispatcherTimer? _timer;
    private bool _disposed;

    public CleanupService(StorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
    }

    /// <summary>启动时跑一次，然后挂上每小时的定时器。</summary>
    public void Start()
    {
        RunNow();

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        _timer.Tick += (_, _) => RunNow();
        _timer.Start();
    }

    /// <summary>
    /// 立刻跑一次。启动、定时、以及用户改完设置都会调到这里。
    ///
    /// 整个包在 try 里：清理是后台的例行工作，它失败了不该影响用户正在做的事
    /// （比如刚点完保存却发现程序崩了）。
    /// </summary>
    public void RunNow()
    {
        try
        {
            var settings = _settings.Current;

            // 顺序要紧：**先删条目、再扫文件夹**。
            // 删条目时如果图片文件被别的程序占用没删掉，那次失败留下的孤儿
            // 正好在紧接着的扫描里被收走。反过来的话，这一轮新产生的孤儿
            // 要等到下一个小时才会被清理。
            _storage.RunCleanup(settings.RetentionDays, settings.MaxTotalMB);
            DataMaintenance.Run(_storage);
        }
        catch (Exception ex)
        {
            Log.Error("自动清理失败，已跳过这一次", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Stop();
        _timer = null;
    }
}
