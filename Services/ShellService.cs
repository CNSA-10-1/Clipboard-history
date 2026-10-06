using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>调起资源管理器。文件卡片的"点击路径打开所在位置"用它。</summary>
public static class ShellService
{
    /// <summary>
    /// 在资源管理器里定位到这个条目。
    /// 文件 → 打开所在文件夹并**选中**它；文件夹 → 直接打开它。
    ///
    /// 路径已经失效时不硬报错：能退到父目录就退到父目录（用户至少能看到附近的文件），
    /// 退不了就什么都不做。失败静默处理——为了一个"打开文件夹"弹个错误框不值当。
    /// </summary>
    public static void RevealInExplorer(FileEntry entry)
    {
        try
        {
            if (entry.IsDirectory && Directory.Exists(entry.Path))
            {
                Start($"\"{entry.Path}\"");
                return;
            }

            if (File.Exists(entry.Path))
            {
                // /select 让资源管理器打开父目录并把光标停在这个文件上
                Start($"/select,\"{entry.Path}\"");
                return;
            }

            // 文件没了，退而求其次打开它原来所在的目录
            var folder = Path.GetDirectoryName(entry.Path);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Start($"\"{folder}\"");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            // 打不开就算了，不打扰用户
        }
    }

    /// <summary>
    /// 用资源管理器打开一个文件夹。设置窗口的"打开数据文件夹"用它。
    /// 目录不存在就直接放弃——不弹错误框，也不现场创建一个空目录。
    /// </summary>
    public static void OpenFolder(string folderPath)
    {
        try
        {
            if (Directory.Exists(folderPath))
            {
                Start($"\"{folderPath}\"");
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            // 打不开就算了，不打扰用户
        }
    }

    /// <summary>
    /// 用默认浏览器打开网址。
    ///
    /// 这里**必须**用 UseShellExecute = true，让系统去决定用哪个浏览器；
    /// 直接 Process.Start("chrome.exe") 之类的做法会在没装那个浏览器的机器上失败。
    /// </summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(UrlDetector.Normalize(url)) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            // 没有默认浏览器之类的情况，静默跳过
        }
    }

    private static void Start(string arguments) =>
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
}
