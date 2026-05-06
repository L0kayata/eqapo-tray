using System.Runtime.InteropServices;
using EqApoTray.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using WinRT.Interop;

namespace EqApoTray;

public sealed partial class FlyoutWindow : Window
{
    private const double DefaultWidth = 340;
    private const double DefaultHeight = 200;
    private const double GapPx = 8;
    private const double EdgePaddingPx = 6;

    // Click-outside dismiss is implemented with a low-level mouse hook because
    // Window.Activated / WM_ACTIVATE are both unreliable on a WinUI 3 window
    // that's been Hide()'d and Show()'d again. Composite of three known bugs:
    //   - microsoft-ui-xaml#7595 (Window.Activate doesn't foreground)
    //   - microsoft-ui-xaml#9990 (OverlappedPresenter.IsAlwaysOnTop pulls focus
    //     back, the "desktop icons flicker but window doesn't dismiss" symptom)
    //   - MS Q&A "Deactivated event not firing in NOACTIVATE state"
    // Even a raw comctl32 SetWindowSubclass on the HWND doesn't see WA_INACTIVE
    // after the first cycle. WH_MOUSE_LL bypasses the entire activation path.
    private const int WH_MOUSE_LL    = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;

    private readonly IntPtr _hwnd;
    private readonly OverlappedPresenter _presenter;
    private readonly DispatcherQueue _dispatcherQueue;

    // Field-held so the GC can't collect the delegate while the OS still holds
    // a function pointer to it via SetWindowsHookEx.
    private LowLevelMouseProc? _mouseHookProc;
    private IntPtr _mouseHookHandle = IntPtr.Zero;

    public FlyoutWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        SystemBackdrop = new DesktopAcrylicBackdrop();

        _presenter = OverlappedPresenter.Create();
        _presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        // Deliberately NOT setting IsAlwaysOnTop. OverlappedPresenter's
        // implementation pulls focus back to the window after losing it, which
        // is the root cause of "click outside doesn't dismiss" after the first
        // Hide/Show cycle. Topmost is set manually below via SetWindowPos.
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Hide();

        Flyout.DialogOwnerHwnd = _hwnd;
    }

    public bool IsOpen { get; private set; }

    public void ShowAt(TrayPopupPositioner.ScreenBounds anchor)
    {
        var dpi = GetDpiForWindow(_hwnd);
        var dpiScale = (dpi == 0 ? 96 : dpi) / 96.0;
        var widthPx = (int)Math.Round(DefaultWidth * dpiScale);
        var heightPx = (int)Math.Round(DefaultHeight * dpiScale);

        var (xPx, yPx) = ComputePosition(anchor, widthPx, heightPx, dpiScale);

        AppWindow.MoveAndResize(new RectInt32(xPx, yPx, widthPx, heightPx));
        AppWindow.Show(activateWindow: true);

        // Manual topmost; see IsAlwaysOnTop note above.
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);

        // Helps with WinUI 3 issue #7595 (Window.Activate doesn't foreground a
        // window reliably after Hide); harmless when foreground is already us.
        SetForegroundWindow(_hwnd);

        IsOpen = true;

        InstallMouseHook();

        Flyout.RefreshFromDisk();
        PlayEnterAnimation();
    }

    public void HideFlyout()
    {
        if (!IsOpen)
        {
            return;
        }

        UninstallMouseHook();
        AppWindow.Hide();
        IsOpen = false;
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        _mouseHookProc = MouseHookProc;
        _mouseHookHandle = SetWindowsHookExW(WH_MOUSE_LL, _mouseHookProc, GetModuleHandleW(null), 0);
    }

    private void UninstallMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
        _mouseHookProc = null;
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsOpen)
        {
            var msg = wParam.ToInt32();
            if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
            {
                var data = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
                if (GetWindowRect(_hwnd, out var rect))
                {
                    var inside =
                        data.pt.X >= rect.Left && data.pt.X < rect.Right &&
                        data.pt.Y >= rect.Top  && data.pt.Y < rect.Bottom;

                    if (!inside)
                    {
                        // Don't swallow the click — let it propagate to whatever
                        // the user actually clicked on. We just dismiss in
                        // parallel.
                        _dispatcherQueue.TryEnqueue(HideFlyout);
                    }
                }
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private (int X, int Y) ComputePosition(
        TrayPopupPositioner.ScreenBounds anchor,
        int widthPx,
        int heightPx,
        double dpiScale)
    {
        var widthDip = widthPx / dpiScale;
        var heightDip = heightPx / dpiScale;

        var leftDip = anchor.Left + (anchor.Width - widthDip) / 2;
        var topDip = anchor.Top - heightDip - GapPx;

        if (topDip < anchor.WorkTop + EdgePaddingPx)
        {
            topDip = anchor.Bottom + GapPx;
        }

        leftDip = Clamp(leftDip, anchor.WorkLeft + EdgePaddingPx, anchor.WorkRight - widthDip - EdgePaddingPx);
        topDip = Clamp(topDip, anchor.WorkTop + EdgePaddingPx, anchor.WorkBottom - heightDip - EdgePaddingPx);

        return ((int)Math.Round(leftDip * dpiScale), (int)Math.Round(topDip * dpiScale));
    }

    private void PlayEnterAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(220));

        EnterTranslate.Y = 12;
        RootGrid.Opacity = 0;

        var translateAnimation = new DoubleAnimation
        {
            From = 12,
            To = 0,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(translateAnimation, EnterTranslate);
        Storyboard.SetTargetProperty(translateAnimation, "Y");

        var fadeAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(fadeAnimation, RootGrid);
        Storyboard.SetTargetProperty(fadeAnimation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(translateAnimation);
        storyboard.Children.Add(fadeAnimation);
        storyboard.Begin();
    }

    private static double Clamp(double value, double min, double max)
    {
        return max < min ? min : Math.Min(Math.Max(value, min), max);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseLowLevelHookStruct
    {
        public Point pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16, EntryPoint = "GetModuleHandleW")]
    private static partial IntPtr GetModuleHandleW(string? lpModuleName);

    // [DllImport] (not LibraryImport) because the second parameter is a
    // managed delegate; LibraryImport's source generator only supports
    // delegate*<...> for callback marshalling, which would require an
    // [UnmanagedCallersOnly] static method and a parallel instance lookup.
    // Plain DllImport is AOT-supported as long as the delegate stays alive,
    // which we ensure with the _mouseHookProc field.
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
}
