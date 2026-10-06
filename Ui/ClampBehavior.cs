using System.Windows;

namespace ClipboardHistory.Ui;

/// <summary>
/// 判断内容有没有超出卡片允许显示的高度，结果写到 <see cref="IsOverflowingProperty"/> 上，
/// 界面据此决定要不要显示底部渐隐和"展开"按钮。文字卡片和图片卡片共用这一套。
///
/// <b>使用前提</b>：挂载的元素外面必须套一个 <c>ScrollViewer</c>，而且它的
/// <c>VerticalScrollBarVisibility</c> 得是 <c>Hidden</c> 或 <c>Auto</c>。
/// 因为 ScrollViewer 会不限高度地测量内容，元素的 <c>ActualHeight</c> 才是"内容本来的高度"；
/// 要是用 <c>Disabled</c>，内容会被压到可视高度，量出来永远是那个被截断的值，
/// 判断就永远不成立。
///
/// 用 Hidden 的副作用是内容其实能滚——所以还要配合
/// <see cref="ScrollChainBehavior.CanScroll"/>，在收起状态下把滚轮拦下来。
/// </summary>
public static class ClampBehavior
{
    /// <summary>允许显示的最大高度（像素）。设为 NaN 表示不启用。</summary>
    public static readonly DependencyProperty ClampHeightProperty =
        DependencyProperty.RegisterAttached(
            "ClampHeight",
            typeof(double),
            typeof(ClampBehavior),
            new PropertyMetadata(double.NaN, OnClampHeightChanged));

    /// <summary>只读结果：内容是否超出了允许的高度。</summary>
    public static readonly DependencyProperty IsOverflowingProperty =
        DependencyProperty.RegisterAttached(
            "IsOverflowing",
            typeof(bool),
            typeof(ClampBehavior),
            new PropertyMetadata(false));

    public static void SetClampHeight(DependencyObject element, double value) =>
        element.SetValue(ClampHeightProperty, value);

    public static double GetClampHeight(DependencyObject element) =>
        (double)element.GetValue(ClampHeightProperty);

    public static void SetIsOverflowing(DependencyObject element, bool value) =>
        element.SetValue(IsOverflowingProperty, value);

    public static bool GetIsOverflowing(DependencyObject element) =>
        (bool)element.GetValue(IsOverflowingProperty);

    private static void OnClampHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.SizeChanged -= OnSizeChanged;

        if (e.NewValue is double height && !double.IsNaN(height))
        {
            element.SizeChanged += OnSizeChanged;
            Recalculate(element);
        }
        else
        {
            SetIsOverflowing(element, false);
        }
    }

    /// <summary>
    /// 尺寸变了就重算。
    ///
    /// 有个容易绕进去的点：文字从 4 行变 8 行、超出更多时，被裁剪后的高度一直是 54 没变，
    /// 不会触发这个事件——但那种情况"溢出"仍然是 true，结论不受影响，不需要重算。
    /// </summary>
    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            Recalculate(element);
        }
    }

    private static void Recalculate(FrameworkElement element)
    {
        var clampHeight = GetClampHeight(element);
        if (double.IsNaN(clampHeight))
        {
            return;
        }

        // 加 0.5 的容差：字体和布局的舍入误差可能让"刚好三行"量出 54.0001，
        // 那不该被判成溢出，否则会出现"展开按钮点了没任何变化"的诡异情况。
        SetIsOverflowing(element, element.ActualHeight > clampHeight + 0.5);
    }
}
