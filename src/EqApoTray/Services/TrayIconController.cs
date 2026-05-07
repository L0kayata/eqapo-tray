using System;
using System.IO;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace EqApoTray.Services;

public sealed class TrayIconController : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly DispatcherQueue _ui;
    // Strong reference required: UISettings event subscription stops firing if
    // the instance is collected.
    private readonly UISettings _uiSettings;
    private readonly BitmapIconSource?[,] _cache = new BitmapIconSource?[2, 5];

    private TrayIconState _state = TrayIconState.Neutral;
    private bool _systemLight = true;
    private bool _initialized;

    public TrayIconController(TaskbarIcon icon, DispatcherQueue dispatcher)
    {
        _icon = icon;
        _ui = dispatcher;
        _uiSettings = new UISettings();
    }

    public void Initialize(double initialDb)
    {
        _state = TrayIconStateClassifier.Classify(initialDb);
        _systemLight = ReadSystemLight();
        _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
        _initialized = true;
        Apply();
    }

    public void OnDbChanged(double db)
    {
        if (!_initialized) return;
        var next = TrayIconStateClassifier.Classify(db);
        if (next == _state) return;
        _state = next;
        Apply();
    }

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnSystemColorValuesChanged;
    }

    private void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        // Fires on a non-UI thread; marshal back before touching XAML state.
        _ui.TryEnqueue(() =>
        {
            var light = ReadSystemLight();
            if (light == _systemLight) return;
            _systemLight = light;
            Apply();
        });
    }

    private bool ReadSystemLight()
    {
        var bg = _uiSettings.GetColorValue(UIColorType.Background);
        return (bg.R * 299 + bg.G * 587 + bg.B * 114) / 1000 > 128;
    }

    private void Apply()
    {
        var themeIdx = _systemLight ? 0 : 1;
        var stateIdx = (int)_state + 2;
        var source = _cache[themeIdx, stateIdx] ??= BuildSource(_systemLight, _state);
        _icon.IconSource = source;
    }

    private static BitmapIconSource BuildSource(bool light, TrayIconState state)
    {
        var themeDir = light ? "light" : "dark";
        var fileName = $"state-{(int)state}.ico";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", themeDir, fileName);
        return new BitmapIconSource
        {
            UriSource = new Uri(path),
            ShowAsMonochrome = false,
        };
    }
}
