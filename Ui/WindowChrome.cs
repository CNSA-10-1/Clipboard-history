using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ClipboardHistory.Services;

namespace ClipboardHistory.Ui;

/// <summary>
/// 窗口边框的深浅色。
///
/// 为什么要单独做：Windows 的标题栏**不会**跟着程序内部的主题走。
/// 只把窗口内容换成深色、标题栏还留着一条白，看着像没做完。
/// Win10 提供了 DwmSetWindowAttribute 让程序主动声明"我是深色的"。
/// </summary>
public static class WindowChrome
{
    /// <summary>1903 及以上用的属性号。</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>更早的 Win10 版本用的是 19（当时还没正式公开）。</summary>
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// 把窗口标题栏切成深色或浅色。
    ///
    /// 句柄还没创建时直接返回——窗口在构造阶段还没有 HWND，
    /// 这种情况下等 SourceInitialized 再调一次就行。
    /// </summary>
    public static void ApplyTitleBarTheme(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var value = dark ? 1 : 0;

            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            {
                // 老版本 Windows 不认 20 这个属性号，退回 19 再试一次。
                // 两次都失败也不管了——标题栏颜色不对只是不好看，不影响用。
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
            // 极老的系统没有 dwmapi.dll，忽略
        }
        catch (EntryPointNotFoundException)
        {
            // 同上
        }
    }

    /// <summary>把当前主题套到所有已经存在的窗口上。</summary>
    public static void ApplyToAllWindows(bool dark)
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBarTheme(window, dark);
        }
    }

    /// <summary>
    /// 给窗口挂上"句柄一就绪就按当前主题刷标题栏"的钩子。
    ///
    /// 每扇窗都得挂：构造窗口的时候 HWND 还没建出来，那时候刷不了；
    /// 而切主题时新开的窗口又还没挂上 ApplyToAllWindows 那趟车。
    /// </summary>
    public static void FollowTheme(Window window) =>
        window.SourceInitialized += (_, _) => ApplyTitleBarTheme(window, ThemeService.IsDark);
}
