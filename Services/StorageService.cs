using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using ClipboardHistory.Models;

namespace ClipboardHistory.Services;

/// <summary>
/// 历史记录的读写与增删。负责 index.json 的原子写入、按内容指纹去重。
/// 过期清理和容量淘汰分别在 S17 / S18 加进来。
/// </summary>
public sealed class StorageService
{
    /// <summary>写文件时的互斥锁。剪贴板事件和界面操作可能同时想写。</summary>
    private readonly Lock _fileLock = new();

    private readonly ObservableCollection<ClipItem> _items = [];

    /// <summary>当前所有记录。用 ObservableCollection 是为了界面能自动跟着增删刷新。</summary>
    public ObservableCollection<ClipItem> Items => _items;

    /// <summary>
    /// 列表需要重新排序时触发。
    ///
    /// 为什么不用 INotifyPropertyChanged：改了 CreatedAt 或 Pinned 之后，条目本身没变，
    /// 变的是"它该排在哪儿"。让 ClipItem 实现通知接口要往每个属性里塞样板代码，
    /// 而这里最多几百条，直接让界面整体 Refresh 一次更省事。
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>从磁盘读回历史。启动时调一次。</summary>
    public void Load()
    {
        AppPaths.EnsureCreated();
        _items.Clear();

        if (!File.Exists(AppPaths.IndexFile))
        {
            return;
        }

        ClipIndex? index;
        try
        {
            var json = File.ReadAllText(AppPaths.IndexFile);
            index = JsonSerializer.Deserialize<ClipIndex>(json, JsonConfig.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            PreserveUnreadableIndex("解析失败");
            return;
        }

        if (index is null)
        {
            PreserveUnreadableIndex("内容为空");
            return;
        }

        if (index.Version <= 0)
        {
            // 文件存在、也不是空 JSON，却连版本号都没解析出来，说明结构跟我们预期的不一样
            // （字段改名、被别的程序写过、或者手工改坏了）。
            // 这时候 Items 大概率也是个空列表——**绝对不能拿它去覆盖原文件**，
            // 那等于把用户的历史一次性抹掉。
            PreserveUnreadableIndex("缺少版本号，结构不符合预期");
            return;
        }

        if (index.Version > ClipIndex.CurrentVersion)
        {
            // 是更新版本的软件写的。硬读进来再保存，会把新字段悄悄抹掉——宁可不动它。
            PreserveUnreadableIndex($"版本 {index.Version} 比当前支持的 {ClipIndex.CurrentVersion} 新");
            return;
        }

        foreach (var item in index.Items)
        {
            _items.Add(item);
        }
    }

    /// <summary>把当前列表写回磁盘。原子写入，中途失败不会损坏原文件。</summary>
    public void Save()
    {
        var index = new ClipIndex { Version = ClipIndex.CurrentVersion, Items = [.. _items] };
        var json = JsonSerializer.Serialize(index, JsonConfig.Options);

        lock (_fileLock)
        {
            AtomicFile.Write(AppPaths.IndexFile, json);
        }
    }

    /// <summary>
    /// 新增一条记录。
    ///
    /// 内容指纹相同的旧记录会被合并：只把时间刷成新的，不堆第二张卡片。这是用户选的行为。
    /// </summary>
    /// <returns>true = 真的新增了；false = 合并到了已有记录上。</returns>
    public bool Add(ClipItem item)
    {
        var existing = _items.FirstOrDefault(i => i.Kind == item.Kind && i.Hash == item.Hash);

        if (existing is not null)
        {
            // 合并掉的新条目要顺手清干净。
            //
            // 这不是可选的：Capture() 是先把图片写到磁盘、再调 Add() 的，
            // 走到这里说明那份图片刚写下去就成了没人引用的孤儿。
            // 而且触发它根本不需要崩溃——**复制同一张图两次就够了**
            // （Windows 一次复制经常连发两条 WM_CLIPBOARDUPDATE）。
            DeleteImageFiles(item);

            existing.CreatedAt = item.CreatedAt;
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        _items.Add(item);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>删除一条记录，连同它的图片文件一起删掉。</summary>
    public bool Remove(string id)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null)
        {
            return false;
        }

        _items.Remove(item);
        DeleteImageFiles(item);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>置顶 / 取消置顶。</summary>
    public void SetPinned(string id, bool pinned)
    {
        var item = _items.FirstOrDefault(i => i.Id == id);
        if (item is null || item.Pinned == pinned)
        {
            return;
        }

        item.Pinned = pinned;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>按 id 找一条记录。</summary>
    public ClipItem? FindById(string id) => _items.FirstOrDefault(i => i.Id == id);

    /// <summary>一次清理的结果，用来判断"这次有没有东西被删掉"。</summary>
    public readonly record struct CleanupResult(int Expired, int Evicted)
    {
        public bool AnythingRemoved => Expired > 0 || Evicted > 0;
    }

    /// <summary>
    /// 跑一遍自动清理：先按时效过期，再按容量淘汰。
    ///
    /// 两件事放在一个方法里是有意的——它们都以"删条目 + 写索引"结尾，
    /// 分开调会写两次索引，白白多两次磁盘 IO 和多两轮界面重建。
    /// </summary>
    public CleanupResult RunCleanup(int retentionDays, int maxTotalMB)
    {
        var expired = RemoveExpiredCore(retentionDays);
        var evicted = EvictOverCapacityCore(maxTotalMB);

        if (expired > 0 || evicted > 0)
        {
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new CleanupResult(expired, evicted);
    }

    /// <summary>
    /// 过期清理：删掉"没被置顶、且创建时间早于 N 天前"的条目。
    ///
    /// **置顶项永远不参与**——置顶就是用户明确说"这条我要留着"，
    /// 拿时间把它删掉等于把用户的意思直接推翻。
    /// </summary>
    private int RemoveExpiredCore(int retentionDays)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-retentionDays);

        var expired = _items
            .Where(item => !item.Pinned && item.CreatedAt < cutoff)
            .ToList();

        foreach (var item in expired)
        {
            _items.Remove(item);
            DeleteImageFiles(item);
        }

        return expired.Count;
    }

    /// <summary>
    /// 容量淘汰：总占用超过上限时，从**最旧的、且没被置顶的**开始删。
    ///
    /// 如果未置顶的都删光了还是超限就停下（说明置顶内容本身就超了）——
    /// 那种情况下再删就是在删用户明确要保留的东西了，宁可让文件夹超一点。
    /// </summary>
    private int EvictOverCapacityCore(int maxTotalMB)
    {
        var limit = maxTotalMB * 1024L * 1024L;
        var total = _items.Sum(item => item.SizeBytes);

        if (total <= limit)
        {
            return 0;
        }

        var evicted = 0;

        foreach (var item in _items.Where(i => !i.Pinned).OrderBy(i => i.CreatedAt).ToList())
        {
            if (total <= limit)
            {
                break;
            }

            _items.Remove(item);
            DeleteImageFiles(item);
            total -= item.SizeBytes;
            evicted++;
        }

        return evicted;
    }

    /// <summary>
    /// 清空全部历史。设置不参与，只删记录和图片。
    ///
    /// 图片是**直接清空整个 images 目录**，而不是逐条删各自引用的文件：
    /// 这样连之前因为文件被占用没删掉的孤儿文件也一并收走。
    /// </summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(AppPaths.ImagesDir))
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.ImagesDir))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 删不掉的留着，启动时的自检（S18）会再收一次
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("清空历史时清理图片目录失败", ex);
        }

        _items.Clear();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 删掉一条记录占用的图片文件。
    /// 删除失败（文件被别的程序占用等）不抛异常——记一笔就算了，
    /// 剩下的孤儿文件会在下次启动清理时（S18）统一收走，不能因为一个文件删不掉就卡住界面。
    ///
    /// 两种来源都要收：截图那样的真图片（<see cref="ClipItem.Images"/>），
    /// 以及复制图片文件时顺手生成的缩略图（<see cref="FileEntry.ThumbFile"/>）。
    /// 漏掉后者的话，**同一张照片复制两次**就会留下一张没人引用的缩略图——
    /// 合并时删的是"新写下那份"，旧的那份还在。
    /// </summary>
    private static void DeleteImageFiles(ClipItem item)
    {
        foreach (var image in item.Images)
        {
            TryDeleteImage(image.ImageFile);
            TryDeleteImage(image.ThumbFile);
        }

        foreach (var file in item.Files)
        {
            TryDeleteImage(file.ThumbFile);
        }
    }

    /// <summary>删 images 目录下的一个文件。名字为空（不是图片、没生成缩略图）就什么都不做。</summary>
    private static void TryDeleteImage(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        try
        {
            var path = AppPaths.ImagePath(name);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 留给孤儿文件清理收尾
        }
        catch (UnauthorizedAccessException)
        {
            // 同上
        }
    }

    /// <summary>
    /// 索引读不出来时，把坏文件改名留档，再从空列表开始。
    ///
    /// 绝对不能直接覆盖写——那等于把用户攒了几年的历史一次性抹掉。
    /// 留个 .corrupt 文件，用户至少还有机会自己捞回来。
    /// </summary>
    private static void PreserveUnreadableIndex(string reason)
    {
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var target = Path.Combine(AppPaths.Root, $"index.unreadable-{stamp}.json");
            File.Move(AppPaths.IndexFile, target);
            System.Diagnostics.Debug.WriteLine($"[StorageService] 索引无法读取（{reason}），已留档到 {target}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 连改名都做不到，那就只能从空列表开始了
        }
    }
}
