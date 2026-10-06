using System.Globalization;
using System.Windows.Data;
using ClipboardHistory.Models;
using ClipboardHistory.Ui;

namespace ClipboardHistory.Converters;

/// <summary>把内容类型（ClipKind）转成左侧要显示的类型图标。</summary>
public sealed class KindIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ClipKind kind ? Icons.ForKind(kind) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>把文件卡片里的一个条目转成图标——文件夹还是文件。</summary>
public sealed class FileEntryIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is FileEntry entry ? Icons.ForEntry(entry) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
