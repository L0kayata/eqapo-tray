using System.Globalization;
using System.IO;
using EqApoTray.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace EqApoTray;

public partial class FlyoutControl : UserControl
{
    private static readonly Brush GreenBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43));
    private static readonly Brush RedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD1, 0x34, 0x38));

    private const string ConfigFileFilter =
        "Equalizer APO 配置文件 (*.txt)\0*.txt\0所有文件 (*.*)\0*.*\0";

    private readonly Settings _settings = SettingsStore.Load();
    private readonly DispatcherQueueTimer _writeTimer;
    private double _pendingValue;
    private bool _isDraggingSliderTrack;
    private bool _suppressEvents;

    public FlyoutControl()
    {
        InitializeComponent();

        var queue = DispatcherQueue.GetForCurrentThread();
        _writeTimer = queue.CreateTimer();
        _writeTimer.Interval = TimeSpan.FromMilliseconds(60);
        _writeTimer.IsRepeating = false;
        _writeTimer.Tick += OnWriteTimerTick;

        AttachGainSliderTrackDragHandlers();
    }

    public IntPtr DialogOwnerHwnd { get; set; }

    private void AttachGainSliderTrackDragHandlers()
    {
        GainSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(GainSlider_PointerPressed),
            handledEventsToo: true);
        GainSlider.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(GainSlider_PointerMoved),
            handledEventsToo: true);
        GainSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(GainSlider_PointerReleased),
            handledEventsToo: true);
        GainSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(GainSlider_PointerCaptureLost),
            handledEventsToo: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFromDisk();
    }

    public void RefreshFromDisk()
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

    private void GainSlider_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        var point = e.GetCurrentPoint(slider).Position;
        if (!TryComputeValueFromPoint(slider, point, out var value))
        {
            return;
        }

        slider.Value = SnapValue(slider, value);
        slider.Focus(FocusState.Pointer);
        slider.CapturePointer(e.Pointer);
        _isDraggingSliderTrack = true;
        e.Handled = true;
    }

    private void GainSlider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSliderTrack || sender is not Slider slider)
        {
            return;
        }

        var props = e.GetCurrentPoint(slider).Properties;
        if (!props.IsLeftButtonPressed)
        {
            FinishSliderTrackDrag(slider, e.Pointer);
            return;
        }

        var point = e.GetCurrentPoint(slider).Position;
        if (TryComputeValueFromPoint(slider, point, out var value))
        {
            slider.Value = SnapValue(slider, value);
        }

        e.Handled = true;
    }

    private void GainSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSliderTrack || sender is not Slider slider)
        {
            return;
        }

        var point = e.GetCurrentPoint(slider).Position;
        if (TryComputeValueFromPoint(slider, point, out var value))
        {
            slider.Value = SnapValue(slider, value);
        }

        FinishSliderTrackDrag(slider, e.Pointer);
        e.Handled = true;
    }

    private void GainSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSliderTrack = false;
    }

    private static bool TryComputeValueFromPoint(Slider slider, Point point, out double value)
    {
        value = default;

        var range = slider.Maximum - slider.Minimum;
        if (range <= 0)
        {
            return false;
        }

        double ratio;
        if (slider.Orientation == Orientation.Vertical)
        {
            if (slider.ActualHeight <= 0)
            {
                return false;
            }
            ratio = 1 - point.Y / slider.ActualHeight;
        }
        else
        {
            if (slider.ActualWidth <= 0)
            {
                return false;
            }
            ratio = point.X / slider.ActualWidth;
        }

        if (slider.IsDirectionReversed)
        {
            ratio = 1 - ratio;
        }

        ratio = Math.Clamp(ratio, 0, 1);
        value = slider.Minimum + ratio * range;
        return true;
    }

    private static double SnapValue(Slider slider, double value)
    {
        value = Math.Clamp(value, slider.Minimum, slider.Maximum);

        if (slider.SnapsTo != SliderSnapsTo.StepValues || slider.StepFrequency <= 0)
        {
            return value;
        }

        var steps = Math.Round((value - slider.Minimum) / slider.StepFrequency);
        return Math.Clamp(slider.Minimum + steps * slider.StepFrequency, slider.Minimum, slider.Maximum);
    }

    private void FinishSliderTrackDrag(Slider slider, Pointer pointer)
    {
        _isDraggingSliderTrack = false;
        slider.ReleasePointerCapture(pointer);
    }

    private void GainSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateGainLabel(e.NewValue);
        // Tray icon must reflect the visible value even when _suppressEvents is
        // set (RefreshFromDisk pushes the disk value into the slider with that
        // flag on); icon swap is sub-ms and does not touch disk.
        ((App)Application.Current).NotifyDbChanged(e.NewValue);
        if (_suppressEvents) return;

        _pendingValue = e.NewValue;
        _writeTimer.Stop();
        _writeTimer.Start();
    }

    private void OnWriteTimerTick(DispatcherQueueTimer sender, object args)
    {
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
        ToolTipService.SetToolTip(StatusDot, ok ? "配置文件已加载" : "无法读写配置文件（路径不存在或权限不足）");
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
            _suppressEvents = true;
            StartupCheck.IsChecked = StartupService.IsEnabled();
            _suppressEvents = false;
            _ = ShowErrorAsync("无法修改开机自启", ex.Message);
        }
    }

    private void BrowseConfig_Click(object sender, RoutedEventArgs e)
    {
        var initialDir = File.Exists(_settings.ConfigPath)
            ? Path.GetDirectoryName(_settings.ConfigPath)
            : null;

        var path = Win32FileDialog.PickFile(
            DialogOwnerHwnd,
            "选择 Equalizer APO 配置文件",
            ConfigFileFilter,
            initialDir);

        if (path is null)
        {
            return;
        }

        _settings.ConfigPath = path;
        SettingsStore.Save(_settings);
        RefreshFromDisk();
    }

    private async System.Threading.Tasks.Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
