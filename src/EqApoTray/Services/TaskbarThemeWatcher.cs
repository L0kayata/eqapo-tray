using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace EqApoTray.Services;

// Signals changes to the taskbar ("Windows mode") theme. That is
// SystemUsesLightTheme, not AppsUseLightTheme: the two can be set
// independently, and only the former decides the tray icon colour.
//
// RegNotifyChangeKeyValue is kernel-signalled, so the idle cost is one parked
// thread-pool wait; nothing polls. onChanged runs on a thread-pool thread and
// fires for any value change under the key, so callers re-read and compare.
public sealed partial class TaskbarThemeWatcher : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";

    private const uint REG_NOTIFY_CHANGE_LAST_SET = 0x00000004;
    // The arming thread is a thread-pool thread that may exit; without this
    // flag the registration would die with it.
    private const uint REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

    private readonly RegistryKey? _key;
    private readonly AutoResetEvent _changed = new(false);
    private readonly RegisteredWaitHandle? _wait;
    private readonly Action _onChanged;

    public TaskbarThemeWatcher(Action onChanged)
    {
        _onChanged = onChanged;
        _key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        if (_key is null || !Arm())
        {
            return;
        }

        _wait = ThreadPool.RegisterWaitForSingleObject(
            _changed, OnKeyChanged, state: null, Timeout.Infinite, executeOnlyOnce: false);
    }

    // Missing value means dark: pre-1903 Windows 10 had no light taskbar.
    public static bool IsLightTaskbar()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(SystemUsesLightTheme) is int value && value == 1;
    }

    public void Dispose()
    {
        // Wait out an in-flight callback, which would otherwise re-arm on a
        // closed key.
        using var unregistered = new ManualResetEvent(false);
        if (_wait?.Unregister(unregistered) == true)
        {
            unregistered.WaitOne();
        }
        _key?.Dispose();
        _changed.Dispose();
    }

    private void OnKeyChanged(object? state, bool timedOut)
    {
        // Each registration fires once; re-arm before reporting so a change
        // landing in between is not missed.
        Arm();
        _onChanged();
    }

    private bool Arm()
    {
        return _key is not null && RegNotifyChangeKeyValue(
            _key.Handle,
            watchSubtree: false,
            REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC,
            _changed.SafeWaitHandle,
            asynchronous: true) == 0;
    }

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        SafeWaitHandle hEvent,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
