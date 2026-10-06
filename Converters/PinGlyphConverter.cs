using System.Globalization;
using System.Windows.Data;
using ClipboardHistory.Ui;

namespace ClipboardHistory.Converters;

/// <summary>图钉按钮的字形：已置顶显示"取消置顶"图标，未置顶显示"置顶"图标。</summary>
public sealed class PinGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Icons.Unpin : Icons.Pin;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 右键菜单里那一项该写什么。菜单项要说清楚"点下去会发生什么"，
/// 而不是像图标那样只表达当前状态——写成固定的"置顶"会让已置顶的条目
/// 看起来像是"再置顶一次"。
/// </summary>
public sealed class PinActionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "取消置顶" : "置顶";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
