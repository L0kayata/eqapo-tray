using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private bool _isDraggingSliderTrack;
    private bool _suppressEvents;

    public FlyoutControl(Window dialogOwner)
    {
        _dialogOwner = dialogOwner;
        InitializeComponent();
        AttachGainSliderTrackDragHandlers();

        _writeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _writeTimer.Tick += OnWriteTimerTick;
    }

    private void AttachGainSliderTrackDragHandlers()
    {
        GainSlider.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(GainSlider_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        GainSlider.AddHandler(
            UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(GainSlider_PreviewMouseMove),
            handledEventsToo: true);
        GainSlider.AddHandler(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(GainSlider_PreviewMouseLeftButtonUp),
            handledEventsToo: true);
        GainSlider.AddHandler(
            UIElement.LostMouseCaptureEvent,
            new MouseEventHandler(GainSlider_LostMouseCapture),
            handledEventsToo: true);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFromDisk();
        Focusable = true;
        Focus();
        PlayEnterAnimation();
    }

    private void PlayEnterAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(500));

        EnterTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation { From = 12, To = 0, Duration = duration, EasingFunction = ease });

        FlyoutRoot.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease });
    }

    private void GainSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || IsFromThumb(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (!TryMoveSliderToMousePoint(slider, e))
        {
            return;
        }

        _isDraggingSliderTrack = true;
        slider.Focus();
        slider.CaptureMouse();
        e.Handled = true;
    }

    private void GainSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingSliderTrack || sender is not Slider slider)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            FinishSliderTrackDrag(slider);
            return;
        }

        TryMoveSliderToMousePoint(slider, e);
        e.Handled = true;
    }

    private void GainSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingSliderTrack || sender is not Slider slider)
        {
            return;
        }

        TryMoveSliderToMousePoint(slider, e);
        FinishSliderTrackDrag(slider);
        e.Handled = true;
    }

    private void GainSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isDraggingSliderTrack = false;
    }

    private static bool TryMoveSliderToMousePoint(Slider slider, MouseEventArgs e)
    {
        if (slider.Template.FindName("PART_Track", slider) is not Track track)
        {
            return false;
        }

        if (!TryGetSliderValueFromPoint(slider, track, e.GetPosition(track), out var value))
        {
            return false;
        }

        slider.Value = SnapSliderValue(slider, value);
        return true;
    }

    private static bool TryGetSliderValueFromPoint(Slider slider, Track track, Point point, out double value)
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
            if (track.ActualHeight <= 0)
            {
                return false;
            }

            ratio = 1 - point.Y / track.ActualHeight;
        }
        else
        {
            if (track.ActualWidth <= 0)
            {
                return false;
            }

            ratio = point.X / track.ActualWidth;
        }

        if (slider.IsDirectionReversed)
        {
            ratio = 1 - ratio;
        }

        ratio = Math.Clamp(ratio, 0, 1);
        value = slider.Minimum + ratio * range;
        return true;
    }

    private static double SnapSliderValue(Slider slider, double value)
    {
        value = Math.Clamp(value, slider.Minimum, slider.Maximum);

        if (!slider.IsSnapToTickEnabled)
        {
            return value;
        }

        if (slider.Ticks.Count > 0)
        {
            var nearest = slider.Ticks[0];
            var nearestDistance = Math.Abs(value - nearest);

            foreach (var tick in slider.Ticks)
            {
                var distance = Math.Abs(value - tick);
                if (distance < nearestDistance)
                {
                    nearest = tick;
                    nearestDistance = distance;
                }
            }

            return Math.Clamp(nearest, slider.Minimum, slider.Maximum);
        }

        if (slider.TickFrequency <= 0)
        {
            return value;
        }

        var tickCount = Math.Round((value - slider.Minimum) / slider.TickFrequency);
        return Math.Clamp(slider.Minimum + tickCount * slider.TickFrequency, slider.Minimum, slider.Maximum);
    }

    private void FinishSliderTrackDrag(Slider slider)
    {
        _isDraggingSliderTrack = false;

        if (slider.IsMouseCaptured)
        {
            slider.ReleaseMouseCapture();
        }
    }

    private static bool IsFromThumb(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Thumb)
            {
                return true;
            }

            source = source is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source)
                : source switch
                {
                    FrameworkElement element => element.Parent,
                    FrameworkContentElement contentElement => contentElement.Parent,
                    _ => null,
                };
        }

        return false;
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
