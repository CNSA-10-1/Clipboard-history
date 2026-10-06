using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>
/// 把剪贴板里的内容读成一条 <see cref="ClipItem"/>。
///
/// 格式优先级是 **文件 → 图片 → 文字**（见 docs/02-技术方案.md）：
/// 一次复制可能同时带好几种格式（比如从浏览器复制图片，图片和文字格式都有），
/// 取第一个非空的，避免同一份内容被存成两条。
///
/// 当前实现了文字（S6）和图片（S8）。文件在 S9。
/// </summary>
internal static class ClipboardCapture
{
    /// <summary>读一次剪贴板。没有可用内容时返回 null。</summary>
    public static ClipItem? Capture()
    {
        // 优先级：文件 > 图片 > 文字。
        // 一次复制可能同时带好几种格式，取第一个有内容的，避免同一份东西被存成两条。
        return CaptureFiles() ?? CaptureImage() ?? CaptureText();
    }

    // ── 文件 / 文件夹 ──

    /// <summary>
    /// 一次复制最多给几张图生成缩略图。
    ///
    /// 生成缩略图要解码原图，是这条路上唯一的重活，而且跑在界面线程上。
    /// 一次选中整个相册文件夹（几百张照片）去复制，不做上限的话窗口会僵住好几秒——
    /// 与其那样，不如只给前几张出预览，剩下的还是图标。
    /// </summary>
    private const int MaxThumbnailsPerCapture = 20;

    private static ClipItem? CaptureFiles()
    {
        var paths = ClipboardRetry.TryGet(() =>
            Clipboard.ContainsFileDropList()
                ? Clipboard.GetFileDropList()?.Cast<string>().ToArray()
                : null);

        if (paths is null || paths.Length == 0)
        {
            return null;
        }

        var entries = new List<FileEntry>();
        var thumbnailBudget = MaxThumbnailsPerCapture;
        var thumbnailBytes = 0L;

        foreach (var path in paths)
        {
            var isDirectory = Directory.Exists(path);
            var isFile = File.Exists(path);

            // 两种都不是：路径在复制的那一刻就已经失效了，不值得记
            if (!isDirectory && !isFile)
            {
                continue;
            }

            var size = FileEntry.UnknownSize;
            var thumbFile = "";

            if (isFile)
            {
                try
                {
                    // 记的是"复制那一刻的大小"。之后文件变大变小都不再更新——这是历史记录，不是文件监视器。
                    size = new FileInfo(path).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 读不到大小就留 UnknownSize，界面显示"—"
                }

                // 图片文件顺手生一张缩略图，卡片里就能直接看到内容。
                // 失败了就留空串，卡片退回显示类型图标——预览有就赚，没有也不影响记录本身。
                if (thumbnailBudget > 0 && FileEntry.IsImagePath(path))
                {
                    thumbnailBudget--;

                    var thumbnail = ImageStore.SaveThumbnailFor(path);
                    thumbFile = thumbnail.FileName;
                    thumbnailBytes += thumbnail.SizeBytes;
                }
            }

            entries.Add(new FileEntry
            {
                Path = path,
                IsDirectory = isDirectory,
                SizeBytes = size,
                ThumbFile = thumbFile,
            });
        }

        if (entries.Count == 0)
        {
            return null;
        }

        return new ClipItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = ClipKind.Files,
            // 文件名拼起来存进 Text，这样搜索能搜到文件名
            Text = string.Join(' ', entries.Select(e => e.Name)),
            Files = [.. entries],
            Hash = ClipHash.ForFiles(entries),
            CreatedAt = DateTimeOffset.Now,
            // 缩略图也算进占用：它是这条记录实打实写在磁盘上的东西，
            // 不计进去的话容量淘汰会漏算，用户设的上限就守不住了
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(
                string.Join('\n', entries.Select(e => e.Path))) + thumbnailBytes,
        };
    }

    // ── 文字 ──

    private static ClipItem? CaptureText()
    {
        var text = ClipboardRetry.TryGet(() =>
            Clipboard.ContainsText() ? Clipboard.GetText() : null);

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new ClipItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = ClipKind.Text,
            Text = text,
            Hash = ClipHash.ForText(text),
            CreatedAt = DateTimeOffset.Now,
            SizeBytes = System.Text.Encoding.UTF8.GetByteCount(text),
        };
    }

    // ── 图片 ──

    private static ClipItem? CaptureImage()
    {
        var sources = ClipboardRetry.TryGet(ReadBitmaps);
        if (sources is null || sources.Count == 0)
        {
            return null;
        }

        var saved = new List<SavedImage>();
        foreach (var source in sources)
        {
            try
            {
                saved.Add(ImageStore.Save(source));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 某一张存不下（磁盘满、格式不认识）就跳过它，
                // 不要因为一张图把整次复制都丢掉
            }
        }

        if (saved.Count == 0)
        {
            return null;
        }

        // 指纹取所有图片字节拼起来的哈希：一次复制多张图时，
        // 只哈希第一张的话，换了第二张还会被当成同一条。
        var allBytes = saved.SelectMany(s => s.Bytes).ToArray();

        return new ClipItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = ClipKind.Image,
            Images = [.. saved.Select(s => s.Entry)],
            Hash = ClipHash.ForImageBytes(allBytes),
            CreatedAt = DateTimeOffset.Now,
            SizeBytes = saved.Sum(s => s.Entry.SizeBytes),
        };
    }

    /// <summary>
    /// 从剪贴板取出位图。
    ///
    /// 先试着看有没有多张（极少数程序会一次塞好几张，比如数组或流集合），
    /// 拿不到再走最常规的单张路径。
    ///
    /// **多张那条路必须能失败而不影响单张**：它要读的是原始剪贴板对象，
    /// 而对方放的可能是个 .NET 序列化对象——.NET 9 移除了反序列化要用的 BinaryFormatter，
    /// 读它会抛 PlatformNotSupportedException。这种情况下应该安静地退回去拿第一张，
    /// 而不是把整次复制都丢掉。
    ///
    /// 另外说清楚：**这条多张路径实际上很难被走到**。Windows 剪贴板没有标准的"多张图片"格式，
    /// 常规的一次复制就是一张位图。模型和卡片支持多张（手造数据验证过），
    /// 但真实捕获基本永远只会有 1 张。
    /// </summary>
    private static List<BitmapSource>? ReadBitmaps()
    {
        var result = new List<BitmapSource>();

        var raw = ClipboardRetry.TryGet(() =>
            Clipboard.GetDataObject() is { } data && data.GetDataPresent(DataFormats.Bitmap)
                ? data.GetData(DataFormats.Bitmap)
                : null);

        if (raw is not null)
        {
            CollectBitmaps(raw, result);
        }

        if (result.Count == 0)
        {
            var single = ClipboardRetry.TryGet(() =>
                Clipboard.ContainsImage() ? Clipboard.GetImage() : null);

            if (single is not null)
            {
                result.Add(single);
            }
        }

        return result;
    }

    private static void CollectBitmaps(object? raw, List<BitmapSource> into)
    {
        switch (raw)
        {
            case null:
                return;

            case BitmapSource source:
                into.Add(source);
                return;

            // byte[] 必须排在 Array 前面：它本身就是 Array，
            // 落到下面那个分支就会被当成"数组，逐个拆"，把字节一个个当图片解，全解不出来。
            case byte[] bytes:
                using (var memory = new MemoryStream(bytes))
                {
                    if (Decode(memory) is { } fromBytes)
                    {
                        into.Add(fromBytes);
                    }
                }

                return;

            case Stream stream:
                if (Decode(stream) is { } fromStream)
                {
                    into.Add(fromStream);
                }

                return;

            // 其他数组：逐个拆（可能是 Stream[] 或 BitmapSource[]）
            case Array array:
                foreach (var item in array)
                {
                    CollectBitmaps(item, into);
                }

                return;
        }
    }

    private static BitmapSource? Decode(Stream stream)
    {
        try
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FileFormatException)
        {
            return null;
        }
    }
}
