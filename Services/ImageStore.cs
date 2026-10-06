using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardHistory.Services;

/// <summary>保存一张图的结果：元数据 + 原图的 PNG 字节（字节用来算内容指纹）。</summary>
public readonly record struct SavedImage(Models.ImageEntry Entry, byte[] Bytes);

/// <summary>给磁盘上的图片文件生成的缩略图。<see cref="FileName"/> 为空串表示没生成成功。</summary>
public readonly record struct SavedThumbnail(string FileName, long SizeBytes);

/// <summary>
/// 图片的落盘。原图和缩略图分开存：
/// 列表里只加载缩略图（长边 ≤ 200px），原图只在真正写回剪贴板时才读，
/// 不然一个 4K 截图的卡片就能让列表滚动卡顿。
/// </summary>
public static class ImageStore
{
    /// <summary>缩略图长边上限。够卡片显示，又不至于占内存。</summary>
    private const int ThumbnailMaxEdge = 200;

    /// <summary>把一张图存到 images 目录，返回元数据。</summary>
    public static SavedImage Save(BitmapSource source)
    {
        // 每张图一个自己的 GUID，不用条目 id：一条记录可以带多张图，
        // 用条目 id 命名的话第二张就把第一张覆盖了。
        var imageId = Guid.NewGuid().ToString("N");

        var entry = new Models.ImageEntry
        {
            ImageFile = $"{imageId}.png",
            ThumbFile = $"{imageId}_thumb.png",
            Width = source.PixelWidth,
            Height = source.PixelHeight,
        };

        var originalBytes = EncodePng(source);
        File.WriteAllBytes(AppPaths.ImagePath(entry.ImageFile), originalBytes);
        File.WriteAllBytes(AppPaths.ImagePath(entry.ThumbFile), EncodePng(CreateThumbnail(source)));

        entry.SizeBytes = originalBytes.Length;
        return new SavedImage(entry, originalBytes);
    }

    /// <summary>
    /// 给磁盘上的一个图片文件生成缩略图并落盘，返回缩略图文件名。
    /// 任何一步失败都返回空串——**预览是锦上添花，不能因为它拖垮整次复制**。
    /// </summary>
    public static SavedThumbnail SaveThumbnailFor(string sourcePath)
    {
        try
        {
            var source = LoadOriented(sourcePath);
            if (source is null)
            {
                return default;
            }

            var name = $"{Guid.NewGuid():N}_thumb.png";
            var bytes = EncodePng(source);
            File.WriteAllBytes(AppPaths.ImagePath(name), bytes);

            return new SavedThumbnail(name, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or NotSupportedException or ArgumentException
                                      or FileFormatException or OverflowException)
        {
            Log.Error($"生成缩略图失败：{sourcePath}", ex);
            return default;
        }
    }

    /// <summary>
    /// 后台线程版本的缩略图生成。
    /// 解码几千万像素的照片是纯 CPU 活，放在界面线程上做，窗口会当场僵住。
    /// </summary>
    public static Task<SavedThumbnail> SaveThumbnailForAsync(string sourcePath) =>
        Task.Run(() => SaveThumbnailFor(sourcePath));

    /// <summary>
    /// 读一张图片文件并**摆正**它，同时缩到缩略图尺寸。
    ///
    /// 两件事都必须现在做：
    ///   1. <c>DecodePixelWidth</c> 让解码器直接吐出小图。相机原图几千万像素，
    ///      先解成全尺寸再缩会瞬间吃掉几百 MB 内存——复制十来张照片就能把程序撑爆。
    ///   2. EXIF 方向。手机和很多相机拍照时把传感器横着放，图片本身是躺倒的，
    ///      靠 EXIF 里一个"请旋转 90 度"的标记来纠正。WPF 的解码器**不会**自动应用它，
    ///      不管的话相机照片在卡片里全是横着的。
    /// </summary>
    private static BitmapSource? LoadOriented(string path)
    {
        var bitmap = new BitmapImage();

        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;          // 读完就松手，否则文件被锁着删不掉
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        bitmap.DecodePixelWidth = ThumbnailMaxEdge;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();

        var transform = OrientationTransform(ReadExifOrientation(bitmap));

        if (transform is null)
        {
            return bitmap;
        }

        var rotated = new TransformedBitmap(bitmap, transform);
        var frozen = new WriteableBitmap(rotated);
        frozen.Freeze();

        return frozen;
    }

    /// <summary>
    /// 读 EXIF 里的方向标记（标签 274）。读不到就当 1（本来就是正的）。
    ///
    /// 非 JPEG、或者元数据被裁掉的图片，这个查询会抛异常，属于正常情况，安静地当 1 处理。
    /// </summary>
    private static int ReadExifOrientation(BitmapSource bitmap)
    {
        try
        {
            if (bitmap.Metadata is BitmapMetadata metadata
                && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort orientation)
            {
                return orientation;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or InvalidOperationException)
        {
            // 没有 EXIF，正常
        }

        return 1;
    }

    /// <summary>
    /// EXIF 方向标记 → 让照片正过来需要的变换。1 表示本来就是正的。
    /// 5~8 是"先翻转再旋转"的组合，所以用 TransformGroup 而不是单个变换。
    /// </summary>
    private static Transform? OrientationTransform(int orientation)
    {
        switch (orientation)
        {
            case 2: return new ScaleTransform(-1, 1);                                  // 水平镜像
            case 3: return new RotateTransform(180);                                   // 倒过来
            case 4: return new ScaleTransform(1, -1);                                  // 垂直镜像
            case 5: return Combine(new RotateTransform(90), new ScaleTransform(-1, 1));
            case 6: return new RotateTransform(90);                                    // 顺时针 90°
            case 7: return Combine(new RotateTransform(270), new ScaleTransform(-1, 1));
            case 8: return new RotateTransform(270);                                   // 逆时针 90°

            // 1 = 正常，0 和别的怪值也按正常处理，宁可不动也不要乱转
            default: return null;
        }

        static Transform Combine(Transform rotate, Transform flip)
        {
            var group = new TransformGroup();
            group.Children.Add(rotate);   // 顺序不能反：先转再翻
            group.Children.Add(flip);
            return group;
        }
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// 生成缩略图。用 TransformedBitmap 而不是重新解码原图——
    /// 它是对已有位图做缩放变换，不用把原图再解一遍，省内存。
    /// </summary>
    private static BitmapSource CreateThumbnail(BitmapSource source)
    {
        var longestEdge = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longestEdge <= ThumbnailMaxEdge)
        {
            // 本来就够小，不用缩。但还是要一份独立的位图，
            // 因为原始对象可能挂在剪贴板上，剪贴板内容变了就失效了。
            var copy = new WriteableBitmap(source);
            copy.Freeze();
            return copy;
        }

        var scale = (double)ThumbnailMaxEdge / longestEdge;
        var thumbnail = new TransformedBitmap(source, new ScaleTransform(scale, scale));

        // 冻结：冻结后的位图不绑定线程，可以被界面之外的线程安全读取
        var frozen = new WriteableBitmap(thumbnail);
        frozen.Freeze();
        return frozen;
    }
}
