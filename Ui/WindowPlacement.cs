using System.Windows;

namespace ClipboardHistory.Ui;

/// <summary>窗口定位的小工具。</summary>
public static class WindowPlacement
{
    /// <summary>
    /// 把窗口挪回屏幕工作区里（工作区已经扣掉了任务栏）。
    ///
    /// 为什么需要：`WindowStartupLocation=CenterOwner` 在主人窗口贴着屏幕边缘时，
    /// 会把子窗口顶出去一大截——用户只看见屏幕边上露一条，剩下的够不着，
    /// 也没法拖回来（对话框往往不能缩放）。显示之后夹一次最省事。
    /// </summary>
    public static void ClampToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;

        if (window.Left < area.Left)
        {
            window.Left = area.Left;
        }

        if (window.Top < area.Top)
        {
            window.Top = area.Top;
        }

        if (window.Left + window.ActualWidth > area.Right)
        {
            window.Left = area.Right - window.ActualWidth;
        }

        if (window.Top + window.ActualHeight > area.Bottom)
        {
            window.Top = area.Bottom - window.ActualHeight;
        }
    }
}
