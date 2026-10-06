using System.Windows;
using System.Windows.Input;
using ClipboardHistory.Ui;

namespace ClipboardHistory;

/// <summary>
/// 应用自己的提示 / 确认对话框。
///
/// 不用系统的 <c>MessageBox</c>，原因见 MessageWindow.xaml 里的注释：
/// 位置不可控（实测会跑到屏幕外且拖不回来）、深色主题下仍是亮色、按钮文案改不了。
/// </summary>
public partial class MessageWindow : Window
{
    private MessageWindow()
    {
        InitializeComponent();

        // 标题栏是系统画的，得单独跟着主题走
        WindowChrome.FollowTheme(this);
    }

    /// <summary>要用户确认才继续的操作（比如清空历史）。返回 true 表示用户点了确认。</summary>
    public static bool Confirm(
        Window? owner,
        string caption,
        string message,
        string confirmText = "确定",
        bool danger = false)
    {
        var window = new MessageWindow
        {
            Title = caption,
        };

        window.MessageText.Text = message;
        window.ConfirmButton.Content = confirmText;
        window.ConfirmButton.Style = (Style)window.FindResource(
            danger ? "Style.Button.Danger" : "Style.Button.Primary");

        return window.ShowDialog(owner);
    }

    /// <summary>只是告知一下（比如"已清空 6 条"），只有一个确定按钮。</summary>
    public static void Info(Window? owner, string caption, string message)
    {
        var window = new MessageWindow
        {
            Title = caption,
        };

        window.MessageText.Text = message;
        window.CancelButton.Visibility = Visibility.Collapsed;
        window.ConfirmButton.Content = "确定";
        window.ConfirmButton.Style = (Style)window.FindResource("Style.Button.Primary");

        window.ShowDialog(owner);
    }

    /// <summary>
    /// 显示并返回结果。顺带把窗口夹回屏幕内。
    ///
    /// ShowDialog 之后才夹：窗口没显示出来之前 ActualWidth/ActualHeight 还是 0，算不了位置。
    /// </summary>
    private bool ShowDialog(Window? owner)
    {
        if (owner is { IsLoaded: true })
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // 先夹一次再显示。ShowDialog 会阻塞，没法在它之后夹；
        // 而 CenterOwner 的位置在 Show 之前是算不出来的，所以用 Loaded 钩子。
        Loaded += (_, _) => WindowPlacement.ClampToWorkArea(this);

        var result = ShowDialog() == true;

        // 关掉对话框之后要把焦点还给调用它的窗口，不然焦点会掉到桌面上
        owner?.Activate();

        return result;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Esc = 取消。这是对话框的通用习惯，用户按 Esc 时意思是"算了"。</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }
}
