using System.IO;
using System.Text;

namespace ClipboardHistory.Services;

/// <summary>
/// 原子写入文本文件：先写临时文件，再用 <see cref="File.Replace(string,string,string)"/> 替换。
///
/// 为什么不能直接覆盖写：写到一半崩溃/断电，会留下一个半截的文件，
/// 原来那份完好的数据也没了。Replace 是原子的——要么旧的完好，要么新的完好，不会两头落空。
/// 顺带还把上一版挪到 `.bak`，多一层兜底。
/// </summary>
internal static class AtomicFile
{
    public static void Write(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents, Encoding.UTF8);

        if (File.Exists(path))
        {
            File.Replace(temp, path, path + ".bak");
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
