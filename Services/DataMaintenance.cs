using System.IO;

namespace ClipboardHistory.Services;

/// <summary>
/// 数据文件夹自检。防的是"文件夹里悄悄攒垃圾"。
///
/// 这类问题的麻烦之处在于它**完全无声**：用户不会发现，等到发现时磁盘已经少了好几个 G。
/// 所以启动时统一扫一遍，把不该留在那儿的东西收走。
/// </summary>
internal static class DataMaintenance
{
    /// <summary>`index.unreadable-*.json` 最多留几份。</summary>
    private const int KeepUnreadableArchives = 3;

    /// <summary>跑一遍全部自检。</summary>
    public static void Run(StorageService storage)
    {
        DeleteStaleTempFiles();
        TrimUnreadableArchives();
        DeleteOrphanImages(storage);
    }

    /// <summary>
    /// 删掉写到一半留下的临时文件。
    ///
    /// 正常路径下 <see cref="AtomicFile"/> 的 `File.Replace` 会把它消费掉；
    /// 只有在替换之前崩溃/断电才会残留。删无可删就什么都不做。
    /// </summary>
    private static void DeleteStaleTempFiles()
    {
        foreach (var path in new[] { AppPaths.IndexFile + ".tmp", AppPaths.SettingsFile + ".tmp" })
        {
            TryDelete(path);
        }
    }

    /// <summary>
    /// 索引读不出来时会留档成 `index.unreadable-&lt;时间戳&gt;.json`，方便用户捞数据。
    /// 但那是带时间戳的名字，不做上限理论上能一直攒——只留最近几份。
    /// </summary>
    private static void TrimUnreadableArchives()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Root))
            {
                return;
            }

            var archives = Directory
                .EnumerateFiles(AppPaths.Root, "index.unreadable-*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)   // 新的在前
                .ToList();

            foreach (var old in archives.Skip(KeepUnreadableArchives))
            {
                TryDelete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("清理索引留档失败", ex);
        }
    }

    /// <summary>
    /// 删掉 images 目录里没有任何条目引用的文件。
    ///
    /// 孤儿是怎么来的：
    ///   1. 删条目时文件被别的程序占用，没删掉
    ///   2. 复制图片的过程中崩溃 —— 图片已经落盘、索引还没写
    ///   3. 去重合并时把新写下的图片丢下（这条已经在 Add 里补了，但保不齐还有别的路径）
    ///
    /// **只删 .png**：这个目录本来就是我们放的 png，但用户万一往里丢了个别的东西，
    /// 不该被当成垃圾收走。
    /// </summary>
    private static void DeleteOrphanImages(StorageService storage)
    {
        try
        {
            if (!Directory.Exists(AppPaths.ImagesDir))
            {
                return;
            }

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in storage.Items)
            {
                // 图片卡片：原图 + 缩略图
                foreach (var image in item.Images)
                {
                    if (image.ImageFile.Length > 0)
                    {
                        referenced.Add(image.ImageFile);
                    }

                    if (image.ThumbFile.Length > 0)
                    {
                        referenced.Add(image.ThumbFile);
                    }
                }

                // 文件卡片里那些图片文件也各有一张缩略图。
                // ⚠️ 这一份不能漏：漏了的话，下次启动会把用户所有照片的预览图
                // 当成孤儿全部删掉，而且**完全无声**——卡片还在，只是预览集体消失。
                foreach (var file in item.Files)
                {
                    if (file.ThumbFile.Length > 0)
                    {
                        referenced.Add(file.ThumbFile);
                    }
                }
            }

            foreach (var path in Directory.EnumerateFiles(AppPaths.ImagesDir, "*.png"))
            {
                if (!referenced.Contains(Path.GetFileName(path)))
                {
                    TryDelete(path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("清理孤儿图片失败", ex);
        }
    }

    /// <summary>删文件失败就算了——记日志跳过，不能因为一个文件删不掉就让整个自检崩掉。</summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"删除文件失败：{path}", ex);
        }
    }
}
