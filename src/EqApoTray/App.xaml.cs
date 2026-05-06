using EqApoTray.Services;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EqApoTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private FlyoutWindow? _flyoutWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _flyoutWindow = new FlyoutWindow();

        // ContextMenuMode.PopupMenu renders the right-click menu as a native
        // Win32 popup menu. The default SecondWindow mode would need the
        // TaskbarIcon to live in a XAML tree with a valid XamlRoot — we create
        // it programmatically with none, so MenuFlyoutItem clicks never route.
        // PopupMenu mode also dispatches via Command (not Click); see
        // BuildContextMenu.
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "EqAPO Tray",
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = BuildContextMenu(),
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(ToggleFlyout),
        };
        _trayIcon.ForceCreate();
    }

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();
        // ContextMenuMode.PopupMenu translates each MenuFlyoutItem into a Win32
        // popup menu item. The library raises the *Command*, not the *Click*
        // event — confirmed in H.NotifyIcon source
        // (TaskbarIcon.ContextMenu.WinRT.PopupMenu.cs PopulateMenu). Wiring
        // Click here would silently do nothing.
        var quitItem = new MenuFlyoutItem
        {
            Text = "退出",
            Command = new RelayCommand(QuitApplication),
        };
        menu.Items.Add(quitItem);
        return menu;
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

    private sealed partial class RelayCommand(Action execute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
