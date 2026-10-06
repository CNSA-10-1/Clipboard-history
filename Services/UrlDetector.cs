using System.Text.RegularExpressions;

namespace ClipboardHistory.Services;

/// <summary>
/// 从文字里找出网址，供界面把那一截标蓝、做成可点的。
///
/// **判定故意保守**：只认带协议的（http/https）和 `www.` 开头的。
/// 认裸域名（`example.com`）看着聪明，实际会把 `你好.世界`、`1.5.3`、文件名 `报告.docx`
/// 全标成链接——标错了比漏标烦人得多，用户会不敢点。
/// </summary>
internal static partial class UrlDetector
{
    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""'（）【】《》]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    /// <summary>网址末尾常跟着句号、逗号、右括号，那是句子的一部分，不属于网址。</summary>
    private const string TrailingPunctuation = ".,;:!?)]}>。，、；：！？）］｝》”’";

    /// <summary>把一段文字切成"普通文字"和"网址"交替的片段。</summary>
    public static IEnumerable<(string Text, bool IsUrl)> Split(string? source)
    {
        if (string.IsNullOrEmpty(source))
        {
            yield break;
        }

        var cursor = 0;

        foreach (Match match in UrlPattern().Matches(source))
        {
            var url = TrimTrailing(match.Value);
            if (url.Length == 0)
            {
                continue;
            }

            if (match.Index > cursor)
            {
                yield return (source[cursor..match.Index], false);
            }

            yield return (url, true);
            cursor = match.Index + url.Length;
        }

        if (cursor < source.Length)
        {
            yield return (source[cursor..], false);
        }
    }

    /// <summary>把末尾的标点去掉，剩下的才是网址本体。全被剃光就返回空串（这段不算网址）。</summary>
    private static string TrimTrailing(string url) =>
        url.TrimEnd(TrailingPunctuation.ToCharArray());

    /// <summary>补上协议头。用户看到的是 `www.xxx.com`，但浏览器要 `http://www.xxx.com`。</summary>
    public static string Normalize(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? url
            : "http://" + url;
}
