using System;
using System.IO;
using Microsoft.Win32;

namespace PaperCare;

/// <summary>
/// Owns MoniPaper's per-user startup value. The startup-approved policy is
/// managed by Windows and is intentionally not read or rewritten here.
/// </summary>
internal sealed class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "MoniPaper";

    private readonly string _runKeyPath;
    private readonly string _valueName;
    private readonly Func<string?> _processPathProvider;

    internal StartupRegistration(
        string runKeyPath = RunKeyPath,
        string valueName = ValueName,
        Func<string?>? processPathProvider = null)
    {
        _runKeyPath = runKeyPath;
        _valueName = valueName;
        _processPathProvider = processPathProvider ?? (() => Environment.ProcessPath);
    }

    internal bool TrySetEnabled(bool enabled, out string? error)
    {
        error = null;
        try
        {
            if (!enabled)
            {
                // Do not create the Run key merely to disable MoniPaper. This
                // removes only our value and leaves other startup entries alone.
                using var existingKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
                existingKey?.DeleteValue(_valueName, throwOnMissingValue: false);
                return true;
            }

            var processPath = _processPathProvider();
            if (string.IsNullOrWhiteSpace(processPath))
            {
                error = "无法设置开机启动：找不到当前程序的绝对路径。";
                return false;
            }

            var absolutePath = Path.GetFullPath(processPath);
            using var runKey = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true);
            if (runKey is null)
            {
                error = "无法设置开机启动：当前用户没有写入启动项的权限。";
                return false;
            }

            runKey.SetValue(_valueName, BuildCommand(absolutePath), RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            error = enabled
                ? $"开机启动设置失败：无法写入当前用户的启动项（{ex.Message}）。"
                : $"开机启动关闭失败：无法移除 MoniPaper 的启动项（{ex.Message}）。";
            return false;
        }
    }

    internal bool TryRead(out StartupEntry entry, out string? error)
    {
        entry = default;
        error = null;
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
            if (runKey is null)
            {
                entry = new StartupEntry(false, null);
                return true;
            }

            var valueNames = runKey.GetValueNames();
            if (!Array.Exists(valueNames, value => string.Equals(value, _valueName, StringComparison.OrdinalIgnoreCase)))
            {
                entry = new StartupEntry(false, null);
                return true;
            }

            entry = new StartupEntry(true, runKey.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            error = $"无法读取开机启动状态：当前用户的启动项不可访问（{ex.Message}）。";
            return false;
        }
    }

    internal static string BuildCommand(string absoluteProcessPath) =>
        $"\"{absoluteProcessPath.Replace("\"", "\\\"", StringComparison.Ordinal)}\" --startup";

    internal readonly record struct StartupEntry(bool Exists, string? Command);
}
