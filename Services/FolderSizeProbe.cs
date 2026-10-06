using System.Diagnostics;
using System.IO;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>
/// 统计文件夹大小。**只在用户展开详情时按需调用**，而且一定在后台线程跑。
///
/// 为什么不在记录时就统计：递归遍历一个大文件夹可能要几十秒甚至更久，
/// 而这是在剪贴板监听回调里跑的——卡住它就等于整个软件卡住。
///
/// 为什么要设上限：有些目录（比如整个 D 盘、node_modules）几万个文件，
/// 完整统计没有意义。超了就放弃，界面显示"较大"。
/// </summary>
internal static class FolderSizeProbe
{
    private const int MaxFiles = 5000;

    private static readonly TimeSpan TimeLimit = TimeSpan.FromSeconds(2);

    /// <summary>后台统计文件夹大小。返回 <see cref="FileEntry.UnknownSize"/> 之外的字节数，
    /// 或 <see cref="FileEntry.TooLargeToMeasure"/> 表示太大放弃了。</summary>
    public static Task<long> MeasureAsync(string folderPath) =>
        Task.Run(() => Measure(folderPath));

    private static long Measure(string folderPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var pending = new Stack<string>();
        var fileCount = 0;
        long total = 0;

        pending.Push(folderPath);

        while (pending.Count > 0)
        {
            if (fileCount >= MaxFiles || stopwatch.Elapsed > TimeLimit)
            {
                return FileEntry.TooLargeToMeasure;
            }

            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        // 单个文件读不到（被占用、已删除）就跳过，不影响整体
                    }

                    if (++fileCount >= MaxFiles)
                    {
                        return FileEntry.TooLargeToMeasure;
                    }
                }

                foreach (var sub in Directory.EnumerateDirectories(current))
                {
                    pending.Push(sub);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 没权限的目录直接跳过
            }
            catch (IOException)
            {
                // 目录刚好被删掉了之类
            }
        }

        return total;
    }
}
