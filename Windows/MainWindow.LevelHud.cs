using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VNotch.Controllers;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

public partial class MainWindow
{
    #region System volume and brightness in the volume HUD

    // The HUD that comes up when scrolling on the notch during music also shows the system volume (volume keys,
    // the Windows mixer) and the screen brightness, with or without music. Brightness wears a sun and a warm tint.
    private enum LevelHudKind
    {
        AppVolume,
        SystemVolume,
        Brightness
    }

    private static readonly Color BrightnessHudTint = Color.FromRgb(0xE2, 0xBE, 0x86);

    private LevelHudKind _levelHudKind = LevelHudKind.AppVolume;
    private bool _levelHudMuted;
    // Without music the HUD covers the resting content (time, weather, Claude), which comes back afterwards.
    private bool _levelHudOverIdle;
    private MasterVolumeWatcher? _masterVolumeWatcher;
    private BrightnessWatcher? _brightnessWatcher;
    private readonly CoalescingActionQueue _levelWrites = new();

    private void InitializeLevelHud()
    {
        _masterVolumeWatcher = new MasterVolumeWatcher();
        _masterVolumeWatcher.Changed += (volume, muted) => Dispatcher.BeginInvoke(() =>
            OnSystemLevelChanged(LevelHudKind.SystemVolume, volume, muted));
        _masterVolumeWatcher.Start();

        _brightnessWatcher = new BrightnessWatcher();
        _brightnessWatcher.Changed += percent => Dispatcher.BeginInvoke(() =>
            OnSystemLevelChanged(LevelHudKind.Brightness, percent / 100f, muted: false));
        _brightnessWatcher.Start();
    }

    private void DisposeLevelHud()
    {
        _masterVolumeWatcher?.Dispose();
        _brightnessWatcher?.Dispose();
        _levelWrites.Dispose();
    }

    private void OnSystemLevelChanged(LevelHudKind kind, float level, bool muted)
    {
        if (_cleanedUp || _settings == null || _isGreetingActive || !CanShowClaudeAttention) return;
        // While dragging the HUD its own writes come back here.
        if (_isDraggingVolumeIndicator) return;
        // The scroll HUD shows the player's volume right now; leave it to that.
        if (kind == LevelHudKind.SystemVolume && _isVolumeIndicatorActive && _levelHudKind == LevelHudKind.AppVolume) return;
        _levelHudMuted = muted;
        ShowVolumeIndicator(muted ? 0f : level, kind);
    }

    /// <summary>Dragging the HUD sets what it shows: the system volume or the brightness.</summary>
    private void WriteSystemLevel(float level)
    {
        switch (_levelHudKind)
        {
            case LevelHudKind.SystemVolume when _masterVolumeWatcher is { } volume:
                if (level > 0) _levelHudMuted = false;
                _levelWrites.Post("volume", () => volume.SetVolume(level));
                break;
            case LevelHudKind.Brightness when _brightnessWatcher is { } brightness:
                int percent = (int)Math.Round(level * 100);
                _levelWrites.Post("brightness", () => brightness.SetBrightness(percent));
                break;
        }
    }

    /// <summary>The music's colour for volume while music shows, warm sand for brightness, else white.</summary>
    private (Color Color, bool Has) EffectiveHudTint() => _levelHudKind switch
    {
        LevelHudKind.Brightness => (BrightnessHudTint, true),
        LevelHudKind.SystemVolume when !_isMusicCompactMode => (Colors.White, false),
        _ => (_hasVolumeHudMediaTint ? _volumeHudMediaColor : Colors.White, _hasVolumeHudMediaTint)
    };

    private bool IsLevelHudMuted(double volume) => volume <= 0.0001 && _levelHudKind switch
    {
        LevelHudKind.SystemVolume => _levelHudMuted,
        LevelHudKind.AppVolume => _viewModel?.AudioMixer?.IsMuted == true,
        _ => false
    };

    /// <summary>Speaker or sun, and the glow in the HUD's tint.</summary>
    private void ApplyLevelHudLook()
    {
        bool sun = _levelHudKind == LevelHudKind.Brightness;
        var speaker = sun ? Visibility.Collapsed : Visibility.Visible;
        VolumeIndicatorSpeakerCone.Visibility = speaker;
        VolumeIndicatorWave1.Visibility = speaker;
        VolumeIndicatorWave2.Visibility = speaker;
        if (sun)
        {
            VolumeIndicatorMuteSlash.Visibility = Visibility.Collapsed;
            VolumeIndicatorMuteSlashGap.Visibility = Visibility.Collapsed;
        }
        VolumeIndicatorSun.Visibility = sun ? Visibility.Visible : Visibility.Collapsed;
        VolumeIndicatorSunRays.Visibility = sun ? Visibility.Visible : Visibility.Collapsed;

        var (tint, has) = EffectiveHudTint();
        Color glow = has ? tint : Colors.White;
        VolumeIndicatorTicksGlow.Color = glow;
        VolumeIndicatorIconGlow.Color = glow;
    }

    /// <summary>The sun's rays grow and brighten with the level, and the icon hops when a tick lights or goes out.</summary>
    private void UpdateSunIconState(double level, Color accent, bool animate)
    {
        TintPath(VolumeIndicatorSun, accent, fill: true);
        TintPath(VolumeIndicatorSunRays, accent, fill: false);

        double t = Math.Clamp(level, 0, 1);
        double rayScale = 0.78 + 0.22 * t;
        double current = VolumeIndicatorSunRaysScale.ScaleX;
        VolumeIndicatorSunRaysScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        VolumeIndicatorSunRaysScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        VolumeIndicatorSunRaysScale.ScaleX = VolumeIndicatorSunRaysScale.ScaleY = rayScale;
        VolumeIndicatorSunRays.Opacity = 0.5 + 0.5 * t;

        if (!animate || AnimationConfig.ReduceMotion) return;
        if (Math.Abs(current - rayScale) > 0.005)
        {
            var grow = MakeAnim(current, rayScale, _dur250, _easeSoftSpring);
            grow.FillBehavior = FillBehavior.Stop;
            VolumeIndicatorSunRaysScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            VolumeIndicatorSunRaysScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
        var pop = new DoubleAnimationUsingKeyFrames();
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.16, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(75)), _easeQuadOut));
        pop.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), _easeSoftSpring));
        Timeline.SetDesiredFrameRate(pop, AnimationConfig.TargetFps);
        VolumeIndicatorIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        VolumeIndicatorIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private static void TintPath(Shape path, Color color, bool fill)
    {
        var current = fill ? path.Fill : path.Stroke;
        if (current is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }
        if (fill) path.Fill = new SolidColorBrush(color);
        else path.Stroke = new SolidColorBrush(color);
    }

    /// <summary>As wide as the HUD over music, also when the notch rests slim.</summary>
    private double LevelHudWidth(double transitionWidth)
    {
        if (_isMusicCompactMode) return transitionWidth;
        double resting = _settings.EnableDynamicIslandMode ? _settings.DynamicIslandWidth : _settings.Width;
        return Math.Max(transitionWidth, resting + VolumeHudExtraWidth);
    }

    private void HideIdleUnderHud()
    {
        _levelHudOverIdle = true;
        int token = _volumeIndicatorToken;
        CollapsedContent.BeginAnimation(OpacityProperty, null);
        var fade = MakeAnim(CollapsedContent.Opacity, 0.0, _dur150, _easeQuadOut);
        fade.Completed += (_, _) =>
        {
            if (_isVolumeIndicatorActive && _levelHudOverIdle && !IsCompactSlotStale(token))
                CollapsedContent.Visibility = Visibility.Collapsed;
        };
        CollapsedContent.BeginAnimation(OpacityProperty, fade);
    }

    private void RestoreIdleUnderHud()
    {
        _levelHudOverIdle = false;
        if (_isMusicCompactMode && _currentMediaInfo != null) return;
        CollapsedContent.Visibility = Visibility.Visible;
        CollapsedContent.BeginAnimation(OpacityProperty, MakeCompactContentRestore());
    }

    /// <summary>The HUD was dropped at once (the notch opens, another alert takes over).</summary>
    private void ReleaseIdleUnderHud()
    {
        if (!_levelHudOverIdle) return;
        _levelHudOverIdle = false;
        // Whoever took the closed notch, or the opening notch, decides what shows now.
        if (_isExpanded || _isAnimating || _compactPillArbiter.ActiveSlot != CompactPillSlot.None) return;
        if (_isMusicCompactMode && _currentMediaInfo != null) return;
        CollapsedContent.BeginAnimation(OpacityProperty, null);
        CollapsedContent.Opacity = 1;
        CollapsedContent.Visibility = Visibility.Visible;
    }

    #endregion
}
