using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using EqApoTray.Services;
using Microsoft.Win32;

namespace EqApoTray;

public partial class FlyoutControl : UserControl
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xA0, 0x43));
    private static readonly Brush RedBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));

    private readonly Window _dialogOwner;
    private readonly Settings _settings = SettingsStore.Load();
    private readonly DispatcherTimer _writeTimer;
    private double _pendingValue;
    private bool _suppressEvents;

    public FlyoutControl(Window dialogOwner)
    {
        _dialogOwner = dialogOwner;
        InitializeComponent();

        _writeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _writeTimer.Tick += OnWriteTimerTick;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFromDisk();
        Focusable = true;
        Focus();
    }

    private void RefreshFromDisk()
    {
        _suppressEvents = true;
        try
        {
            StartupCheck.IsChecked = StartupService.IsEnabled();
            PathLabel.Text = _settings.ConfigPath;

            if (File.Exists(_settings.ConfigPath))
            {
                var current = EqApoConfig.Read(_settings.ConfigPath);
                GainSlider.Value = current;
                UpdateGainLabel(current);
                SetStatus(true);
            }
            else
            {
                GainSlider.Value = 0;
                UpdateGainLabel(0);
                SetStatus(false);
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void GainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateGainLabel(e.NewValue);
        if (_suppressEvents) return;

        _pendingValue = e.NewValue;
        _writeTimer.Stop();
        _writeTimer.Start();
    }

    private void OnWriteTimerTick(object? sender, EventArgs e)
    {
        _writeTimer.Stop();
        try
        {
            EqApoConfig.Write(_settings.ConfigPath, _pendingValue);
            SetStatus(true);
        }
        catch
        {
            SetStatus(false);
        }
    }

    private void UpdateGainLabel(double v)
    {
        GainLabel.Text = v.ToString("+0.0;-0.0;+0.0", CultureInfo.InvariantCulture) + " dB";
    }

    private void SetStatus(bool ok)
    {
        StatusDot.Fill = ok ? GreenBrush : RedBrush;
        StatusDot.ToolTip = ok ? "配置文件已加载" : "无法读写配置文件（路径不存在或权限不足）";
    }

    private void StartupCheck_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        try
        {
            StartupService.SetEnabled(StartupCheck.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_dialogOwner, ex.Message, "无法修改开机自启", MessageBoxButton.OK, MessageBoxImage.Warning);
            _suppressEvents = true;
            StartupCheck.IsChecked = StartupService.IsEnabled();
            _suppressEvents = false;
        }
    }

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择 Equalizer APO 配置文件",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = File.Exists(_settings.ConfigPath)
                ? Path.GetDirectoryName(_settings.ConfigPath)
                : null,
        };
        if (dlg.ShowDialog(_dialogOwner) == true)
        {
            _settings.ConfigPath = dlg.FileName;
            SettingsStore.Save(_settings);
            RefreshFromDisk();
        }
    }
}
