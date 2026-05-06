using System.Runtime.InteropServices;
using H.NotifyIcon;

namespace EqApoTray.Services;

public static partial class TrayPopupPositioner
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int DefaultDpi = 96;

    public static bool TryGetTrayIconBounds(TaskbarIcon trayIcon, out ScreenBounds bounds)
    {
        bounds = default;

        if (!TryGetNotifyIconRect(trayIcon, out var iconRect))
        {
            return false;
        }

        bounds = ToScreenBounds(iconRect);
        return true;
    }

    public static bool TryGetCursorFallbackBounds(out ScreenBounds bounds)
    {
        bounds = default;

        if (!GetCursorPos(out var cursor))
        {
            return false;
        }

        var cursorRect = new NativeRect
        {
            Left = cursor.X - 1,
            Top = cursor.Y - 1,
            Right = cursor.X + 1,
            Bottom = cursor.Y + 1,
        };

        bounds = ToScreenBounds(cursorRect);
        return true;
    }

    private static bool TryGetNotifyIconRect(TaskbarIcon trayIcon, out NativeRect iconRect)
    {
        var windowHandle = trayIcon.TrayIcon.WindowHandle;
        var iconId = trayIcon.TrayIcon.Id;

        var identifier = new NotifyIconIdentifier
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            HWnd = windowHandle,
            GuidItem = iconId,
        };

        if (TryShellNotifyIconGetRect(identifier, out iconRect))
        {
            return true;
        }

        identifier.GuidItem = Guid.Empty;
        identifier.UID = 0;
        return TryShellNotifyIconGetRect(identifier, out iconRect);
    }

    private static bool TryShellNotifyIconGetRect(NotifyIconIdentifier identifier, out NativeRect iconRect)
    {
        iconRect = default;

        try
        {
            var result = Shell_NotifyIconGetRect(ref identifier, out iconRect);
            return result >= 0 && iconRect.Width > 0 && iconRect.Height > 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static ScreenBounds ToScreenBounds(NativeRect anchorRect)
    {
        var monitor = MonitorFromRect(ref anchorRect, MonitorDefaultToNearest);
        var workArea = TryGetWorkArea(monitor, out var area) ? area : GetVirtualScreenBounds();
        var scale = GetMonitorScale(monitor);

        return new ScreenBounds(
            anchorRect.Left / scale.X,
            anchorRect.Top / scale.Y,
            anchorRect.Right / scale.X,
            anchorRect.Bottom / scale.Y,
            workArea.Left / scale.X,
            workArea.Top / scale.Y,
            workArea.Right / scale.X,
            workArea.Bottom / scale.Y);
    }

    private static bool TryGetWorkArea(IntPtr monitor, out NativeRect workArea)
    {
        workArea = default;

        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var info = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>(),
        };

        if (!GetMonitorInfoW(monitor, ref info))
        {
            return false;
        }

        workArea = info.WorkArea;
        return true;
    }

    private static NativeRect GetVirtualScreenBounds()
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);

        return new NativeRect
        {
            Left = left,
            Top = top,
            Right = left + GetSystemMetrics(SmCxVirtualScreen),
            Bottom = top + GetSystemMetrics(SmCyVirtualScreen),
        };
    }

    private static DpiScale GetMonitorScale(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero)
        {
            return new DpiScale(1, 1);
        }

        try
        {
            var result = GetDpiForMonitor(monitor, MonitorDpiType.Effective, out var dpiX, out var dpiY);
            if (result >= 0 && dpiX > 0 && dpiY > 0)
            {
                return new DpiScale(dpiX / (double)DefaultDpi, dpiY / (double)DefaultDpi);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return new DpiScale(1, 1);
    }

    [LibraryImport("shell32.dll")]
    private static partial int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect iconLocation);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int index);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    public readonly record struct ScreenBounds(
        double Left,
        double Top,
        double Right,
        double Bottom,
        double WorkLeft,
        double WorkTop,
        double WorkRight,
        double WorkBottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
    }

    private readonly record struct DpiScale(double X, double Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint CbSize;
        public IntPtr HWnd;
        public uint UID;
        public Guid GuidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    private enum MonitorDpiType
    {
        Effective = 0,
    }
}
