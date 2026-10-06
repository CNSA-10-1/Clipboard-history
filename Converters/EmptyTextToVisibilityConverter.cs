using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClipboardHistory.Converters;

/// <summary>
/// 字符串为空 → Visible，非空 → Collapsed。
///
/// 两个用途：
///   1. 搜索框的占位文字（"搜索历史…"）—— 输入框为空时才显示
///   2. 搜索框右侧的清除按钮 —— 有内容时才显示
/// 后者传 ConverterParameter="invert" 取反。
/// </summary>
public sealed class EmptyTextToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrEmpty(value as string);
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);

        return isEmpty ^ invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
