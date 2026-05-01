using System.Windows;
using System.Windows.Controls;
using EqApoTray.Services;
using H.NotifyIcon;
using Wpf.Ui.Appearance;

namespace EqApoTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private FlyoutControl? _flyout;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        ApplicationThemeManager.ApplySystemTheme(updateAccent: true);

        _flyout = new FlyoutControl();

        _trayIcon = new TaskbarIcon
        {
            IconSource = TrayIconFactory.Create(),
            ToolTipText = "EqAPO Tray",
            TrayPopup = _flyout,
            MenuActivation = PopupActivationMode.RightClick,
            PopupActivation = PopupActivationMode.LeftClick,
            ContextMenu = BuildContextMenu(),
        };
        _trayIcon.ForceCreate();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "打开控制面板" };
        openItem.Click += (_, _) => _trayIcon?.ShowTrayPopup();
        var quitItem = new MenuItem { Header = "退出" };
        quitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quitItem);
        return menu;
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        _trayIcon?.Dispose();
    }
}
