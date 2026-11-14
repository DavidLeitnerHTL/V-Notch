using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VNotch.Services;
using static VNotch.Services.AnimationPrimitives;

namespace VNotch;

public partial class MainWindow
{
    #region Clipboard Peek

    private bool _isClipboardPeekActive = false;
    private int _clipboardPeekToken = 0;
    private UiScheduledTimer? _clipboardRevertTimer;

    private void PlayClipboardPeek()
    {
        if (!_isExpanded && !_isAnimating && !AnimationConfig.ReduceMotion)
            foreach (var scale in new[] { NotchScale, NotchShadowScale })
            {
                Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Press, velocity: Motion.VelocityOf(scale, ScaleTransform.ScaleXProperty) + 1);
                Motion.To(scale, ScaleTransform.ScaleYProperty, 1, Motion.Press, velocity: Motion.VelocityOf(scale, ScaleTransform.ScaleYProperty) - 0.8);
            }
        if (_isMusicCompactMode && !_isExpanded) ShowClipboardCopiedState();
        else if (!_isExpanded && !_isAnimating) ShowCopiedPill();
    }
    private void ShowClipboardCopiedState()
    {
        if (!TryAcquireCompactSlot(VNotch.Controllers.CompactPillSlot.Clipboard, out int token))
            return;

        _clipboardPeekToken = token;
        _isClipboardPeekActive = true;

        SuppressPrivacyDot();

        ClipboardCopiedText.Text = Loc.Get("clipboard.copied").TrimStart('✓', ' ');

        CancelThumbnailSwitchAnimations();

        CompactThumbnailBorder.BeginAnimation(OpacityProperty, null);
        CompactThumbnailScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CompactThumbnailScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        var thumbFadeOut = MakeAnim(1.0, 0.0, _dur150, _easeQuadOut);
        thumbFadeOut.Completed += (s, e) =>
        {
            if (_isClipboardPeekActive && !IsCompactSlotStale(token))
                CompactThumbnailBorder.Visibility = Visibility.Collapsed;
        };
        CompactThumbnailBorder.BeginAnimation(OpacityProperty, thumbFadeOut);

        MusicViz.BeginAnimation(OpacityProperty, null);
        MusicViz.Opacity = 1;
        MusicViz.Visibility = Visibility.Visible;
        MusicViz.SetCopiedFeedback(true);

        CompactHoverInfo.BeginAnimation(OpacityProperty, null);
        CompactHoverInfo.Opacity = 0;
        CompactHoverInfo.Visibility = Visibility.Collapsed;

        HideCompactSurface(ClipboardCheckIcon);

        ClipboardCopiedText.Visibility = Visibility.Visible;
        ClipboardCopiedText.BeginAnimation(OpacityProperty, null);




        var textFadeIn = MakeAnim(0.0, 1.0, _dur250, _easeQuadOut);


        ClipboardCopiedText.BeginAnimation(OpacityProperty, textFadeIn);
        Motion.To(ClipboardCopiedTranslate, TranslateTransform.XProperty, 0, Motion.Tile, from: -8, delay: Motion.Stagger(1));

        StopClipboardRevertTimer();

        int revertToken = _clipboardPeekToken;
        var revertTimer = new UiScheduledTimer
        {
            Interval = TimeSpan.FromMilliseconds(1600)
        };
        _clipboardRevertTimer = revertTimer;
        revertTimer.Tick += (s, e) =>
        {
            revertTimer.Stop();
            revertTimer.Dispose();
            if (!ReferenceEquals(_clipboardRevertTimer, revertTimer) ||
                revertToken != _clipboardPeekToken)
            {
                return;
            }

            _clipboardRevertTimer = null;
            RevertClipboardCopiedState();
        };
        revertTimer.Start();
    }
    private void RevertClipboardCopiedState()
    {
        int token = _clipboardPeekToken;
        if (IsCompactSlotStale(token)) return;
        _isClipboardPeekActive = false;
        _compactPillArbiter.Release(token);
        _clipboardPeekToken = 0;

        RestorePrivacyDotVisibility();

        MusicViz.SetCopiedFeedback(false);
        HideCompactSurface(ClipboardCheckIcon);

        ClipboardCopiedText.BeginAnimation(OpacityProperty, null);


        var textFadeOut = MakeAnim(1.0, 0.0, _dur250, _easeQuadOut);


        textFadeOut.Completed += (s, e) =>
        {
            if (!_isClipboardPeekActive)
                ClipboardCopiedText.Visibility = Visibility.Collapsed;
        };

        ClipboardCopiedText.BeginAnimation(OpacityProperty, textFadeOut);
        Motion.To(ClipboardCopiedTranslate, TranslateTransform.XProperty, -8, Motion.Exit);

        CompactThumbnailBorder.BeginAnimation(OpacityProperty, null);
        CompactThumbnailScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        CompactThumbnailScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        CompactThumbnailScale.ScaleX = 1.0;
        CompactThumbnailScale.ScaleY = 1.0;
        CompactThumbnailBorder.Visibility = Visibility.Visible;
        CompactThumbnailBorder.Opacity = 0;
        var thumbFadeIn = MakeAnim(0.0, 1.0, _dur200, _easeQuadOut);
        CompactThumbnailBorder.BeginAnimation(OpacityProperty, thumbFadeIn);

        ShowMusicVisualizer(duration: _dur100);
    }

    private void CancelClipboardPeekImmediate()
    {
        if (!_isClipboardPeekActive) return;

        StopClipboardRevertTimer();
        MusicViz.SetCopiedFeedback(false);
        _isClipboardPeekActive = false;
        _clipboardPeekToken = 0;

        ClipboardCheckIcon.BeginAnimation(OpacityProperty, null);
        ClipboardCheckScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ClipboardCheckScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ClipboardCheckIcon.Opacity = 0;
        ClipboardCheckIcon.Visibility = Visibility.Collapsed;

        ClipboardCopiedText.BeginAnimation(OpacityProperty, null);

        ClipboardCopiedText.Opacity = 0;
        ClipboardCopiedText.Visibility = Visibility.Collapsed;

    }

    private void StopClipboardRevertTimer()
    {
        var timer = _clipboardRevertTimer;
        _clipboardRevertTimer = null;
        timer?.Stop();
        timer?.Dispose();
    }

    #endregion
}
