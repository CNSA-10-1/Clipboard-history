using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ClipboardHistory.Services;

namespace ClipboardHistory.Ui;

/// <summary>
/// 会把文字里的网址渲染成链接的 TextBlock。
///
/// 为什么不用绑定的方式：`TextBlock.Inlines` 没法直接绑定，
/// 而网址要和普通文字混排（"看这个 https://xxx 很有意思" 里只有中间那截是链接），
/// 所以得在代码里把文字切段、逐段构造 Run / Hyperlink。
///
/// **链接只吃掉落在自己身上的点击**，卡片其余地方的点击照样往上冒泡——
/// 所以"点网址开浏览器、点卡片其它地方复制"两件事能共存。
/// </summary>
public sealed class LinkifiedTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty =
        DependencyProperty.Register(
            nameof(SourceText),
            typeof(string),
            typeof(LinkifiedTextBlock),
            new PropertyMetadata(string.Empty, OnSourceTextChanged));

    public string SourceText
    {
        get => (string)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    private static void OnSourceTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LinkifiedTextBlock block)
        {
            block.Rebuild();
        }
    }

    private void Rebuild()
    {
        Inlines.Clear();

        foreach (var (text, isUrl) in UrlDetector.Split(SourceText))
        {
            if (!isUrl)
            {
                Inlines.Add(new Run(text));
                continue;
            }

            var link = new Hyperlink(new Run(text))
            {
                // 不用 NavigateUri：那会让 WPF 去找导航容器，我们只想自己处理点击
                Cursor = System.Windows.Input.Cursors.Hand,
                TextDecorations = System.Windows.TextDecorations.Underline,
            };

            // 用资源引用而不是直接给颜色，这样切深浅主题时会跟着变
            link.SetResourceReference(TextElement.ForegroundProperty, "Brush.Accent");

            var target = text;
            link.Click += (_, _) => ShellService.OpenUrl(target);

            Inlines.Add(link);
        }
    }
}
