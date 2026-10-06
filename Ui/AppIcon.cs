using System.IO;
using System.Windows;
using ClipboardHistory.Services;
using Drawing = System.Drawing;

namespace ClipboardHistory.Ui;

/// <summary>
/// 读程序自己的图标（`Assets/app.ico`）。
///
/// 为什么不用 <c>Icon.ExtractAssociatedIcon(ProcessPath)</c>：那个返回的是系统挑的一张，
/// 尺寸由它说了算。托盘需要的是**恰好 16×16** 那张——给了 256 的，Windows 会现场缩，
/// 结果就是一团糊。
///
/// 为什么直接读资源流而不是按路径打开：打包成单文件 exe 之后没有 Assets 目录，
/// 路径是不存在的。资源是嵌在程序里的，什么打包方式都在。
/// </summary>
public static class AppIcon
{
    private const string ResourceUri = "pack://application:,,,/Assets/app.ico";

    /// <summary>
    /// 取出指定尺寸的图标。取不到就返回 null，调用方自己兜底。
    /// </summary>
    public static Drawing.Icon? Load(int size)
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri(ResourceUri))?.Stream;

            if (stream is null)
            {
                return null;
            }

            return new Drawing.Icon(stream, new Drawing.Size(size, size));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UriFormatException)
        {
            Log.Error("读取程序图标失败，托盘会用系统默认图标兜底", ex);
            return null;
        }
    }
}
