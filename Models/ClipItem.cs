using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClipboardHistory.Models;

/// <summary>一条历史记录的内容类型。</summary>
public enum ClipKind
{
    /// <summary>纯文字。</summary>
    Text,

    /// <summary>图片。</summary>
    Image,

    /// <summary>文件 / 文件夹（可混合）。</summary>
    Files,
}

/// <summary>
/// 文件卡片里的一个条目。文件和文件夹都存在这里，靠 <see cref="IsDirectory"/> 区分。
/// </summary>
public sealed class FileEntry : INotifyPropertyChanged
{
    public string Path { get; set; } = "";

    /// <summary>
    /// 是真文件夹还是文件。
    /// 必须在写入时确定并保存——如果筛选时用 File.Exists 现算，文件被移动后就判断不出来，
    /// 卡片会从「文件夹」筛选里莫名消失。
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// 缩略图文件名（相对 images 目录）。只有图片文件才有，其它文件是空串。
    ///
    /// 存一份独立的小图而不是渲染时现读原文件，有两个原因：
    ///   1. 相机原图动辄几千万像素，每次滚动都重新解码会把列表卡死
    ///   2. 原文件被移走或删掉之后，预览还能留着——这条记录本来就该记着"当时复制的是什么"
    ///
    /// 会发变更通知：这个功能上线**之前**存下来的老记录是没有缩略图的，
    /// 得等卡片显示出来之后在后台补生成。补完不发通知的话，界面会一直停在图标上，
    /// 明明图已经生成好了却看不见。
    /// </summary>
    public string ThumbFile
    {
        get => _thumbFile;
        set
        {
            if (_thumbFile == value)
            {
                return;
            }

            _thumbFile = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbFile)));
        }
    }

    private string _thumbFile = "";

    /// <summary>
    /// 这是不是一张图片文件。
    ///
    /// 只看扩展名，不碰磁盘：路径是复制那一刻存下来的，扩展名不会变；
    /// 而用 File.Exists 现算的话，文件被移动后判断结果会翻来覆去，
    /// 卡片在「图片」筛选里忽有忽无（跟 <see cref="IsDirectory"/> 同一个理由）。
    /// </summary>
    [JsonIgnore]
    public bool IsImage => IsImagePath(Path);

    /// <summary>算得上的图片扩展名。够用就行，不做完整 MIME 表。</summary>
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp",
        ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif", ".avif",
    };

    /// <summary>按扩展名判断一个路径是不是图片。</summary>
    public static bool IsImagePath(string path)
    {
        // 必须写全 System.IO.Path：类里有个叫 Path 的属性，直接写 Path 会解析到它身上报错
        var ext = System.IO.Path.GetExtension(path);
        return ext.Length > 0 && ImageExtensions.Contains(ext);
    }

    /// <summary>
    /// 大小（字节）。
    /// 文件：写入时读一次 FileInfo.Length。之后文件变大变小都不再更新——这是历史记录，记的是当时的样子。
    /// 文件夹：写入时不统计（递归遍历可能几万个文件，会卡住剪贴板监听），存 <see cref="UnknownSize"/>，
    ///         用户展开详情时才在后台算。
    ///
    /// 这个属性会发变更通知：文件夹的大小是**展开之后**才异步算出来的，
    /// 不发通知的话界面永远停在"…"——内层列表绑的是 Files 数组本身，
    /// 刷新外层列表对它没有任何影响。
    /// </summary>
    public long SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (_sizeBytes == value)
            {
                return;
            }

            _sizeBytes = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeBytes)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        }
    }

    private long _sizeBytes;

    /// <summary>表示"还没统计过"的大小值，只用于文件夹。</summary>
    public const long UnknownSize = -1;

    /// <summary>表示"统计过但太大了"——遍历超过上限被主动放弃，界面显示"较大"。</summary>
    public const long TooLargeToMeasure = -2;

    public event PropertyChangedEventHandler? PropertyChanged;

    [JsonIgnore]
    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } name ? name : Path;

    /// <summary>路径现在还在不在。用来把失效的文件显示成灰色。</summary>
    [JsonIgnore]
    public bool Exists => IsDirectory ? System.IO.Directory.Exists(Path) : System.IO.File.Exists(Path);

    /// <summary>
    /// 界面上显示的大小文案。
    /// 三种特殊情况要分开说，都笼统显示 "0 B" 就没法排查问题了：
    ///   还没统计（文件夹，展开时才去算） → "…"
    ///   太大放弃了（遍历超过 5000 个文件） → "较大"
    ///   路径已经失效                     → "—"
    /// </summary>
    [JsonIgnore]
    public string SizeText => _sizeBytes switch
    {
        UnknownSize => "…",
        TooLargeToMeasure => "较大",
        _ => Exists ? FormatSize(_sizeBytes) : "—",
    };

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };
}

/// <summary>
/// 图片卡片里的一张图。
/// 一条记录可以有多张（比如在网页上连续选中多张图复制），所以是数组不是单个字段。
/// </summary>
public sealed class ImageEntry
{
    /// <summary>原图文件名（相对 images 目录）。只在真正写回剪贴板时才读。</summary>
    public string ImageFile { get; set; } = "";

    /// <summary>缩略图文件名（相对 images 目录）。列表里显示这个，比原图快得多。</summary>
    public string ThumbFile { get; set; } = "";

    public int Width { get; set; }

    public int Height { get; set; }

    public long SizeBytes { get; set; }

    [JsonIgnore]
    public string SizeText => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "图片";
}

/// <summary>一条剪贴板历史记录。字段定义见 docs/04-数据规范.md。</summary>
public sealed class ClipItem
{
    public string Id { get; set; } = "";

    public ClipKind Kind { get; set; }

    /// <summary>文字内容。文件类型下也会填（拼起来的文件名），这样搜索能搜到文件名。</summary>
    public string Text { get; set; } = "";

    public FileEntry[] Files { get; set; } = [];

    public ImageEntry[] Images { get; set; } = [];

    /// <summary>内容指纹，去重用。</summary>
    public string Hash { get; set; } = "";

    /// <summary>最近一次复制时间。用 DateTimeOffset 存，避免换时区后时间错乱。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public bool Pinned { get; set; }

    /// <summary>占用空间（字节），容量淘汰用。</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// 卡片当前是不是展开状态。
    ///
    /// 这是界面状态，不写进 index.json。之所以放在模型上而不是让控件自己记着：
    /// 列表刷新时（复制了新内容、算完文件夹大小）条目容器会被回收重建，
    /// 控件自己的状态就丢了——正展开着的卡片会莫名其妙缩回去。
    /// </summary>
    [JsonIgnore]
    public bool IsExpanded { get; set; }

    // ── 以下是给界面用的计算属性，不写进 index.json ──

    /// <summary>文件卡片左侧图标取第一个条目的类型（文件夹 / 文件）。</summary>
    [JsonIgnore]
    public FileEntry? FirstEntry => Files.Length > 0 ? Files[0] : null;

    /// <summary>图片卡片取第一张。</summary>
    [JsonIgnore]
    public ImageEntry? FirstImage => Images.Length > 0 ? Images[0] : null;

    /// <summary>文件卡片的摘要文案，例如 "报告.docx 等 3 个文件"。</summary>
    [JsonIgnore]
    public string FileSummary
    {
        get
        {
            if (Files.Length == 0)
            {
                return "";
            }

            var firstName = Files[0].Name;
            if (Files.Length == 1)
            {
                return firstName;
            }

            // 全是文件夹 / 全是文件 / 混合，三种说法不一样，否则读起来别扭
            var folders = Files.Count(f => f.IsDirectory);
            return folders switch
            {
                0 => $"{firstName} 等 {Files.Length} 个文件",
                var n when n == Files.Length => $"{firstName} 等 {Files.Length} 个文件夹",
                _ => $"{firstName} 等 {Files.Length} 项",
            };
        }
    }

    /// <summary>图片卡片的摘要文案。</summary>
    [JsonIgnore]
    public string ImageSummary => Images.Length switch
    {
        0 => "",
        1 => Images[0].SizeText,
        var n => $"{Images[0].SizeText} 等 {n} 张图片",
    };

    /// <summary>内容是否超过一个条目——超过就要在卡片底部做渐隐、并显示展开按钮。</summary>
    [JsonIgnore]
    public bool HasMultipleEntries => Kind switch
    {
        ClipKind.Files => Files.Length > 1,
        ClipKind.Image => Images.Length > 1,
        _ => false,
    };

    /// <summary>卡片归到哪个分组——置顶的单独一组。</summary>
    [JsonIgnore]
    public string GroupName => Pinned ? "已置顶" : "最近";

    /// <summary>
    /// 这条记录里是否含"普通文件"（"文件"筛选用）。
    ///
    /// **图片文件不算**：从资源管理器复制一张 .jpg 走的是"文件列表"格式，
    /// 但用户的认知里它就是一张图片，扔进「文件」筛选会让那里混进一大堆照片，
    /// 而「图片」筛选里反倒一张都没有。"2 张图 + 1 个 pdf"这种混合复制
    /// 在「图片」和「文件」下都会出现，不至于把它藏起来。
    /// </summary>
    [JsonIgnore]
    public bool ContainsFile => Files.Any(f => !f.IsDirectory && !f.IsImage);

    /// <summary>
    /// 这条记录要不要出现在「图片」筛选下。
    /// 两种来源：真的从剪贴板截下来的位图，以及复制过来的图片文件。
    /// </summary>
    [JsonIgnore]
    public bool ContainsImage => Kind == ClipKind.Image || Files.Any(f => f.IsImage);

    /// <summary>这条记录里是否含文件夹（"文件夹"筛选用）。混合内容的卡片两边都会出现。</summary>
    [JsonIgnore]
    public bool ContainsFolder => Files.Any(f => f.IsDirectory);
}
