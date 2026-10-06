using System.Security.Cryptography;
using System.Text;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>
/// 内容指纹。两条记录指纹相同就认为是"同样的内容"，合并成一条。
/// 算法见 docs/04-数据规范.md。
/// </summary>
internal static class ClipHash
{
    public static string ForText(string text) =>
        FromBytes(Encoding.UTF8.GetBytes(text.Trim()));

    public static string ForImageBytes(byte[] pngBytes) =>
        FromBytes(pngBytes);

    /// <summary>文件名用换行拼起来再哈希。用 \n 而不是别的字符，避免文件名里含分隔符时算重。</summary>
    public static string ForFiles(IEnumerable<FileEntry> files) =>
        FromBytes(Encoding.UTF8.GetBytes(
            string.Join('\n', files.Select(f => f.Path.ToLowerInvariant()))));

    private static string FromBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
