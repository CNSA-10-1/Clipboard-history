using System.Windows.Interop;
using ClipboardHistory.Interop;

namespace ClipboardHistory.Services;

/// <summary>
/// 监听剪贴板变化。
///
/// 做法：建一个隐藏的"消息窗口"，用 AddClipboardFormatListener 注册，
/// 系统每次广播 WM_CLIPBOARDUPDATE 时就触发 <see cref="ClipboardChanged"/>。
///
/// 用消息窗口而不是主窗口，是为了**主窗口关掉之后照样能记录**——
/// 托盘的常驻工具必须做到这一点。
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly HwndSource _messageWindow;

    /// <summary>我们自己写进剪贴板时记下的序号，收到相同序号就跳过。</summary>
    private uint _ignoredSequence;

    private bool _disposed;

    /// <summary>剪贴板内容变了（且不是我们自己写的）。回调在 UI 线程上执行。</summary>
    public event EventHandler? ClipboardChanged;

    public ClipboardMonitor()
    {
        var parameters = new HwndSourceParameters("ClipboardHistory.MessageWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
        };

        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);

        if (!NativeMethods.AddClipboardFormatListener(_messageWindow.Handle))
        {
            throw new InvalidOperationException(
                "注册剪贴板监听失败。这通常意味着系统资源不足，或另一个剪贴板工具抢占了监听。");
        }
    }

    /// <summary>
    /// 告知监听器："接下来这次变化是我自己写剪贴板造成的，别记。"
    ///
    /// **必须在写完剪贴板之后调用**——它记的是当前序号，也就是我们刚写进去那一版。
    /// 写之前调用记的是旧序号，防不住。
    /// </summary>
    public void IgnoreCurrentContent()
    {
        _ignoredSequence = NativeMethods.GetClipboardSequenceNumber();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_CLIPBOARDUPDATE)
        {
            return IntPtr.Zero;
        }

        // 点卡片时我们会把内容写回剪贴板，那也会触发一次变化。
        // 不拦掉的话，历史里会自己刷自己，列表一直往上冒重复项。
        if (NativeMethods.GetClipboardSequenceNumber() != _ignoredSequence)
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 先注销监听再销毁窗口。反过来的话，窗口没了消息还在往它发。
        if (_messageWindow.Handle != IntPtr.Zero)
        {
            NativeMethods.RemoveClipboardFormatListener(_messageWindow.Handle);
        }

        _messageWindow.RemoveHook(WndProc);
        _messageWindow.Dispose();
    }
}
