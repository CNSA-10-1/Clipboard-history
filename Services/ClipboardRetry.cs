using System.Runtime.InteropServices;

namespace ClipboardHistory.Services;

/// <summary>
/// 剪贴板操作的统一重试封装。
///
/// 剪贴板是全局独占资源：别的程序正打开着它的时候，读写会抛 COMException（HRESULT 0x800401D0）。
/// 这种情况短则几毫秒就过去了，重试一下基本都能成功。
/// **所有剪贴板读写都必须走这里**，不允许裸调用 Clipboard.*，否则会随机崩。
/// </summary>
internal static class ClipboardRetry
{
    private const int Attempts = 3;
    private const int DelayMs = 50;

    /// <summary>执行一个会碰剪贴板的操作，失败就等 50ms 再试，最多 3 次。</summary>
    public static bool Try(Action action)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (ExternalException)
            {
                // COMException 也走这条（它是 ExternalException 的子类）
                Thread.Sleep(DelayMs);
            }
            catch (Exception ex)
            {
                GiveUp(nameof(Try), ex);
                return false;
            }
        }

        return false;
    }

    /// <summary>读取剪贴板内容，失败返回 null。用于返回引用类型（字符串、数组等）的读操作。</summary>
    public static T? TryGet<T>(Func<T?> read) where T : class
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                return read();
            }
            catch (ExternalException)
            {
                Thread.Sleep(DelayMs);
            }
            catch (Exception ex)
            {
                GiveUp(nameof(TryGet), ex);
                return null;
            }
        }

        return null;
    }

    /// <summary>读值类型（比如拿到 BitmapSource 后算尺寸）用这个，拿不到时返回 fallback。</summary>
    public static T TryGetValue<T>(Func<T> read, T fallback)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                return read();
            }
            catch (ExternalException)
            {
                Thread.Sleep(DelayMs);
            }
            catch (Exception ex)
            {
                GiveUp(nameof(TryGetValue), ex);
                return fallback;
            }
        }

        return fallback;
    }

    /// <summary>
    /// 非重试类的异常：放弃，但记一笔日志。
    ///
    /// 为什么这里必须有个兜底的 catch：**剪贴板里放什么完全由别的程序决定**，
    /// 我们是在解析不可信的外部数据。具体踩到过的坑——剪贴板里放着 .NET 序列化的对象时，
    /// WPF 会调 BinaryFormatter 去反序列化，而 .NET 9 已经把它移除了，于是抛
    /// PlatformNotSupportedException。它不是 COMException，上面那条 catch 接不住；
    /// 如果让它继续往外逃，会一路穿过消息循环，**整个程序当场崩溃**。
    ///
    /// 重试对这类错误没意义（不是时序问题），所以直接放弃这一条剪贴板内容。
    /// </summary>
    private static void GiveUp(string operation, Exception ex) =>
        Log.Error($"剪贴板操作 {operation} 失败，已跳过这次内容", ex);
}
