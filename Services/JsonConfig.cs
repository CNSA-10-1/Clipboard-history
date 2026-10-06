using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipboardHistory.Services;

/// <summary>index.json 和 settings.json 共用的序列化配置。</summary>
public static class JsonConfig
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            // 字段名用小驼峰（id / createdAt），不用 C# 的 PascalCase。
            // JSON 的惯例就是小驼峰，而且别的地方（文档、手写排查）都按这个写。
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            // 大小写不敏感地读。
            // 这条不是"顺手加的"，是防丢数据的：如果哪天改了命名策略（或者用户手改过文件），
            // 严格匹配会让整个 index.json 解析成 0 条——界面上看着像"历史没了"，
            // 而下一次复制就会拿这个空列表覆盖存档，**把用户攒的历史真的删光**。
            PropertyNameCaseInsensitive = true,

            // 缩进过的 JSON 人能直接看懂——出问题时用户自己打开 index.json 就能看出个大概，
            // 或者贴给开发者。这点体积换来的可排查性很值。
            WriteIndented = true,

            // 读的时候允许注释和尾逗号：用户手改过文件的话不至于直接读崩
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // 枚举存成字符串（"Text" / "Image" / "Files"）而不是 0/1/2，
        // 同样是给人看的——数字版本对不上号就没法排查了。
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
