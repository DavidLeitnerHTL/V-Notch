using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VNotch.Controls;
using VNotch.Controllers;
using VNotch.Models;
using VNotch.Presenters;
using VNotch.Services;

namespace VNotch;

public partial class MainWindow
{
    private NotchShellAttachment _shellAttachment = null!;
    private double _shellOpenWidth, _shellOpenHeight;
    private double _shellDragStartWidth, _shellDragStartHeight;
    private double _shellDragStartRadius, _shellDragOriginY;
    private bool _shellGestureInMotion;
    private bool _shellGestureWasOpen, _shellDragActive;
    private double? _shellReleaseVelocity;
    private double _shellReleaseWidthRatio;
    private NotchView _shellDragView;
    private DateTime _shellLastSampleAt;

    private void InitializeShellAttachment()
    {
        var open = new GlassClipGrid { Name = "OpenShellContent", Opacity = 0, UseLayoutRounding = false };
        FrameworkElement[] elements = [ExpandedContent, TimerContent, SecondaryContent, CameraContent,
            ClaudeContent, PiContent, SchoolContent, WeatherContent, AudioContent,
            NavIconsPanel, NavIconsBackground, SharedSettingsHost,
            SystemBackdrop, HomeBackdrop, SchoolBackdrop, ClaudeBackdrop];
        foreach (var element in elements)
        {
            ((Panel)element.Parent).Children.Remove(element);
            open.Children.Add(element);
        }
        NotchContent.Children.Add(open);
        _shellAttachment = new NotchShellAttachment(NotchBorder, open, () => _collapsedHeight,
            CollapsedContent, MusicCompactContent);
        NotchWrapper.LostMouseCapture += (_, _) =>
        {
            if (!_isGestureActive) return;
            if (_musicDragging) FinishMusicSwipe(true, DateTime.UtcNow);
            else if (_shellDragActive) FinishShellDrag(canceled: true);
            else AnimateGestureSnapBack();
            _gestureController.CancelTracking();
            _isGestureActive = false;
        };
    }

    private bool IsShellDragControl(DependencyObject? element)
    {
        if (IsInteractiveControl(element)) return true;
        while (element != null && element is not Window)
        {
            if (ReferenceEquals(element, ThumbnailBorder) && CanSwipeMusic) return false;
            if (element is ActionBorder || element is FrameworkElement { Cursor: { } cursor } && cursor == Cursors.Hand)
                return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    internal void BeginShellGestureTracking(Point position, DateTime timestamp)
    {
        _shellGestureWasOpen = _transitionCoordinator.TargetView != NotchView.Compact;
        _shellDragStartWidth = NotchBorder.Width;
        _shellDragStartHeight = NotchBorder.Height;
        _shellDragStartRadius = CurrentCornerRadius;
        _shellDragOriginY = 0;
        _shellGestureInMotion = Motion.IsRunning(NotchBorder, WidthProperty) || Motion.IsRunning(NotchBorder, HeightProperty);
        _shellDragActive = false;
        _shellLastSampleAt = timestamp;
        _gestureController.BeginTracking(position, allowShellDrag: true, expanded: _shellGestureWasOpen, timestamp: timestamp,
            allowMediaGestures: _currentMediaInfo?.IsAnyMediaPlaying == true);
    }

    internal bool UpdateGestureTracking(Point position, DateTime timestamp)
    {
        bool triggered = _gestureController.UpdateTracking(position, timestamp);
        _shellLastSampleAt = timestamp;
        if (_shellGestureWasOpen && CanSwipeMusic && _gestureController.HasDragAxis && !_gestureController.IsVerticalDrag)
        {
            MoveMusicSwipe(_gestureController.AccumulatedX);
            return false;
        }
        if (!_gestureController.IsVerticalDrag) return triggered;
        if (!_shellDragActive)
        {
            _shellDragActive = true;
            _isAnimating = true;
            if (_shellGestureInMotion)
            {
                // Slop must not rewind a shell that kept moving between the press and axis lock.
                _shellDragStartWidth = NotchBorder.Width;
                _shellDragStartHeight = NotchBorder.Height;
                _shellDragStartRadius = CurrentCornerRadius;
                _shellDragOriginY = _gestureController.AccumulatedY;
            }
            _hoverCollapseTimer.Stop();
            _hoverThumbnailDelayTimer.Stop();
            _notchShellPresenter!.CancelCurrentAnimation();
            if (!_shellGestureWasOpen)
            {
                _shellDragView = NotchView.Secondary;
                _shellOpenWidth = ClipboardTrayWidth;
                _shellOpenHeight = ClipboardTrayHeight;
                SecondaryContent.Width = _shellOpenWidth - SecondaryContent.Margin.Left - SecondaryContent.Margin.Right;
                SecondaryContent.Height = _shellOpenHeight - SecondaryContent.Margin.Top - SecondaryContent.Margin.Bottom;
                SecondaryContent.VerticalAlignment = VerticalAlignment.Top;
                _notchContentPresenter!.SnapToView(_shellDragView);
                ClipboardTrayView.BeginEntrance(animate: false);
                _navigationVisualTarget = _shellDragView;
                _shellAttachment.Attach(_shellOpenWidth, _shellOpenHeight);
                AnimateStatusBarReveal(true);
            }
            else
            {
                _shellDragView = _transitionCoordinator.TargetView;
                if (!_shellAttachment.IsAttached)
                {
                    _shellOpenWidth = _shellDragStartWidth;
                    _shellOpenHeight = _shellDragStartHeight;
                }
            }
            ResizeHostWindowHeight(_shellOpenHeight + 50);
            _shellAttachment.Attach(_shellOpenWidth, _shellOpenHeight);
        }

        double dy = _gestureController.AccumulatedY - _shellDragOriginY;
        bool drawerHandled = false;
        if (_shellGestureWasOpen) MoveShellDrawer(dy, ref drawerHandled);
        if (drawerHandled) return false;
        double height, width, radius;
        if (!_shellGestureWasOpen)
        {
            double span = Math.Max(1, _shellOpenHeight - _shellDragStartHeight);
            double pull = dy < 0 ? -ShellRubber(-dy, 20) : dy <= span ? dy : span + ShellRubber(dy - span, 50);
            height = _shellDragStartHeight + pull;
            double p = Math.Clamp(pull / span, 0, 1);
            width = _shellDragStartWidth + (_shellOpenWidth - _shellDragStartWidth) * p;
            radius = _shellDragStartRadius + (_cornerRadiusExpanded - _shellDragStartRadius) * p;
        }
        else if (dy < 0)
        {
            height = Math.Max(_collapsedHeight, _shellDragStartHeight + dy);
            double p = Math.Clamp((height - _collapsedHeight) / Math.Max(1, _shellDragStartHeight - _collapsedHeight), 0, 1);
            width = _collapsedWidth + (_shellDragStartWidth - _collapsedWidth) * p;
            radius = _cornerRadiusCollapsed + (_shellDragStartRadius - _cornerRadiusCollapsed) * p;
        }
        else
        {
            height = _shellDragStartHeight + ShellRubber(dy, 26);
            width = _shellDragStartWidth;
            radius = _shellDragStartRadius;
        }
        Motion.Stop(NotchBorder, WidthProperty, width);
        Motion.Stop(NotchBorder, HeightProperty, height);
        AnimateCornerRadius(radius, (SpringMotion?)null);
    
        return false;
    }

    internal static double ShellRubber(double distance, double limit) =>
        (1 - 1 / (distance * .55 / limit + 1)) * limit;

    private void ApplyShellReleaseVelocity()
    {
        if (_shellReleaseVelocity is not { } velocity) return;
        _shellReleaseVelocity = null;
        _notchShellPresenter!.SetReleaseVelocity(velocity, _shellReleaseWidthRatio);
    }

    internal void FinishShellDrag(bool canceled, DateTime? timestamp = null)
    {
        if (!_shellDragActive) return;
        _shellDragActive = false;
        double dy = _gestureController.AccumulatedY - _shellDragOriginY;
        double velocity = ((timestamp ?? DateTime.UtcNow) - _shellLastSampleAt).TotalMilliseconds < 60
            ? _gestureController.VerticalVelocity : 0;
        bool drawerHandled = false;
        if (_shellGestureWasOpen) FinishShellDrawer(velocity, canceled, ref drawerHandled);
        if (drawerHandled) return;
        bool open = _shellGestureWasOpen
            ? canceled || !(dy < -44 || velocity < -380)
            : !canceled && (dy > 40 || velocity > 380);
        _shellReleaseVelocity = velocity;
        if (_shellGestureWasOpen)
            _shellReleaseWidthRatio = dy >= 0 ? 0 :
                (_shellDragStartWidth - _collapsedWidth) / Math.Max(1, _shellDragStartHeight - _collapsedHeight);
        else
            _shellReleaseWidthRatio = dy >= _shellOpenHeight - _shellDragStartHeight ? 0 :
                (_shellOpenWidth - _shellDragStartWidth) / Math.Max(1, _shellOpenHeight - _shellDragStartHeight);
        if (_transitionCoordinator.RequestView(open ? _shellDragView : NotchView.Compact, "ShellDrag")) return;
        open = _transitionCoordinator.TargetView != NotchView.Compact;

        // A rejected drag returns to the existing view without creating a second navigation session.
        var plan = new TransitionPlan(_transitionCoordinator.ActiveTransitionId,
            open ? NotchView.Compact : _shellDragView, open ? _shellDragView : NotchView.Compact,
            open ? NotchShapeState.Expanding : NotchShapeState.Collapsing,
            open ? _shellOpenWidth : _collapsedWidth, open ? _shellOpenHeight : _collapsedHeight,
            open ? _cornerRadiusExpanded : _cornerRadiusCollapsed,
            new(TimeSpan.FromMilliseconds(500), AnimationPrimitives._easeUiOut, AnimationConfig.TargetFps,
                AnimationConfig.ReduceMotion, open ? Motion.Shift : Motion.ShellCloseWidth));
        ApplyShellReleaseVelocity();
        _notchShellPresenter!.AnimateShell(plan, _ =>
        {
            _isAnimating = false;
            if (open && _transitionCoordinator.IsTransitionActive)
                OnExpandCompleted((int)plan.SessionId, IsCountdownCompletionVisualActive, _shellDragView);
            else if (!open)
            {
                _notchContentPresenter!.SnapToView(NotchView.Compact);
                _navigationVisualTarget = null;
                AnimateStatusBarReveal(false);
                if (_transitionCoordinator.IsTransitionActive)
                    OnCollapseCompleted((int)plan.SessionId, _isAudioView,
                        _isMusicCompactMode ? MusicCompactContent : CollapsedContent, IsCountdownCompletionVisualActive);
                else RestoreExpandedWindowSize();
            }
        });
    }

    partial void MoveShellDrawer(double deltaY, ref bool handled);
    partial void FinishShellDrawer(double velocity, bool canceled, ref bool handled);
}
