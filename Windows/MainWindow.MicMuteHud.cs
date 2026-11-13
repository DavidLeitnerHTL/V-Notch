using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

/// <summary>The mic dives out and returns struck through in rose when muted, or plain when switched back on.</summary>
public partial class MainWindow
{
    private static readonly Color MicMuteRose = Color.FromRgb(0xD2, 0x85, 0x7E);
    private MasterVolumeWatcher? _captureVolumeWatcher;
    private bool? _micMuteKnown;
    private bool _micMuteShown;
    private int _micDiveToken;

    internal bool MicMuteIsKnownMuted => _micMuteKnown == true;

    private void InitializeMicMuteHud()
    {
        _captureVolumeWatcher = new MasterVolumeWatcher(capture: true);
        _captureVolumeWatcher.Changed += (volume, muted) => Dispatcher.BeginInvoke(() => OnCaptureLevelChanged(volume, muted));
        _captureVolumeWatcher.SpeakerSwitched += (volume, muted) => Dispatcher.BeginInvoke(() => OnCaptureDefaultChanged(volume, muted));
        _captureVolumeWatcher.Start();
    }

    private void DisposeMicMuteHud()
    {
        _fakeMicMuteTimer?.Stop();
        _captureVolumeWatcher?.Dispose();
        CancelMicDive();
    }

    private void OnCaptureLevelChanged(float volume, bool muted)
    {
        if (_cleanedUp || MotionLabFakesMicMute) return;
        OnMicMuteChanged(muted);
    }

    // Initial binding and default-device switches establish the state without playing a mute moment.
    private void OnCaptureDefaultChanged(float volume, bool muted)
    {
        if (_cleanedUp || MotionLabFakesMicMute) return;
        _micMuteKnown = muted;
        _micMuteShown = muted;
        if (_isVolumeIndicatorActive && _levelHudKind == LevelHudKind.MicMute)
        {
            CancelMicDive();
            ApplyMicMuteFace(muted);
        }
    }

    private void OnMicMuteChanged(bool muted)
    {
        bool? previous = _micMuteKnown;
        _micMuteKnown = muted;
        if (previous == muted) return;
        if (previous == null || _cleanedUp || _settings == null || _isGreetingActive || !CanShowClaudeAttention)
        {
            CancelMicDive();
            _micMuteShown = muted;
            if (_isVolumeIndicatorActive && _levelHudKind == LevelHudKind.MicMute) ApplyMicMuteFace(muted);
            return;
        }

        _levelHudLabelTimer?.Stop();
        _levelHudLabelOverride = null;
        ShowLevelIndicator(0f, LevelHudKind.MicMute);
        if (_isVolumeIndicatorActive && _levelHudKind == LevelHudKind.MicMute) PlayMicDive(muted);
        else _micMuteShown = muted;
    }

    private void ApplyMicMuteFace(bool muted)
    {
        _micMuteShown = muted;
        var (accent, hasAccent) = EffectiveHudTint();
        Color tint = muted ? MicMuteRose : hasAccent ? accent : Colors.White;
        foreach (var child in VolumeIndicatorMic.Children)
            if (child is Shape path) TintPath(path, tint, fill: false);
        VolumeIndicatorMicSlash.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        VolumeIndicatorMicWord.Text = Loc.Get(muted ? "levelHud.micMuted" : "levelHud.micOn");
        VolumeIndicatorMicWord.Foreground = new SolidColorBrush(tint);
        VolumeIndicatorIconGlow.Color = tint;
    }

    private void CancelMicDive(bool reset = true)
    {
        ++_micDiveToken;
        // Preserve the visible values when a second toggle replaces an unfinished dive.
        double y = VolumeIndicatorMicShift.Y;
        double squash = VolumeIndicatorMicSquash.ScaleY;
        double opacity = VolumeIndicatorMic.Opacity;
        double wordOpacity = VolumeIndicatorMicWord.Opacity;
        VolumeIndicatorMicShift.BeginAnimation(TranslateTransform.YProperty, null);
        VolumeIndicatorMicSquash.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        VolumeIndicatorMic.BeginAnimation(OpacityProperty, null);
        VolumeIndicatorMicWord.BeginAnimation(OpacityProperty, null);
        VolumeIndicatorMicShift.Y = reset ? 0 : y;
        VolumeIndicatorMicSquash.ScaleY = reset ? 1 : squash;
        VolumeIndicatorMic.Opacity = reset ? 1 : opacity;
        VolumeIndicatorMicWord.Opacity = reset ? 1 : wordOpacity;
    }

    /// <summary>Fade the word with the dive, swap at the hidden midpoint, then bring the new mic face back from above.</summary>
    private void PlayMicDive(bool toMuted)
    {
        CancelMicDive(reset: AnimationConfig.ReduceMotion);
        if (AnimationConfig.ReduceMotion)
        {
            ApplyMicMuteFace(toMuted);
            return;
        }

        int token = _micDiveToken;
        var dive = new Duration(TimeSpan.FromMilliseconds(140));
        var rise = new Duration(TimeSpan.FromMilliseconds(200));
        VolumeIndicatorMicShift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(VolumeIndicatorMicShift.Y, 9, dive, _easeQuadIn));
        VolumeIndicatorMicSquash.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(VolumeIndicatorMicSquash.ScaleY, 0.7, dive, _easeQuadIn));
        VolumeIndicatorMicWord.BeginAnimation(OpacityProperty, MakeAnim(VolumeIndicatorMicWord.Opacity, 0, dive, _easeQuadIn));
        var fade = MakeAnim(VolumeIndicatorMic.Opacity, 0, dive, _easeQuadIn);
        fade.Completed += (_, _) =>
        {
            if (token != _micDiveToken || _cleanedUp || !_isVolumeIndicatorActive || _levelHudKind != LevelHudKind.MicMute) return;
            ApplyMicMuteFace(toMuted);
            VolumeIndicatorMicShift.BeginAnimation(TranslateTransform.YProperty, MakeAnim(-9, 0, rise, _easeSoftSpring));
            VolumeIndicatorMicSquash.BeginAnimation(ScaleTransform.ScaleYProperty, MakeAnim(0.8, 1, rise, _easeSoftSpring));
            VolumeIndicatorMicWord.BeginAnimation(OpacityProperty, MakeAnim(0, 1, rise, _easeQuadOut));
            var appear = MakeAnim(0, 1, rise, _easeQuadOut);
            appear.Completed += (_, _) =>
            {
                if (token == _micDiveToken) CancelMicDive();
            };
            VolumeIndicatorMic.BeginAnimation(OpacityProperty, appear);
        };
        VolumeIndicatorMic.BeginAnimation(OpacityProperty, fade);
    }
}
