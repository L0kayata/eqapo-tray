using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using EqApoTray.Services;
using H.NotifyIcon;
using Wpf.Ui.Appearance;

namespace EqApoTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private FlyoutControl? _flyout;
    private Window? _dialogOwnerWindow;

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
            ContextMenu = BuildContextMenu(),
        };
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
