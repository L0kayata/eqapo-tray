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
    private TaskbarThemeWatcher? _themeWatcher;
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
                    new PopupMenuItem(Strings.Get("TrayMenuExit"), (_, _) => QuitApplication()),
                },
            },
        };
        _trayIcon.MessageWindow.MouseEventReceived += OnTrayMouseEvent;

        // The icon follows the gain shown in the flyout. Reading the config
        // now, before Create(), means NIM_ADD already carries an image.
        var iconSwitcher = new TrayIconSwitcher(_trayIcon);
        _flyoutWindow.GainChanged += iconSwitcher.SetGain;
        _flyoutWindow.RefreshFromDisk();

        // These fire on the tray / thread-pool threads; marshal like clicks.
        _trayIcon.MessageWindow.DpiChanged += (_, _) => _dispatcherQueue?.TryEnqueue(iconSwitcher.RefreshIconSize);
        _trayIcon.MessageWindow.TaskbarCreated += (_, _) => _dispatcherQueue?.TryEnqueue(iconSwitcher.Reset);
        _themeWatcher = new TaskbarThemeWatcher(() => _dispatcherQueue?.TryEnqueue(iconSwitcher.RefreshTheme));

        _trayIcon.Create();

        // Previously implied by H.NotifyIcon.WinUI's TaskbarIcon.ForceCreate().
        EfficiencyModeUtilities.SetEfficiencyMode(true);
    }

    private void OnTrayMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
    {
        // A quick second click arrives from the shell as WM_LBUTTONDBLCLK, and
        // H.NotifyIcon then swallows the mouse-up that follows it, so the
        // double-click event has to count as a click too.
        if (e.MouseEvent is MouseEvent.IconLeftMouseUp or MouseEvent.IconLeftDoubleClick)
        {
            _dispatcherQueue?.TryEnqueue(ToggleFlyout);
        }
    }

    private void QuitApplication()
    {
        try
        {
            _themeWatcher?.Dispose();
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

        // A click on the tray icon while the flyout is open first trips the
        // flyout's outside-click hook on mouse-down, which usually hides it
        // before this handler runs. Consume that record on every toggle, not
        // just when it suppresses a reopen: a leftover record would swallow the
        // next quick click too.
        var justDismissed = _flyoutWindow.ConsumeRecentOutsideDismiss();

        if (_flyoutWindow.IsOpen)
        {
            _flyoutWindow.HideFlyout();
            return;
        }

        // The hook already closed it for this click: that was the "close" half
        // of the toggle, so don't reopen.
        if (justDismissed)
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
