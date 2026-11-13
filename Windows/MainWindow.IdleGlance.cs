using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VNotch.Controls;
using VNotch.Controllers;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

public partial class MainWindow
{
    #region Idle notch: slim, with the time and the weather

    // The resting width with nothing live; music, Claude Code and countdowns get the full width back.
    private const double IdleCompactWidth = 150;
    private static readonly TimeSpan IdleWeatherMaxAge = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan IdleWeatherTooOld = TimeSpan.FromHours(3);
    private static readonly TimeSpan IdleSettleRetry = TimeSpan.FromMilliseconds(700);

    private DispatcherTimer? _idleClockTimer;
    private DispatcherTimer? _idleSettleTimer;
    private bool _idleGlanceShown;
    private int _idleGlanceToken;
    private bool _idleGlanceHooked;

    /// <summary>Nothing live in the closed notch: no music, no Claude Code, no countdown.</summary>
    private bool IsIdleShape =>
        _settings?.SlimIdleNotch == true && !_isMusicCompactMode && _mainActivity == LiveActivity.None && !IsCountdownLive;

    /// <summary>Slims the closed notch when nothing is live and widens it again when something is.</summary>
    private void UpdateIdleShape()
    {
        if (_cleanedUp || _settings == null || IdleGlance == null) return;
        HookIdleGlance();
        _collapsedWidth = GetCollapsedWidth();
        ShowIdleGlance(IsIdleShape);
        SettleIdleWidth();
    }

    private void SettleIdleWidth()
    {
        _idleSettleTimer?.Stop();
        // Opening, closing, pills and the volume HUD all end on the resting width themselves.
        if (_isExpanded || _isMusicExpanded) return;
        if (_isAnimating || _isGreetingActive || IsCountdownCompletionVisualActive ||
            _compactPillArbiter.ActiveSlot != CompactPillSlot.None)
        {
            _idleSettleTimer ??= CreateIdleSettleTimer();
            _idleSettleTimer.Start();
            return;
        }
        double current = NotchBorder.ActualWidth;
        if (current <= 0 || Math.Abs(current - _collapsedWidth) < 0.5) return;
        bool widening = _collapsedWidth > current;
        AnimateCompactWidth(_collapsedWidth, TimeSpan.FromMilliseconds(widening ? 480 : 560),
            widening ? _easeSoftSpring : _easeExpOut6, 0);
        if (_privacyIndicatorsVisible) UpdatePrivacyDotPosition(animate: true);
    }

    private DispatcherTimer CreateIdleSettleTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = IdleSettleRetry };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_cleanedUp) SettleIdleWidth();
        };
        return timer;
    }

    /// <summary>The idle clock slides away before live content takes its place.</summary>
    private void ShowIdleGlance(bool show)
    {
        if (show == _idleGlanceShown) return;
        _idleGlanceShown = show;
        int token = ++_idleGlanceToken;
        double opacity = IdleGlance.Opacity;
        double shift = IdleGlanceShift.Y;
        IdleGlance.BeginAnimation(OpacityProperty, null);
        IdleGlanceShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        if (show)
        {
            RefreshIdleClock(animate: false);
            UpdateIdleWeather();
            IdleGlance.Visibility = Visibility.Visible;
            IdleGlanceShift.Y = 0;
            if (AnimationConfig.ReduceMotion)
            {
                IdleGlance.Opacity = 1;
                return;
            }
            // In once the notch has slimmed down.
            IdleGlance.Opacity = 0;
            IdleGlance.BeginAnimation(OpacityProperty, MakeAnim(0d, 1d, TimeSpan.FromMilliseconds(320), _easeQuadOut, TimeSpan.FromMilliseconds(200)));
            IdleGlanceShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                MakeAnim(5d, 0d, TimeSpan.FromMilliseconds(520), _easeSoftSpring, TimeSpan.FromMilliseconds(200)));
        }
        else
        {
            // Out before the live content takes the space.
            if (AnimationConfig.ReduceMotion)
            {
                IdleGlance.Opacity = 0;
                IdleGlance.Visibility = Visibility.Collapsed;
                return;
            }
            IdleGlance.Opacity = opacity;
            var duration = TimeSpan.FromMilliseconds(160);
            var fade = MakeAnim(opacity, 0d, duration, _easeQuadOut);
            fade.Completed += (_, _) =>
            {
                if (token == _idleGlanceToken && !_idleGlanceShown)
                    IdleGlance.Visibility = Visibility.Collapsed;
            };
            IdleGlance.BeginAnimation(OpacityProperty, fade);
            IdleGlanceShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                MakeAnim(shift, -4d, duration, _easeQuadOut));
        }
    }

    private void HookIdleGlance()
    {
        if (_idleGlanceHooked) return;
        _idleGlanceHooked = true;
        // The clock only ticks while it is on screen; hidden (expanded, fullscreen) nothing runs.
        IdleGlance.IsVisibleChanged += (_, _) =>
        {
            if (_cleanedUp) return;
            if (IdleGlance.IsVisible)
            {
                RefreshIdleClock(animate: false);
                ScheduleIdleClock();
                RefreshIdleWeatherIfStale();
            }
            else
            {
                _idleClockTimer?.Stop();
            }
        };
    }

    private void ScheduleIdleClock()
    {
        _idleClockTimer ??= CreateIdleClockTimer();
        var now = DateTime.Now;
        _idleClockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _idleClockTimer.Start();
    }

    private DispatcherTimer CreateIdleClockTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_cleanedUp || !IdleGlance.IsVisible) return;
            RefreshIdleClock(animate: true);
            RefreshIdleWeatherIfStale();
            ScheduleIdleClock();
        };
        return timer;
    }

    private void RefreshIdleClock(bool animate)
    {
        string text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
        if (IdleClock.Text == text) return;
        IdleClock.Text = text;
        if (!animate || AnimationConfig.ReduceMotion) return;
        IdleClock.BeginAnimation(OpacityProperty, MakeAnim(0.25d, 1d, TimeSpan.FromMilliseconds(380), _easeQuadOut));
    }

    private void RefreshIdleWeatherIfStale()
    {
        if (_settings == null || !NetworkPrivacy.Allows(_settings, NetworkFeature.Weather)) return;
        if (_weatherCache is { } cached && DateTime.Now - cached.FetchedAt < IdleWeatherMaxAge) return;
        GetWeatherAsync(IdleWeatherMaxAge, CancellationToken.None).SafeFireAndForget("WEATHER-IDLE");
    }

    /// <summary>The weather beside the clock; without a recent forecast the clock sits alone in the middle.</summary>
    private void UpdateIdleWeather()
    {
        if (IdleWeather == null) return;
        bool allowed = _settings != null && NetworkPrivacy.Allows(_settings, NetworkFeature.Weather);
        var forecast = allowed ? _weatherCache : null;
        if (forecast == null || DateTime.Now - forecast.FetchedAt > IdleWeatherTooOld)
        {
            IdleWeather.Visibility = Visibility.Collapsed;
            IdleClock.HorizontalAlignment = HorizontalAlignment.Center;
            return;
        }
        bool wasShown = IdleWeather.Visibility == Visibility.Visible;
        IdleClock.HorizontalAlignment = HorizontalAlignment.Left;
        IdleWeatherIcon.Content = WeatherIcons.Create(forecast.Now.Code, forecast.Now.IsDay, 15);
        IdleWeatherTemp.Text = WeatherView.Degrees(forecast.Now.Temperature);
        IdleWeather.Visibility = Visibility.Visible;
        if (!wasShown && _idleGlanceShown && !AnimationConfig.ReduceMotion)
            IdleWeather.BeginAnimation(OpacityProperty, MakeAnim(0d, 1d, TimeSpan.FromMilliseconds(360), _easeQuadOut));
    }

    #endregion
}
