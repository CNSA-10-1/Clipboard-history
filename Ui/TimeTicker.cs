using System.ComponentModel;
using System.Windows.Threading;

namespace ClipboardHistory.Ui;

/// <summary>
/// 一个会自己"报时"的对象，让界面上的"3 分钟前"能跟着时间走。
///
/// 为什么需要它：卡片上的时间是 <c>CreatedAt</c> 换算出来的相对说法，
/// 但 CreatedAt 本身不会变，绑定就不会重新求值——窗口开着不动，
/// "刚刚"能一直挂在那儿显示成"刚刚"。
///
/// 做法是让时间文案同时绑定 <see cref="Now"/>，定时器每 30 秒通知一次它的变化，
/// 绑定自然就重新算了一遍。
///
/// 只在窗口可见时走。窗口藏起来的时候没人看，白耗电。
/// </summary>
public sealed class TimeTicker : INotifyPropertyChanged
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public static TimeTicker Instance { get; } = new();

    private DispatcherTimer? _timer;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前时间。绑定它纯粹是为了触发重新求值，值本身没什么意义。</summary>
    public DateTimeOffset Now => DateTimeOffset.Now;

    public void Start()
    {
        _timer ??= CreateTimer();

        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Stop() => _timer?.Stop();

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Interval };
        timer.Tick += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Now)));
        return timer;
    }
}
