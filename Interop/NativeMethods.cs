using System.Runtime.InteropServices;

namespace ClipboardHistory.Interop;

/// <summary>用到的 Win32 API。只声明实际用得上的，不做大而全的封装。</summary>
internal static class NativeMethods
{
    /// <summary>剪贴板内容发生变化时，系统会广播这个消息。</summary>
    public const int WM_CLIPBOARDUPDATE = 0x031D;

    /// <summary>
    /// 特殊父窗口句柄：把窗口设成"消息窗口"——不显示、不进任务栏、不出现在 Alt+Tab，
    /// 只用来收消息。剪贴板监听挂在它上面，所以主窗口关掉了照样能收。
    /// </summary>
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    /// <summary>开始监听剪贴板变化。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    /// <summary>停止监听。程序退出前一定要调，否则窗口销毁后消息还会往那儿发。</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>
    /// 剪贴板内容每变一次，这个序号就 +1。
    /// 用来识别"这次变化是我们自己写进去的"，避免把刚复制出去的内容又记一遍。
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetClipboardSequenceNumber();
}
