using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>
/// 把历史记录写回剪贴板。点卡片就是走这里。
/// </summary>
public static class ClipboardWriter
{
    /// <summary>
    /// 写回一条记录。
    ///
    /// 写成功后**必须**调 <see cref="ClipboardMonitor.IgnoreCurrentContent"/>，
    /// 否则我们自己这次写入会触发一次剪贴板变化，历史里就会自己刷自己——
    /// 用户每点一次卡片，列表顶部就多冒出同一条内容。
    /// </summary>
    public static bool Write(ClipItem item, ClipboardMonitor monitor)
    {
        var data = BuildData(item);
        if (data is null)
        {
            return false;
        }

        var ok = ClipboardRetry.Try(() =>
        {
            // copy: true —— 让数据在我们退出后依然留在剪贴板上。
            // 用 false 的话，本进程一关，用户再去粘贴就发现剪贴板空了。
            Clipboard.SetDataObject(data, true);
        });

        if (ok)
        {
            monitor.IgnoreCurrentContent();
        }

        return ok;
    }

    private static DataObject? BuildData(ClipItem item) => item.Kind switch
    {
        ClipKind.Text => BuildText(item.Text),
        ClipKind.Image => BuildImage(item),
        ClipKind.Files => BuildFiles(item),
        _ => null,
    };

    private static DataObject? BuildText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        return data;
    }

    private static DataObject? BuildImage(ClipItem item)
    {
        var entry = item.FirstImage;
        if (entry is null)
        {
            return null;
        }

        var bitmap = LoadOriginal(entry);
        if (bitmap is null)
        {
            return null;
        }

        var data = new DataObject();
        data.SetImage(bitmap);
        return data;
    }

    /// <summary>
    /// 读原图（不是缩略图）。缩略图只有 200px，粘出去会糊。
    /// 用 OnLoad 保证读完就撒手，不然这张图会一直占着文件锁。
    /// </summary>
    private static BitmapSource? LoadOriginal(ImageEntry entry)
    {
        var path = AppPaths.ImagePath(entry.ImageFile);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException)
        {
            return null;
        }
    }

    private static DataObject? BuildFiles(ClipItem item)
    {
        var collection = new StringCollection();

        // 只放回**还存在**的路径。已经失效的放进去，粘贴时资源管理器会弹一个
        // "找不到文件"的错误框，还不如干脆不粘它。
        foreach (var entry in item.Files.Where(f => f.Exists))
        {
            collection.Add(entry.Path);
        }

        if (collection.Count == 0)
        {
            return null;
        }

        var data = new DataObject();
        data.SetFileDropList(collection);
        return data;
    }
}
