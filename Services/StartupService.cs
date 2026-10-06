using System.IO;
using Microsoft.Win32;

namespace ClipboardHistory.Services;

/// <summary>
/// 开机自动启动。原理是往注册表的 Run 项写一条，Windows 登录时会执行它。
/// 细节见 docs/07-发布打包.md。
/// </summary>
public static class StartupService
{
    /// <summary>
    /// 用 HKCU 而不是 HKLM：**不需要管理员权限**，而且只对当前用户生效
    /// （一台电脑多个用户时，别人不该被动地跟着自启）。
    /// </summary>
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>值的名字。就是它在"任务管理器 → 启动"里显示的名字。</summary>
    private const string ValueName = "剪贴板历史";

    /// <summary>当前进程的可执行文件路径。</summary>
    /// <remarks>
    /// 用 <see cref="Environment.ProcessPath"/> 而不是 Assembly.Location：
    /// 单文件发布下后者会是空字符串，写进注册表就是一条空命令。
    /// </remarks>
    public static string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    /// <summary>注册表里现在有没有这一项。</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>注册表里记的路径和当前 exe 是不是同一个。用来判断"软件被挪走了"。</summary>
    public static bool PointsAtCurrentExecutable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var value = (key?.GetValue(ValueName) as string)?.Trim('"');

            return !string.IsNullOrEmpty(value)
                   && string.Equals(value, ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>打开或关闭自启。返回是否成功，失败时通过 <paramref name="error"/> 给出原因。</summary>
    public static bool TrySet(bool enabled, out string? error)
    {
        error = null;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                error = "打不开注册表的启动项，可能被安全软件拦了。";
                return false;
            }

            if (enabled)
            {
                if (ExecutablePath.Length == 0)
                {
                    error = "拿不到程序自身的路径，没法设置开机启动。";
                    return false;
                }

                // 路径带空格时必须用引号包起来，否则 Windows 会把空格前那截当成命令、
                // 后面当成参数，结果是启动失败且不报错。
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            }
            else
            {
                // 关闭要**删掉这个值**，不能设成空字符串：
                // 空值会让 Windows 尝试执行一条空命令。
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                       or System.Security.SecurityException
                                       or IOException)
        {
            error = "写注册表失败：" + ex.Message;
            Log.Error("设置开机自启失败", ex);
            return false;
        }
    }

    /// <summary>
    /// 软件被挪到别的位置之后，注册表里那条还指着老路径——开机会失败，而且不会报错，
    /// 用户只会发现"说好的自启没了"。所以每次启动都检查一遍，对不上就改过来。
    /// </summary>
    public static void RepairIfStale()
    {
        if (!IsEnabled() || PointsAtCurrentExecutable())
        {
            return;
        }

        if (TrySet(true, out var error))
        {
            Log.Error($"开机自启的路径已失效，已更新为 {ExecutablePath}");
        }
        else
        {
            Log.Error("开机自启的路径已失效，且更新失败：" + error);
        }
    }
}
