using System.Drawing;
using System.Windows.Forms;

namespace ClipboardHistory.Services;

/// <summary>
/// 右下角的托盘图标。
///
/// 这个文件是**唯一**允许用 WinForms 的地方（.NET 自带的 NotifyIcon，不引第三方包）。
/// csproj 里已经把 WinForms 的全局 using 关掉了，所以这里要显式 using；
/// 反过来说，正因为关掉了，这里用 WinForms 类型也不会和 WPF 的同名类型打架。
///
/// 界面动作通过回调传进来，不在这个文件里引用 WPF 类型——免得两种类型混在一起。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;

    private bool _disposed;

    /// <param name="icon">
    /// 恰好 16×16 的那个图标（见 Ui/AppIcon）。传 null 就退回系统默认图标——
    /// 图标读不出来是小事，托盘里没图标用户就完全找不到这个程序了，不能因此起不来。
    /// </param>
    public TrayService(Icon? icon, Action onOpen, Action onSettings, Action onExit)
    {
        _icon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Text = "剪贴板历史",
            Visible = true,
            ContextMenuStrip = BuildMenu(onOpen, onSettings, onExit),
        };

        // 左键点一下就把窗口叫出来。剪贴板工具最常见的用法就是"点一下、粘一下、关掉"。
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                onOpen();
            }
        };
    }

    private static ContextMenuStrip BuildMenu(Action onOpen, Action onSettings, Action onExit)
    {
        var menu = new ContextMenuStrip();

        // 加粗的那一项是默认动作——双击托盘图标时 Windows 就执行它
        var open = menu.Items.Add("打开剪贴板历史", null, (_, _) => onOpen());
        open.Font = new Font(open.Font, FontStyle.Bold);

        menu.Items.Add("设置…", null, (_, _) => onSettings());

        menu.Items.Add(new ToolStripSeparator());

        // 只能从这里真正退出。窗口上的 × 只是把它藏起来（见 MainWindow.OnClosing）
        menu.Items.Add("退出", null, (_, _) => onExit());

        return menu;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 顺序不能反：先 Visible = false 把图标从托盘撤掉，
        // 再 Dispose。直接 Dispose 的话图标可能残留在托盘上，
        // 鼠标划过去才消失，看着像程序崩了。
        _icon.Visible = false;
        _icon.Dispose();
    }
}
