using System.Globalization;
using System.Windows.Data;

namespace ClipboardHistory.Converters;

/// <summary>
/// 把时间转成"刚刚 / 3 分钟前 / 昨天 / 10月3日"这种相对说法。
/// 卡片上显示相对时间更好读；精确时间放在鼠标悬停提示里。
///
/// 是 MultiBinding 而不是普通 Binding：第二个输入是 TimeTicker.Now，
/// 只用来让绑定定期重新求值（见 <see cref="Ui.TimeTicker"/>），值本身不参与计算。
/// </summary>
public sealed class RelativeTimeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] is not DateTimeOffset time)
        {
            return "";
        }

        // 第二个值（TimeTicker.Now）只是刷新用的，不读它

        var now = DateTimeOffset.Now;
        var span = now - time;

        // 时间在未来（系统时间被调整过），别显示成"-3 分钟前"
        if (span < TimeSpan.Zero)
        {
            return "刚刚";
        }

        if (span < TimeSpan.FromMinutes(1))
        {
            return "刚刚";
        }

        if (span < TimeSpan.FromHours(1))
        {
            return $"{(int)span.TotalMinutes} 分钟前";
        }

        // 按"自然日"比较，而不是按 24 小时。
        // 凌晨 1 点复制的，早上 9 点该显示"今天"，而不是"8 小时前"。
        var daysAgo = (now.Date - time.LocalDateTime.Date).Days;

        return daysAgo switch
        {
            0 => $"{(int)span.TotalHours} 小时前",
            1 => $"昨天 {time.LocalDateTime:HH:mm}",
            2 => $"前天 {time.LocalDateTime:HH:mm}",
            _ => time.LocalDateTime.ToString("M月d日 HH:mm", culture),
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
