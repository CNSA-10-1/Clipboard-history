using ClipboardHistory.Models;

namespace ClipboardHistory.Ui;

/// <summary>
/// 图标字形。
///
/// 用 Windows 10 自带的「Segoe MDL2 Assets」图标字体，而不是 emoji：
/// 一是更像系统原生（符合"Win10 设计风格"的要求），二是 emoji 在不同系统上渲染差别很大。
///
/// 字形码直接写十六进制，不写字面字符——私有区码位在编辑器里是看不见的，
/// 出问题没法排查。码位可以从微软的「Segoe MDL2 Assets 图标表」查到。
/// </summary>
public static class Icons
{
    /// <summary>图标字体名。任何显示图标的地方都要用它，否则会显示成方框。</summary>
    public const string FontFamily = "Segoe MDL2 Assets";

    /// <summary>把码位转成字符串。私有区字符不可见，所以源码里一律用码位表示。</summary>
    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    // ── 界面元素 ──
    public static readonly string Search   = Glyph(0xE721);
    public static readonly string Settings = Glyph(0xE713);
    public static readonly string Clear    = Glyph(0xE894);

    // ── 卡片操作 ──
    public static readonly string Pin    = Glyph(0xE718);
    public static readonly string Unpin  = Glyph(0xE77A);
    public static readonly string Delete = Glyph(0xE74D);

    // ── 展开 / 收起 ──
    public static readonly string ChevronDown = Glyph(0xE70D);
    public static readonly string ChevronUp   = Glyph(0xE70E);

    // ── 内容类型 ──
    public static readonly string Photo    = Glyph(0xE91B);
    public static readonly string Folder   = Glyph(0xE8B7);
    public static readonly string Document = Glyph(0xE8A5);
    public static readonly string TextDoc  = Glyph(0xE7C3);
    public static readonly string Warning  = Glyph(0xE7BA);

    /// <summary>取某条记录左侧要显示的类型图标。</summary>
    public static string ForKind(ClipKind kind) => kind switch
    {
        ClipKind.Image => Photo,
        ClipKind.Files => Document,
        _ => TextDoc,
    };

    /// <summary>取文件卡片里某个条目的图标——文件夹 / 图片 / 普通文件。</summary>
    public static string ForEntry(FileEntry entry) => entry switch
    {
        { IsDirectory: true } => Folder,
        { IsImage: true } => Photo,
        _ => Document,
    };
}
