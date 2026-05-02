using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using EqApoTray.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Wpf.Ui.Appearance;
using DrawingPoint = System.Drawing.Point;

namespace EqApoTray;

public partial class App : Application
{
    // The popup auto-closes on focus loss before TrayLeftMouseUp fires, so the click
    // that *triggered* the close arrives at us with the popup already closed. We
    // suppress that single click; the window only needs to cover normal message-pump
    // latency between Popup.Closed and our handler — kept small so rapid reopen
    // clicks aren't blocked.
    private static readonly TimeSpan ReopenSuppressWindow = TimeSpan.FromMilliseconds(120);

    private TaskbarIcon? _trayIcon;
    private FlyoutControl? _flyout;
    private Window? _dialogOwnerWindow;
    private long _lastPopupCloseTicks;
    private bool _popupClosedHandlerAttached;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        ApplicationThemeManager.ApplySystemTheme(updateAccent: true);

        _dialogOwnerWindow = CreateDialogOwnerWindow();
        _flyout = new FlyoutControl(_dialogOwnerWindow);

        _trayIcon = new TaskbarIcon
        {
            Icon = TrayIconFactory.Create(),
            ToolTipText = "EqAPO Tray",
            TrayPopup = _flyout,
            PopupPlacement = PlacementMode.AbsolutePoint,
            PopupActivation = PopupActivationMode.None,
            ContextMenu = BuildContextMenu(),
        };
        _trayIcon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        _trayIcon.TrayPopupOpen += OnTrayPopupOpen;
        _trayIcon.ForceCreate();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        var quitItem = new MenuItem { Header = "退出" };
        quitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(quitItem);
        return menu;
    }

    private void OnTrayLeftMouseUp(object sender, RoutedEventArgs e)
    {
        if (_trayIcon is null)
        {
            return;
        }

        if (_trayIcon.TrayPopupResolved?.IsOpen == true)
        {
            _trayIcon.CloseTrayPopup();
            return;
        }

        if (_lastPopupCloseTicks > 0)
        {
            var sinceClose = TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastPopupCloseTicks);
            // Consume on first use: only the click that caused the close gets eaten.
            // A second rapid click is the user asking to reopen.
            _lastPopupCloseTicks = 0;
            if (sinceClose < ReopenSuppressWindow)
            {
                return;
            }
        }

        _trayIcon.ShowTrayPopup(GetFlyoutPosition());
    }

    private void OnTrayPopupOpen(object sender, RoutedEventArgs e)
    {
        if (_popupClosedHandlerAttached || _trayIcon?.TrayPopupResolved is not { } popup)
        {
            return;
        }

        popup.Closed += (_, _) => _lastPopupCloseTicks = Environment.TickCount64;
        _popupClosedHandlerAttached = true;
    }

    private DrawingPoint GetFlyoutPosition()
    {
        var popupSize = MeasureFlyout();
        if (!TryGetAnchorBounds(out var anchor))
        {
            return DrawingPoint.Empty;
        }

        const double gap = 8;
        const double edgePadding = 6;

        var left = anchor.Left + (anchor.Width - popupSize.Width) / 2;
        var top = anchor.Top - popupSize.Height - gap;

        if (top < anchor.WorkTop + edgePadding)
        {
            top = anchor.Bottom + gap;
        }

        left = Clamp(left, anchor.WorkLeft + edgePadding, anchor.WorkRight - popupSize.Width - edgePadding);
        top = Clamp(top, anchor.WorkTop + edgePadding, anchor.WorkBottom - popupSize.Height - edgePadding);

        return new DrawingPoint((int)Math.Round(left), (int)Math.Round(top));
    }

    private bool TryGetAnchorBounds(out TrayPopupPositioner.ScreenBounds anchor)
    {
        if (_trayIcon is not null && TrayPopupPositioner.TryGetTrayIconBounds(_trayIcon, out anchor))
        {
            return true;
        }

        return TrayPopupPositioner.TryGetCursorFallbackBounds(out anchor);
    }

    private Size MeasureFlyout()
    {
        if (_flyout is null)
        {
            return new Size(340, 180);
        }

        _flyout.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var width = _flyout.ActualWidth > 0 ? _flyout.ActualWidth : _flyout.DesiredSize.Width;
        var height = _flyout.ActualHeight > 0 ? _flyout.ActualHeight : _flyout.DesiredSize.Height;

        return new Size(width, height);
    }

    private static double Clamp(double value, double min, double max)
    {
        return max < min ? min : Math.Min(Math.Max(value, min), max);
    }

    private static Window CreateDialogOwnerWindow()
    {
        // Common dialogs need an owner that outlives H.NotifyIcon's transient TrayPopup window.
        var window = new Window
        {
            Width = 0,
            Height = 0,
            Left = -32000,
            Top = -32000,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
        };

        _ = new WindowInteropHelper(window).EnsureHandle();
        return window;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _dialogOwnerWindow?.Close();
    }
}
