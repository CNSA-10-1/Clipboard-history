using System.IO;

namespace ClipboardHistory.Services;

/// <summary>
/// 数据文件都放在哪。
///
/// 用 %AppData%（Roaming）而不是 %LocalAppData%：数据量小、都是用户自己的东西，
/// 跟着漫游配置文件走更符合预期。用 "ClipboardHistory" 而不是中文名，
/// 是因为这个路径会出现在日志、异常信息和别的程序的调用里，纯 ASCII 少踩坑。
/// 用户在设置里点「打开数据文件夹」就能直接看到它。
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipboardHistory");

    public static string IndexFile => Path.Combine(Root, "index.json");

    /// <summary>上一次的索引。原子写入时由 File.Replace 自动产生。</summary>
    public static string IndexBackupFile => Path.Combine(Root, "index.json.bak");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>图片原图和缩略图都放这里。</summary>
    public static string ImagesDir => Path.Combine(Root, "images");

    /// <summary>取图片的完整路径。</summary>
    public static string ImagePath(string fileName) => Path.Combine(ImagesDir, fileName);

    /// <summary>确认目录存在。启动时调一次即可。</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ImagesDir);
    }
}
