using Microsoft.Win32;

namespace EqApoTray.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "EqApoTray";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException("无法访问 HKCU\\...\\Run 注册表项");

        if (enable)
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法解析当前可执行文件路径");
            key.SetValue(AppName, $"\"{exe}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }
}
