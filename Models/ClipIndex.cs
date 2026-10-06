namespace ClipboardHistory.Models;

/// <summary>
/// index.json 的顶层结构。
///
/// 为什么要包一层 version 而不是直接存数组：以后改数据结构时，能认出"这是老格式"，
/// 备份一份再重建，而不是把用户几年的历史直接读崩。
/// </summary>
public sealed class ClipIndex
{
    /// <summary>当前代码认识的格式版本。</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public List<ClipItem> Items { get; set; } = [];
}
