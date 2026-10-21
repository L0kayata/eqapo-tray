using System.Buffers.Binary;
using System.Runtime.InteropServices;
using H.NotifyIcon.Core;

namespace EqApoTray.Services;

// Shows a 0/1/2-wave speaker in the tray depending on the Preamp gain, in the
// variant matching the taskbar theme. UI thread only.
//
// SetGain runs on every slider ValueChanged, so it only compares; the tray is
// touched (one Shell_NotifyIcon NIM_MODIFY) when the gain crosses a level
// boundary or the theme / icon size changes. HICONs are decoded lazily from
// the embedded .ico files and cached per (theme, level) at the current size.
// No Dispose: quit runs on the tray thread, and process exit frees the HICONs.
public sealed partial class TrayIconSwitcher
{
    private const double LowGainDb = -5;
    private const double HighGainDb = 5;
    private const int LevelCount = 3;

    private const int SM_CXSMICON = 49;
    private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
    private const int DefaultDpi = 96;
    private const uint IconResourceVersion = 0x00030000;

    private readonly TrayIcon _trayIcon;
    // [isLight ? 1 : 0, level]
    private readonly IntPtr[,] _icons = new IntPtr[2, LevelCount];
    private int _level = -1;
    private bool _isLight;
    private int _iconSize;

    public TrayIconSwitcher(TrayIcon trayIcon)
    {
        _trayIcon = trayIcon;
        _isLight = TaskbarThemeWatcher.IsLightTaskbar();
        _iconSize = GetTrayIconSize();
    }

    public void SetGain(double gainDb)
    {
        // -5 and +5 themselves belong to the middle level.
        var level = gainDb < LowGainDb ? 0 : gainDb > HighGainDb ? 2 : 1;
        if (level == _level)
        {
            return;
        }

        _level = level;
        Apply();
    }

    public void RefreshTheme()
    {
        var isLight = TaskbarThemeWatcher.IsLightTaskbar();
        if (isLight == _isLight)
        {
            return;
        }

        _isLight = isLight;
        Apply();
    }

    public void RefreshIconSize()
    {
        var size = GetTrayIconSize();
        if (size == _iconSize)
        {
            return;
        }

        _iconSize = size;
        var stale = (IntPtr[,])_icons.Clone();
        Array.Clear(_icons);
        Apply();

        // Only after the tray holds the new handle: H.NotifyIcon re-adds the
        // icon from TrayIcon.Icon when explorer restarts.
        DestroyIcons(stale);
    }

    // After explorer restarts. Pushes the icon unconditionally: an Apply that
    // raced the restart may have failed to reach the new taskbar.
    public void Reset()
    {
        RefreshIconSize();
        _isLight = TaskbarThemeWatcher.IsLightTaskbar();
        Apply();
    }

    private void Apply()
    {
        if (_level < 0)
        {
            return;
        }

        var theme = _isLight ? 1 : 0;
        ref var icon = ref _icons[theme, _level];
        if (icon == IntPtr.Zero)
        {
            icon = LoadIcon(_isLight ? "light" : "dark", _level, _iconSize);
        }

        try
        {
            _trayIcon.UpdateIcon(icon);
        }
        catch (InvalidOperationException)
        {
            // NIM_MODIFY fails while explorer restarts; TaskbarCreated then
            // calls Reset.
        }
    }

    private static IntPtr LoadIcon(string theme, int level, int size)
    {
        using var stream = typeof(TrayIconSwitcher).Assembly
            .GetManifestResourceStream($"TrayIcons.speaker_{level}_{theme}.ico");
        if (stream is null)
        {
            return IntPtr.Zero;
        }

        var ico = new byte[stream.Length];
        stream.ReadExactly(ico);

        // .ico file layout: 6-byte ICONDIR, then 16-byte ICONDIRENTRY each
        // (width, height, ..., bytesInRes @8, imageOffset @12; width 0 = 256).
        // Not LookupIconIdFromDirectoryEx: that parses the RT_GROUP_ICON
        // resource layout, whose entries are 14 bytes.
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        var best = -1;
        var bestWidth = 0;
        for (var i = 0; i < count; i++)
        {
            var width = ico[6 + 16 * i] is 0 ? 256 : ico[6 + 16 * i];
            // Smallest entry that is at least the target size, else the largest.
            var better = best < 0
                || (width >= size ? bestWidth < size || width < bestWidth : width > bestWidth);
            if (better)
            {
                best = i;
                bestWidth = width;
            }
        }

        if (best < 0)
        {
            return IntPtr.Zero;
        }

        var entry = ico.AsSpan(6 + 16 * best);
        var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
        var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
        return CreateIconFromResourceEx(
            ico.AsSpan(offset, length), (uint)length, fIcon: true, IconResourceVersion, size, size, 0);
    }

    // The notification area lives on the primary monitor's taskbar and draws
    // icons at SM_CXSMICON for that monitor's DPI.
    private static int GetTrayIconSize()
    {
        var monitor = MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);
        var dpi = GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out _) >= 0 && dpiX > 0
            ? dpiX
            : DefaultDpi;
        return GetSystemMetricsForDpi(SM_CXSMICON, dpi);
    }

    private static void DestroyIcons(IntPtr[,] icons)
    {
        foreach (var icon in icons)
        {
            if (icon != IntPtr.Zero)
            {
                DestroyIcon(icon);
            }
        }
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateIconFromResourceEx(
        ReadOnlySpan<byte> presbits,
        uint dwResSize,
        [MarshalAs(UnmanagedType.Bool)] bool fIcon,
        uint dwVer,
        int cxDesired,
        int cyDesired,
        uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private enum MonitorDpiType
    {
        Effective = 0,
    }
}
