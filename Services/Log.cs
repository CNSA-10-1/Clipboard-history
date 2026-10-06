using System.IO;
using System.Text;

namespace ClipboardHistory.Services;

/// <summary>
/// 极简的错误日志。只记异常，不记正常流程。
///
/// 为什么不引入日志框架：项目要求零第三方依赖，而这里需要的只是"出事了留个痕迹"。
///
/// **日志文件有大小上限**：这个软件整天在后台跑，万一某个异常反复触发，
/// 日志能几小时就涨到几百兆。超过上限就整体截断重来。
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;

    private static readonly Lock Gate = new();

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            var line = new StringBuilder()
                .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                .Append(message);

            if (ex is not null)
            {
                line.AppendLine().Append(ex);
            }

            line.AppendLine().AppendLine();

            var path = Path.Combine(AppPaths.Root, "error.log");

            lock (Gate)
            {
                AppPaths.EnsureCreated();

                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Delete(path);
                }

                File.AppendAllText(path, line.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // 记日志本身失败就算了，绝不能让"记录错误"变成新的错误
        }
    }
}
