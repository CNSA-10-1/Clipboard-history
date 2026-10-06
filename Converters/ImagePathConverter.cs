using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ClipboardHistory.Services;

namespace ClipboardHistory.Converters;

/// <summary>
/// 把图片文件名转成界面能用的位图。文件不存在或读不出来就返回 null，界面自然会显示占位。
/// </summary>
public sealed class ImagePathConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string fileName || string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var path = AppPaths.ImagePath(fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();

            // ⚠️ 关键：OnLoad 表示"读完就把文件关掉"。
            // 用默认的 OnDemand 的话，文件会一直被这个位图占着锁住，
            // 之后删卡片、清理过期内容都会因为"文件被占用"而失败。
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            // 不走 WPF 的图片缓存：同一个文件被删掉又重新生成时，
            // 缓存会返回旧的那张图（缩略图文件名是新的，但保险起见）。
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or FileFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
