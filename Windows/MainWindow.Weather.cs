using System;
using System.Threading;
using System.Threading.Tasks;
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
        // The tab asks for fresh data (a minute old at most); the idle notch is happy with half an hour.
        WeatherPanel.Attach(
            token => GetWeatherAsync(TimeSpan.FromMinutes(1), token),
            () => _settings != null && NetworkPrivacy.Allows(_settings, NetworkFeature.Weather));
        Dispatcher.BeginInvoke(DispatcherPriority.Background, UpdateIdleShape);

        // One quiet read after startup, so the first open shows the sky instead of loading it.
        _weatherPrefetchTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = WeatherPrefetchDelay };
        _weatherPrefetchTimer.Tick += (_, _) =>
        {
            _weatherPrefetchTimer?.Stop();
            if (_cleanedUp || _settings == null) return;
            UpdateIdleShape();
            if (!(_settings.VisibleNavTabs ?? "").Contains("Weather", StringComparison.OrdinalIgnoreCase)) return;
            if (NetworkPrivacy.Allows(_settings, NetworkFeature.Weather)) WeatherPanel.Prefetch();
        };
        _weatherPrefetchTimer.Start();
    }

    private WeatherForecast? _weatherCache;
    private Task<WeatherForecast?>? _weatherRequest;

    /// <summary>
    /// The forecast for the tab and the idle notch: reused while younger than <paramref name="maxAge"/>, and one
    /// request at a time. The place comes from the weather settings (empty: found from the IP address).
    /// </summary>
    private Task<WeatherForecast?> GetWeatherAsync(TimeSpan maxAge, CancellationToken token)
    {
        if (_weatherCache is { } cached && DateTime.Now - cached.FetchedAt < maxAge) return Task.FromResult<WeatherForecast?>(cached);
        if (_weatherRequest is { IsCompleted: false } running) return running;
        _weatherRequest = FetchWeatherAsync(token);
        return _weatherRequest;
    }

    private async Task<WeatherForecast?> FetchWeatherAsync(CancellationToken token)
    {
        var service = _weatherForecast;
        var result = service == null ? null : await service.GetForecastAsync(_settings?.ManualCity, token);
        if (result != null && !_cleanedUp)
        {
            _weatherCache = result;
            UpdateIdleWeather();
        }
        return result;
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
