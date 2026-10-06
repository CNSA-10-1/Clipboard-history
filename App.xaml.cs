using System.IO;
using System.Threading;
using System.Windows;
using ClipboardHistory.Models;
using ClipboardHistory.Services;
using ClipboardHistory.Ui;

namespace ClipboardHistory;

/// <summary>
/// 应用入口。负责把各个服务串起来：
/// 单实例守卫 → 存储 → 主题 → 剪贴板监听 → 托盘图标 → 主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>互斥体名字。带 Local\ 前缀表示"只对当前登录会话唯一"。</summary>
    private const string MutexName = @"Local\ClipboardHistory.SingleInstance";

    /// <summary>用来叫醒已经在跑的那个实例，让它把窗口显示出来。</summary>
    private const string ShowWindowEventName = @"Local\ClipboardHistory.ShowWindow";

    /// <summary>历史记录仓库。界面通过它读数据。</summary>
    public static StorageService Storage { get; private set; } = null!;

    /// <summary>用户设置。保留期限、容量上限、主题、开机自启都在这里。</summary>
    public static SettingsService Settings { get; private set; } = null!;

    /// <summary>
    /// 剪贴板监听器。界面写回剪贴板时要通过它"报告"这次变化是自己造成的，
    /// 否则点一次卡片，历史里就会多冒出一条一样的内容。
    /// </summary>
    public static ClipboardMonitor? Monitor { get; private set; }

    /// <summary>
    /// 是不是真的要退出了。
    ///
    /// 关窗口**不等于**退出——那只是把它藏起来，后台还要继续记录。
    /// 只有托盘菜单里的"退出"会把它置为 true，那时候才允许窗口真正关闭。
    /// </summary>
    public static bool IsExiting { get; private set; }

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowSignal;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private CleanupService? _cleanup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例守卫要放在最前面：已经有实例在跑的话，
        // 连存储都不该去碰——两个实例同时写 index.json 会互相覆盖。
        if (!TryClaimSingleInstance())
        {
            NotifyExistingInstance();
            Shutdown();
            return;
        }

        InstallCrashGuards();

        AppPaths.EnsureCreated();

        Storage = new StorageService();
        Storage.Load();

        Settings = new SettingsService();
        Settings.Load();

        // 必须在任何窗口创建之前应用主题，否则会先按默认色闪一下再变色
        ThemeService.Apply(Settings.Current.Theme);

        _cleanup = new CleanupService(Storage, Settings);

        Settings.Changed += (_, _) =>
        {
            ThemeService.Apply(Settings.Current.Theme);

            // 改完保留天数或容量上限立刻清一次：用户把 30 天改成 1 天，
            // 期待的是"马上就只剩一天的"，而不是"等下一个整点再说"。
            _cleanup.RunNow();
        };

        // 开机自启记的是绝对路径。软件被挪到别处之后那条就失效了，
        // 而且开机时不报错，用户只会发现"说好的自启没了"。所以每次启动都检查一遍。
        StartupService.RepairIfStale();

        // 启动时先清一遍，然后挂上每小时的定时器
        _cleanup.Start();

        Monitor = new ClipboardMonitor();
        Monitor.ClipboardChanged += OnClipboardChanged;

        _mainWindow = new MainWindow();

        _tray = new TrayService(
            AppIcon.Load(16),
            onOpen: () => OnUiThread(ShowMainWindow),
            onSettings: () => OnUiThread(OpenSettings),
            onExit: () => OnUiThread(ExitApplication));

        // 启动时不弹窗口：这是开机自启的常驻工具，
        // 每次开机都蹦一个窗口出来很烦。要打开就点托盘图标，
        // 或者再双击一次 exe（走单实例那条路把窗口叫出来）。
    }

    /// <summary>从界面打开设置窗口。主窗口的齿轮按钮走这里。</summary>
    public static void ShowSettings()
    {
        if (Current is App app)
        {
            app.OnUiThread(app.OpenSettings);
        }
    }

    /// <summary>
    /// 打开设置窗口。已经开着的话就把它拉到前面，不再开第二个——
    /// 同时开两个设置窗口，改了两份不同的值再都点保存，谁也说不清最后生效的是哪份。
    /// </summary>
    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(Settings, Storage);

        // 主窗口开着的话就跟着它居中，看着像它的子窗口；主窗口没开（从托盘进来的）
        // 就居中到屏幕，免得跑到某个奇怪的位置。
        _settingsWindow.Owner = _mainWindow is { IsVisible: true } ? _mainWindow : null;
        _settingsWindow.WindowStartupLocation = _settingsWindow.Owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;

        _settingsWindow.Show();

        // CenterOwner 有个坑：主人窗口贴着屏幕边缘时，按它居中的设置窗口会被顶出去一大截，
        // 用户只看见屏幕边上露一条，剩下的够不着。显示之后再夹一次，保证整窗落在工作区内。
        WindowPlacement.ClampToWorkArea(_settingsWindow);
    }

    /// <summary>
    /// 保证操作在 UI 线程上执行。
    ///
    /// 托盘图标的事件通常由 WPF 的消息泵派发、本来就在 UI 线程上，
    /// 但这不是我们能完全掌控的（NotifyIcon 内部有自己的窗口）。碰界面的代码
    /// 一律走这里过一道，免得哪天换了个写法就变成跨线程访问控件。
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.Invoke(action);
        }
    }

    /// <summary>把窗口显示出来并抢到前台。</summary>
    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();

        // Activate 有时候抢不到前台（Windows 对后台程序抢焦点有限制），
        // 用 Topmost 抖一下是常见做法。
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;

        _mainWindow.Focus();
    }

    private void ExitApplication()
    {
        IsExiting = true;
        Shutdown();
    }

    /// <summary>剪贴板变了。抓内容 → 落库。抓不到（比如只有一个我们处理不了的格式）就什么都不做。</summary>
    private void OnClipboardChanged(object? sender, EventArgs e)
    {
        // 最外面再兜一层。剪贴板内容是不可信的外部数据，解析它出什么岔子都不该让程序死掉——
        // 一个记录剪贴板的工具自己崩了，比漏记一条严重得多。
        try
        {
            var item = ClipboardCapture.Capture();
            if (item is not null)
            {
                Storage.Add(item);
            }
        }
        catch (Exception ex)
        {
            Log.Error("处理剪贴板变化时出错，已跳过这次内容", ex);
        }
    }

    // ── 单实例 ──

    /// <summary>
    /// 抢占单实例所有权。返回 true 表示"我才是那个唯一的实例"。
    ///
    /// 为什么必须有这个：剪贴板监听和 index.json 的写入都是按"只有一个实例"设计的。
    /// 开两个的话两边都会收到剪贴板变化、都要写索引，轻则重复记录，重则互相覆盖把历史写坏。
    /// </summary>
    private bool TryClaimSingleInstance()
    {
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

            if (!createdNew)
            {
                return false;
            }

            _showWindowSignal = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ShowWindowEventName);

            // 后台线程等着别的实例来敲门。用后台线程是为了不拦住进程退出。
            var listener = new Thread(ListenForShowRequests)
            {
                IsBackground = true,
                Name = "SingleInstanceListener",
            };
            listener.Start();

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // 拿不到互斥体（权限问题等）。宁可放行让它跑，也不要因为守卫本身出问题就起不来。
            Log.Error("单实例守卫初始化失败，本次按独立实例运行", ex);
            return true;
        }
    }

    private void ListenForShowRequests()
    {
        while (true)
        {
            try
            {
                if (_showWindowSignal is null || !_showWindowSignal.WaitOne())
                {
                    return;
                }

                // 回调要回 UI 线程——窗口只能在 UI 线程上动
                Dispatcher.Invoke(ShowMainWindow);
            }
            catch (Exception ex)
            {
                Log.Error("处理\"显示窗口\"请求时出错", ex);
                return;
            }
        }
    }

    /// <summary>告诉已经在跑的那个实例："有人又想打开你了，把窗口显示出来。"</summary>
    private static void NotifyExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var signal))
            {
                using (signal)
                {
                    signal.Set();
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or WaitHandleCannotBeOpenedException)
        {
            // 叫不醒就算了，用户自己点托盘图标也是一样的
        }
    }

    // ── 退出 ──

    protected override void OnExit(ExitEventArgs e)
    {
        _cleanup?.Dispose();
        _tray?.Dispose();
        Monitor?.Dispose();

        _showWindowSignal?.Dispose();
        _instanceMutex?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// 最后一道防线：任何没被接住的异常都不让它结束进程。
    ///
    /// 这个软件是常驻后台的，崩了用户未必立刻发现——等发现时可能已经漏记了半天内容。
    /// 出异常就记日志、继续跑。（S20 会把这里完善成带用户提示的版本。）
    /// </summary>
    private void InstallCrashGuards()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("界面线程出现未处理异常", args.Exception);

            // 标记已处理，保住进程
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("后台线程出现未处理异常", args.ExceptionObject as Exception);
    }
}
