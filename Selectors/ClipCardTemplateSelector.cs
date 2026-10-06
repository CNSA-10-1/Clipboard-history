using System.Windows;
using System.Windows.Controls;
using ClipboardHistory.Models;

namespace ClipboardHistory.Selectors;

/// <summary>
/// 按内容类型挑卡片模板：文字 / 图片 / 文件各一套。
/// 三套模板在 MainWindow.xaml 里定义，通过属性注入进来。
/// </summary>
public sealed class ClipCardTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public DataTemplate? ImageTemplate { get; set; }

    public DataTemplate? FilesTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ClipItem clip)
        {
            return base.SelectTemplate(item, container);
        }

        return clip.Kind switch
        {
            ClipKind.Image => ImageTemplate,
            ClipKind.Files => FilesTemplate,
            _ => TextTemplate,
        };
    }
}
