using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ClipboardHistory.Ui;

/// <summary>
/// 让内层滚动区滚到头之后，滚轮接着滚外层。
///
/// 场景：卡片展开后内部可以滚动（最多 420px），但列表本身也在滚动。
/// WPF 默认是内层滚到头就"吃掉"滚轮事件，用户会觉得卡住了——鼠标停在卡片上滚不动列表。
/// 这里在滚到边界时把事件转发给外层的滚动区。
/// </summary>
public static class ScrollChainBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollChainBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>
    /// 这个滚动区"现在能不能自己滚"。默认 true。
    ///
    /// 卡片收起时绑成 false：那时内容区虽然看不见滚动条，其实是能滚的
    /// （外面用的是 ScrollViewer + Hidden，只为让内容不被压缩高度）。
    /// 不拦住的话，用户滚滚轮就能不点"展开"偷看后面的内容，
    /// 底部的渐隐也就白做了。
    /// </summary>
    public static readonly DependencyProperty CanScrollProperty =
        DependencyProperty.RegisterAttached(
            "CanScroll",
            typeof(bool),
            typeof(ScrollChainBehavior),
            new PropertyMetadata(true));

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetCanScroll(DependencyObject element, bool value) =>
        element.SetValue(CanScrollProperty, value);

    public static bool GetCanScroll(DependencyObject element) =>
        (bool)element.GetValue(CanScrollProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer)
        {
            return;
        }

        viewer.PreviewMouseWheel -= OnPreviewMouseWheel;

        if (e.NewValue is true)
        {
            viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        if (GetCanScroll(viewer))
        {
            var scrollingUp = e.Delta > 0;
            var atTop = viewer.VerticalOffset <= 0.5;
            var atBottom = viewer.VerticalOffset >= viewer.ScrollableHeight - 0.5;

            // 自己还能滚，就让 ScrollViewer 自己处理，别抢
            var canScrollItself = viewer.ScrollableHeight > 0
                                  && ((scrollingUp && !atTop) || (!scrollingUp && !atBottom));

            if (canScrollItself)
            {
                return;
            }
        }

        var outer = FindOuterScrollViewer(viewer);
        if (outer is null)
        {
            return;
        }

        // 自己不滚（或已经到头了），把手上的滚轮转交给外层
        e.Handled = true;
        outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = viewer,
        });
    }

    private static ScrollViewer? FindOuterScrollViewer(DependencyObject start)
    {
        var current = VisualTreeHelper.GetParent(start);

        while (current is not null)
        {
            if (current is ScrollViewer viewer)
            {
                return viewer;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
