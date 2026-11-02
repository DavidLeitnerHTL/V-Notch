using System;
using System.Windows.Input;
using System.Windows.Threading;
using VNotch.Models;
using VNotch.Services;

namespace VNotch;

public partial class MainWindow
{
    #region Weather tab

    // Like the controls and Pi tabs: wide enough for the sky beside the 24-hour curve.
    private const double WeatherViewWidth = 720;
    private const double WeatherViewHeight = 372;
    private static readonly TimeSpan WeatherPrefetchDelay = TimeSpan.FromSeconds(8);

    private bool _isWeatherView;
    private WeatherService? _weatherForecast;
    private DispatcherTimer? _weatherPrefetchTimer;

    private void InitializeWeather()
    {
        _weatherForecast = new WeatherService();
        var service = _weatherForecast;
        // The place is the one from the weather settings (empty: found from the IP address).
        WeatherPanel.Attach(
            token => service.GetForecastAsync(_settings?.ManualCity, token),
            () => _settings != null && NetworkPrivacy.Allows(_settings, NetworkFeature.Weather));

        // One quiet read after startup, so the first open shows the sky instead of loading it.
        _weatherPrefetchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = WeatherPrefetchDelay };
        _weatherPrefetchTimer.Tick += (_, _) =>
        {
            _weatherPrefetchTimer?.Stop();
            if (_cleanedUp || _settings == null) return;
            if (!(_settings.VisibleNavTabs ?? "").Contains("Weather", StringComparison.OrdinalIgnoreCase)) return;
            if (NetworkPrivacy.Allows(_settings, NetworkFeature.Weather)) WeatherPanel.Prefetch();
        };
        _weatherPrefetchTimer.Start();
    }

    private void DisposeWeather()
    {
        _weatherPrefetchTimer?.Stop();
        WeatherPanel.Dispose();
        _weatherForecast?.Dispose();
    }

    private void WeatherIconButton_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_isWeatherView) NavigateToNotchView(NotchView.Weather);
    }

    #endregion
}
