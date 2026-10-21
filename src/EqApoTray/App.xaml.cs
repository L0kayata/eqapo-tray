using EqApoTray.Services;
using H.NotifyIcon.Core;
using H.NotifyIcon.EfficiencyMode;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace EqApoTray;

public partial class App : Application
{
    // Per-session single-instance guard. Without it, launching the exe while
    // the autostart copy is running adds a second tray icon and a second
    // writer to config.txt. Field-held so it lives as long as the process.
    private readonly Mutex _singleInstance;
    private TrayIconWithContextMenu? _trayIcon;
    private FlyoutWindow? _flyoutWindow;
    private DispatcherQueue? _dispatcherQueue;

    public App()
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\EqApoTray", out var createdNew);
        if (!createdNew)
        {
            Environment.Exit(0);
        }

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _flyoutWindow = new FlyoutWindow();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Core (non-WinUI) tray icon. TrayIconWithContextMenu.Create() runs its
        // hidden message window on a background thread of its own, so tray
        // callbacks arrive off the UI thread and must be marshalled back before
        // touching any WinUI object (RPC_E_WRONG_THREAD otherwise). The
        // right-click menu is a native Win32 popup menu shown by the library.
        _trayIcon = new TrayIconWithContextMenu
        {
            ToolTip = "EqAPO Tray",
            ContextMenu = new PopupMenu
            {
                Items =
                {
                    new PopupMenuItem("退出", (_, _) => QuitApplication()),
                },
            },
        };
        _trayIcon.MessageWindow.MouseEventReceived += OnTrayMouseEvent;
        _trayIcon.Create();

        // Previously implied by H.NotifyIcon.WinUI's TaskbarIcon.ForceCreate().
        EfficiencyModeUtilities.SetEfficiencyMode(true);
    }

    private void OnTrayMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
    {
        if (e.MouseEvent == MouseEvent.IconLeftMouseUp)
        {
            _dispatcherQueue?.TryEnqueue(ToggleFlyout);
        }
    }

    private void QuitApplication()
    {
        try
        {
            _trayIcon?.Dispose();
        }
        catch
        {
            // Best-effort cleanup; we're tearing down the process anyway.
        }
        Environment.Exit(0);
    }

    private void ToggleFlyout()
    {
        if (_flyoutWindow is null)
        {
            return;
        }

        if (_flyoutWindow.IsOpen)
        {
            _flyoutWindow.HideFlyout();
            return;
        }

        // A click on the tray icon while the flyout is open first trips the
        // flyout's outside-click hook on mouse-down, which hides it before this
        // mouse-up handler runs. Treat that as the "close" half of the toggle
        // rather than reopening.
        if (_flyoutWindow.WasJustDismissedByOutsideClick)
        {
            return;
        }

        var anchor = TryGetAnchorBounds();
        _flyoutWindow.ShowAt(anchor);
    }

    private TrayPopupPositioner.ScreenBounds TryGetAnchorBounds()
    {
        if (_trayIcon is not null && TrayPopupPositioner.TryGetTrayIconBounds(_trayIcon, out var anchor))
        {
            return anchor;
        }

        if (TrayPopupPositioner.TryGetCursorFallbackBounds(out anchor))
        {
            return anchor;
        }

        return default;
    }
}
