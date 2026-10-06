using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace ClipboardHistory.Converters;

/// <summary>
/// 内容区该给多高。绑定在"展开"按钮的 IsChecked 上，参数写成「收起高度|展开高度」，
/// 例如 <c>ConverterParameter=54|420</c>。
///
/// 分隔符用竖线不用逗号：逗号在 XAML 标记扩展里是属性之间的分隔符，
/// 写 <c>ConverterParameter=54,420</c> 会被解析成"另一个名为 420 的属性"而直接报错。
///
/// 展开态也要有上限，不能真的"不限"：不然复制一篇几千字的文章再展开，
/// 卡片会比屏幕还高，"收起"按钮被顶到看不见的地方，用户想收回去还得先滚很久。
/// 超出 420 之后在卡片内部滚动。
/// </summary>
public sealed class ClampHeightConverter : IValueConverter
{
    private const double Fallback = 54;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "").Split('|');
        var index = value is true ? 1 : 0;

        if (index < parts.Length
            && double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            return height;
        }

        return Fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 卡片内部的滚动条该不该显示。绑定在"展开"按钮的 IsChecked 上。
///
///   **收起时用 Hidden（不显示、也不占位）**：这是 ClampBehavior 能量准高度的前提。
///   一旦换成 Auto，内容会被滚动条挤窄，换行位置跟着变，量出来的高度就不是
///   "内容本来有多高"，卡片会平白判成装不下、多出一个点了没反应的「展开」按钮。
///
///   **展开时用 Auto**：只在确实滚得动的时候才出现滚动条。
///   用 Visible 的话，一段刚好装得下的短内容下面会挂一条空槽，看着像出错了。
/// </summary>
public sealed class ExpandToScrollBarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 决定内容上要不要盖一层渐隐遮罩。
///
/// 输入两个：[是否溢出, 是否已展开]。只有当"溢出了、而且没收起着"时才给遮罩。
///
/// 遮罩用 OpacityMask 而不是一层带颜色的渐变方块：卡片背景会随主题变（白 ↔ 深灰）、
/// 还会随鼠标悬停变，用带颜色的遮罩得配四套色值，一定会漏。OpacityMask 是让内容自己淡出、
/// 露出底下的真实背景，什么主题什么悬停状态都自动是对的。
/// </summary>
public sealed class FadeMaskConverter : IMultiValueConverter
{
    /// <summary>遮罩本身是固定的一份，可以冻结共享——它只描述"哪里透明"，跟主题无关。</summary>
    private static readonly Brush Mask = CreateMask();

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var isOverflowing = values.Length > 0 && values[0] is true;
        var isExpanded = values.Length > 1 && values[1] is true;

        return isOverflowing && !isExpanded ? Mask : null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush CreateMask()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            // 这里的白色不是"写死了颜色"：OpacityMask 只取 alpha 通道，
            // RGB 填什么都不影响结果，写白色是"无色"的惯用写法。
            // 详见 docs/03-设计规范.md 的「颜色审计」。
            GradientStops =
            {
                // 前 70% 完全不透明，最后一段淡出到透明。
                // 起点定在 0.70 而不是 0.55：裁剪高度正好是 3 行，从 55% 起淡等于让整个
                // 第三行都糊掉，用户没展开就看不清第三行写了什么，等于白显示一行。
                new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.70),
                new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.00),
            },
        };

        brush.Freeze();
        return brush;
    }
}
