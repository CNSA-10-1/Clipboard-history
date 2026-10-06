using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ClipboardHistory.Models;
using ClipboardHistory.Services;
using ClipboardHistory.Ui;

namespace ClipboardHistory;

/// <summary>
/// 历史列表主窗口。数据来自 <see cref="App.Storage"/>，列表变化时自动刷新。
///
/// 目前能浏览、能展开长内容。搜索、筛选、点击复制、置顶删除分别在 S12 / S10 / S11 接上。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 列表实际用的视图。
    ///
    /// 用 <see cref="CollectionViewSource.GetDefaultView"/> 而不是自己 new 一个 CollectionViewSource：
    /// 后者的 `.View` 在改 GroupDescriptions 之后**可能换一个实例**，
    /// 于是"我给 A 设了 Filter、列表却绑在 B 上"，筛选怎么都不生效。默认视图是稳定复用的。
    /// </summary>
    private ICollectionView? _itemsView;

    /// <summary>当前的类型筛选。默认"全部"。</summary>
    private ClipFilter _filter = ClipFilter.All;

    /// <summary>当前的关键词。空串表示没在搜。</summary>
    private string _query = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        // 标题栏是系统画的，得单独跟着主题走
        WindowChrome.FollowTheme(this);

        InitializeList();
    }

    private void InitializeList()
    {
        _itemsView = CollectionViewSource.GetDefaultView(App.Storage.Items);

        // 置顶的排最前，同组内按时间倒序
        _itemsView.SortDescriptions.Add(new SortDescription(nameof(ClipItem.Pinned), ListSortDirection.Descending));
        _itemsView.SortDescriptions.Add(new SortDescription(nameof(ClipItem.CreatedAt), ListSortDirection.Descending));

        _itemsView.Filter = Matches;

        ClipList.ItemsSource = _itemsView;

        // 不选中任何一条。否则 WPF 会自动选第一项，开场就带一圈蓝色选中框，
        // 看起来像是用户点过一样。
        ClipList.SelectedIndex = -1;

        App.Storage.Changed += OnStorageChanged;

        // 设置里改了保留天数，底部状态栏那句"保留 N 天"要跟着变
        App.Settings.Changed += OnSettingsChanged;

        ApplyViewOptions();

        IsVisibleChanged += OnVisibilityChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => UpdateStatus();

    /// <summary>
    /// 窗口露出来 / 藏起来时要跟着做的事。
    ///
    /// 焦点：每次显示都把焦点给列表，用户能直接按 ↑↓ 选内容。
    /// 不给的话焦点会停在搜索框里，↑↓ 被文本框吃掉（变成移动光标），键盘操作等于没有。
    /// 要搜索按 Ctrl+F。
    ///
    /// 计时器：只在窗口可见时走。藏起来的时候没人看"3 分钟前"，
    /// 让它每 30 秒空转一次纯属浪费电。
    /// </summary>
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            TimeTicker.Instance.Start();
            ClipList.Focus();
        }
        else
        {
            TimeTicker.Instance.Stop();
        }
    }

    // ── 搜索与筛选 ──

    /// <summary>
    /// 类型筛选和关键词是"且"的关系：选"图片"再搜一个词，只在图片里找。
    ///
    /// 类型匹配用的是"包含"而不是"等于"：一次复制了 2 个文件 + 1 个文件夹只会产生**一张**卡片，
    /// 这张混合卡片在"文件"和"文件夹"两个筛选下都该出现，不该被二选一。
    ///
    /// 「图片」尤其不能只看 Kind：**从资源管理器复制一张照片走的是"文件列表"格式**，
    /// 它的 Kind 是 Files，但它就是一张图片。所以图片文件按图片算、
    /// 不再算进「文件」——判断逻辑都在 <see cref="ClipItem.ContainsImage"/> 里。
    /// </summary>
    private bool Matches(object obj)
    {
        if (obj is not ClipItem item)
        {
            return false;
        }

        var kindOk = _filter switch
        {
            ClipFilter.Text => item.Kind == ClipKind.Text,
            ClipFilter.Image => item.ContainsImage,
            ClipFilter.File => item.ContainsFile,
            ClipFilter.Folder => item.ContainsFolder,
            _ => true,
        };

        if (!kindOk)
        {
            return false;
        }

        if (_query.Length == 0)
        {
            return true;
        }

        // 文件卡片连路径一起搜：用户记得的往往是"放在哪个文件夹里"，
        // 而卡片上显示的文件名可能好几个都一样（比如一堆 index.html）。
        var haystack = item.Kind == ClipKind.Files
            ? item.Text + " " + string.Join(' ', item.Files.Select(f => f.Path))
            : item.Text;

        return haystack.Contains(_query, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// 把筛选条件应用到列表上。
    ///
    /// 分组（"已置顶"/"最近"）只在没筛选时显示：搜索结果里再分组，
    /// 会出现"已置顶"下面孤零零一条、下面又跳回"最近"的割裂感，不如平铺。
    /// </summary>
    private void ApplyViewOptions()
    {
        if (_itemsView is null)
        {
            return;
        }

        var isFiltering = _filter != ClipFilter.All || _query.Length > 0;

        _itemsView.GroupDescriptions.Clear();
        if (!isFiltering)
        {
            _itemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ClipItem.GroupName)));
        }

        _itemsView.Refresh();
        UpdateStatus();
    }

    private void SearchInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _query = SearchInput.Text.Trim();
        ApplyViewOptions();
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<ClipFilter>(tag, out var filter))
        {
            _filter = filter;
            ApplyViewOptions();
        }
    }

    private enum ClipFilter
    {
        All,
        Text,
        Image,
        File,
        Folder,
    }

    /// <summary>
    /// 存储变了就整体重算一次视图。
    ///
    /// 因为排序（Pinned / CreatedAt）和分组都依赖条目属性，去重合并会改 CreatedAt、
    /// 置顶会改 Pinned，这些变化不重算视图是不会体现在排序上的。
    /// 条目最多几百条，重算的开销远小于给 ClipItem 的每个属性都挂通知。
    /// </summary>
    private void OnStorageChanged(object? sender, EventArgs e)
    {
        _itemsView?.Refresh();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var all = App.Storage.Items;
        var visible = _itemsView?.Cast<object>().Count() ?? 0;

        if (all.Count == 0)
        {
            ShowEmpty(
                icon: true,
                title: "还没有记录任何内容",
                hint: "复制点什么试试吧");
        }
        else if (visible == 0)
        {
            // 有记录、但当前条件筛不出东西——把条件说出来，用户才知道该怎么改
            ShowEmpty(icon: false, title: DescribeEmptyResult(), hint: null);
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            ClipList.Visibility = Visibility.Visible;
        }

        if (all.Count == 0)
        {
            StatusText.Text = "还没有记录任何内容";
            return;
        }

        // 筛选生效时把"显示了几条"也说出来，否则用户看着列表变短了、
        // 状态栏却还写着"共 4 条"，会以为软件把数据弄丢了。
        var shown = visible < all.Count ? $" · 显示 {visible} 条" : "";

        StatusText.Text =
            $"共 {all.Count} 条{shown} · 占用 {FormatSize(all.Sum(i => i.SizeBytes))}"
            + $" · 保留 {App.Settings.Current.RetentionDays} 天";
    }

    private void ShowEmpty(bool icon, string title, string? hint)
    {
        EmptyState.Visibility = Visibility.Visible;
        ClipList.Visibility = Visibility.Collapsed;

        EmptyIcon.Visibility = icon ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Margin = icon ? new Thickness(0, 14, 0, 0) : new Thickness(0);

        EmptyTitle.Text = title;
        EmptyHint.Text = hint ?? string.Empty;
        EmptyHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private string DescribeEmptyResult()
    {
        var kindName = _filter switch
        {
            ClipFilter.Text => "文字",
            ClipFilter.Image => "图片",
            ClipFilter.File => "文件",
            ClipFilter.Folder => "文件夹",
            _ => null,
        };

        return (kindName, _query.Length) switch
        {
            (null, 0) => "没有可显示的内容",
            (null, _) => $"没找到包含“{_query}”的内容",
            (_, 0) => $"没有「{kindName}」类型的内容",
            _ => $"「{kindName}」里没找到包含“{_query}”的内容",
        };
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };

    /// <summary>
    /// 每次窗口被激活时刷新一次主题。
    /// "跟随系统"模式下用户改了 Windows 主题也能跟上，见 ThemeService.RefreshIfFollowingSystem。
    /// </summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        ThemeService.RefreshIfFollowingSystem();
    }

    /// <summary>
    /// 点窗口的 × —— **不退出程序，只是把它藏起来**。
    ///
    /// 这是个托盘常驻的记录工具：关掉窗口后台还得继续记录，
    /// 不然用户"关个窗口"就悄悄把记录停了，等想起来的时候已经漏了一大段。
    /// 真正退出只能走托盘图标右键菜单里的"退出"。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        TimeTicker.Instance.Stop();
        App.Storage.Changed -= OnStorageChanged;
        App.Settings.Changed -= OnSettingsChanged;
        base.OnClosed(e);
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchInput.Clear();
        SearchInput.Focus();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => App.ShowSettings();

    /// <summary>
    /// 点卡片 = 把内容复制回剪贴板，然后窗口隐藏，用户自己按 Ctrl+V。
    ///
    /// 卡片里的网址链接、置顶/删除按钮、路径链接、展开按钮都会自己把点击吃掉，
    /// 所以点它们不会误触发复制——只有落在卡片空白处的点击才会走到这里。
    /// </summary>
    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ClipItem item })
        {
            return;
        }

        CopyItem(item);
    }

    /// <summary>写回剪贴板并隐藏窗口。写失败（比如内容已经失效）就什么都不做，别把窗口关了让用户莫名其妙。</summary>
    private void CopyItem(ClipItem item)
    {
        var monitor = App.Monitor;
        if (monitor is null || !ClipboardWriter.Write(item, monitor))
        {
            return;
        }

        DismissWindow();
    }

    /// <summary>
    /// 收起窗口，把焦点还给用户刚才在用的窗口——他接着按 Ctrl+V 就行。
    ///
    /// 用 Hide 而不是最小化：最小化会在任务栏留一个按钮，而这是个托盘程序，
    /// 焦点还回去之后任务栏再杵着一个图标很碍事。Hide 之后窗口彻底消失，
    /// 想回来点托盘图标即可。
    /// </summary>
    private void DismissWindow() => Hide();

    /// <summary>
    /// 右键菜单。ContextMenu 的 DataContext 继承自它挂着的那个卡片，所以能直接拿到条目。
    /// </summary>
    private void MenuCopy_Click(object sender, RoutedEventArgs e) => WithMenuTarget(sender, CopyItem);

    private void MenuPin_Click(object sender, RoutedEventArgs e) =>
        WithMenuTarget(sender, item => App.Storage.SetPinned(item.Id, !item.Pinned));

    private void MenuDelete_Click(object sender, RoutedEventArgs e) =>
        WithMenuTarget(sender, item => App.Storage.Remove(item.Id));

    private static void WithMenuTarget(object sender, Action<ClipItem> action)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            action(item);
        }
    }

    /// <summary>
    /// 键盘操作。放在窗口的 PreviewKeyDown 上统一分发，免得每个控件各挂一套。
    ///
    /// 用的是 Preview（隧道路由）而不是普通 KeyDown：焦点可能在搜索框里，
    /// 等冒泡上来很多键已经被控件自己吃掉了。
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            // 收起窗口。剪贴板工具的通用习惯——唤起、用完、一键消失。
            case Key.Escape:
                e.Handled = true;
                DismissWindow();
                break;

            case Key.F when Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
                e.Handled = true;
                SearchInput.Focus();
                SearchInput.SelectAll();
                break;

            // 回车 = 复制选中的那条。焦点在搜索框里时不响应，
            // 否则用户想在搜索框里回车确认时会莫名其妙地把某条内容复制走。
            case Key.Enter when ClipList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (ClipList.SelectedItem is ClipItem selected)
                {
                    CopyItem(selected);
                }

                break;

            case Key.Delete when ClipList.IsKeyboardFocusWithin:
                e.Handled = true;
                if (ClipList.SelectedItem is ClipItem toRemove)
                {
                    App.Storage.Remove(toRemove.Id);
                }

                break;
        }
    }

    private void RevealPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FileEntry entry })
        {
            ShellService.RevealInExplorer(entry);
        }
    }

    /// <summary>置顶 / 取消置顶。置顶的条目永不过期，所以这是个有分量的操作。</summary>
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        // 标记已处理：不让点击继续冒泡到卡片上，否则会顺带触发"复制"。
        // 按钮自己通常已经吃掉了，这里是保险。
        e.Handled = true;

        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            App.Storage.SetPinned(item.Id, !item.Pinned);
        }
    }

    /// <summary>
    /// 删除一条记录。**故意不弹确认框**——随手删一条历史不该被打断。
    /// 删错了可以重新复制一次，代价比每次都要点"确定"小得多。
    /// </summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            App.Storage.Remove(item.Id);
        }
    }

    /// <summary>
    /// 用户点了文件卡片的"展开"。
    ///
    /// 用 Click 而不是 Checked：Checked 是"状态变了"就触发，绑定求值、程序设值都算，
    /// 而且 XAML 里属性的处理是有先后顺序的——IsChecked 的绑定在 Checked 的处理器挂上之前
    /// 就已经求值过了，那一次触发根本没人接。
    /// Click 只在用户真的点了（或按空格）时才来，语义正合适。
    /// </summary>
    private void FileExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ClipItem item })
        {
            _ = ProbeFolderSizesAsync(item);
        }
    }

    /// <summary>
    /// 已经尝试过补缩略图的记录 id。
    ///
    /// 有它才能区分"还没补"和"补了但失败了"——生成失败（文件已删除、格式不认识）
    /// 返回的是空串，光看数据分不出这两种情况，卡片每次滚回来都会重试一遍。
    /// </summary>
    private readonly HashSet<string> _thumbnailAttempted = [];

    /// <summary>一张卡片最多补几张缩略图。跟复制时的上限一致，理由也一样：别让一张卡片吃掉几秒钟。</summary>
    private const int MaxThumbnailsPerCard = 20;

    /// <summary>
    /// 卡片渲染出来后，在后台做两件慢活：量文件夹大小、补图片缩略图。
    ///
    /// 为什么不是"只在展开时算"：只含**一个**文件夹的卡片条目不会溢出，
    /// 压根不显示"展开"按钮——那样它的大小会永远停在"…"，用户永远看不到。
    ///
    /// 也不能立刻就做：列表是虚拟化的，用户快速滚过去的卡片会被 Loaded 一遍，
    /// 为它们逐个扫盘、解图很浪费。所以等 300ms，卡片真的停下来了才开始。
    /// </summary>
    private void FileEntries_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not ClipItem item)
        {
            return;
        }

        var needsFolderSize = item.Files.Any(f => f.IsDirectory && f.SizeBytes == FileEntry.UnknownSize);
        var needsThumbnails = item.Files.Any(f => f.IsImage && f.ThumbFile.Length == 0)
                              && !_thumbnailAttempted.Contains(item.Id);

        if (!needsFolderSize && !needsThumbnails)
        {
            return;
        }

        var settle = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };

        settle.Tick += (_, _) =>
        {
            settle.Stop();

            // 已经滚走了就算了，等它下次露出来再说
            if (!element.IsLoaded)
            {
                return;
            }

            if (needsFolderSize)
            {
                _ = ProbeFolderSizesAsync(item);
            }

            if (needsThumbnails)
            {
                _ = BackfillThumbnailsAsync(item);
            }
        };

        settle.Start();
    }

    /// <summary>
    /// 给"这个功能上线之前就存下来的"图片文件补缩略图，让老记录也能看到预览。
    ///
    /// 放在后台做，而且只对用户**真正看到**的卡片做：新记录在复制的那一刻就生成好了，
    /// 只有历史数据需要补；如果启动时把所有历史一次性补一遍，
    /// 几百张照片会让程序刚起来就狂读磁盘。
    /// </summary>
    private async Task BackfillThumbnailsAsync(ClipItem item)
    {
        if (!_thumbnailAttempted.Add(item.Id))
        {
            return;
        }

        var pending = item.Files
            .Where(f => f.IsImage && f.ThumbFile.Length == 0)
            .Take(MaxThumbnailsPerCard)
            .ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        var added = 0L;

        foreach (var entry in pending)
        {
            var thumbnail = await ImageStore.SaveThumbnailForAsync(entry.Path);
            if (thumbnail.FileName.Length == 0)
            {
                // 文件已经不在、或者不是能解码的图片。留空串，卡片继续显示图标
                continue;
            }

            // 赋值会发 PropertyChanged，卡片上的预览自己就出现了
            entry.ThumbFile = thumbnail.FileName;
            item.SizeBytes += thumbnail.SizeBytes;
            added += thumbnail.SizeBytes;
        }

        if (added > 0)
        {
            // 缩略图文件名必须落盘。不存的话下次启动时它就成了没人引用的孤儿文件，
            // 会被数据文件夹自检删掉，然后又被重新生成一遍，来回白折腾。
            App.Storage.Save();
        }
    }

    /// <summary>
    /// 把这张卡片里还没算过大小的文件夹在后台量一下。
    ///
    /// 放后台是必须的：递归遍历文件夹可能要几秒甚至更久，
    /// 在剪贴板监听回调里做这件事就等于整个软件卡住。
    /// </summary>
    private async Task ProbeFolderSizesAsync(ClipItem item)
    {
        var pending = item.Files
            .Where(f => f.IsDirectory && f.SizeBytes == FileEntry.UnknownSize)
            .ToArray();

        if (pending.Length == 0)
        {
            return;
        }

        foreach (var entry in pending)
        {
            entry.SizeBytes = await FolderSizeProbe.MeasureAsync(entry.Path);
        }

        // 不需要手工刷新界面：FileEntry 会发 PropertyChanged，
        // 显示大小的那个 TextBlock 自己就更新了。
    }
}
