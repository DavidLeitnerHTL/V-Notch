using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using VNotch.Controls;
using VNotch.Services;
using VNotch.Services.ClaudeCode;

namespace VNotch;

public partial class MainWindow
{
    #region Claude live activity (closed notch)

    private static readonly Brush ClaudeLiveWorkingBrush = FrozenBrush(0xD9, 0x77, 0x57);
    private static readonly Brush ClaudeLiveWaitingBrush = FrozenBrush(0xFF, 0xD6, 0x0A);
    // An idle spinner: smooth enough at half the display rate, and half the work.
    private const int ClaudeLiveFps = 30;

    private ClaudeSession? _claudeLiveSession;
    private (ClaudeSessionStatus Status, bool Big)? _claudeLiveMotion;
    private DispatcherTimer? _claudeLiveClock;

    private static Brush FrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>The session the closed notch shows: one that waits for you, else the one working longest.</summary>
    internal static ClaudeSession? PickLiveSession(List<ClaudeSession> sessions)
    {
        ClaudeSession? working = null;
        foreach (var session in sessions)
        {
            if (session.Status == ClaudeSessionStatus.Waiting) return session;
            if (session.Status != ClaudeSessionStatus.Working) continue;
            if (working == null || (session.TurnStartedUtc ?? DateTime.MaxValue) < (working.TurnStartedUtc ?? DateTime.MaxValue))
                working = session;
        }
        return working;
    }

    private void UpdateClaudeLive()
    {
        if (_claudeTracker == null || _cleanedUp) return;
        var session = PickLiveSession(_claudeTracker.GetSessions());
        _claudeLiveSession = session;

        if (session != null)
        {
            bool waiting = session.Status == ClaudeSessionStatus.Waiting;
            Brush brush = waiting ? ClaudeLiveWaitingBrush : ClaudeLiveWorkingBrush;
            ClaudeLiveStar.Fill = brush;
            ClaudeLiveBadgeStar.Fill = brush;
            ClaudeLiveStatus.Foreground = waiting ? ClaudeLiveWaitingBrush : ClaudeLiveStatusShimmer;
            ShowClaudeLiveStatus(Loc.Get(ClaudeLiveStatusKey(session)));
            UpdateClaudeLiveText();
        }
        else
        {
            _claudeLiveStatusTimer?.Stop();
        }
        // Whether it holds the notch, takes the orb beside the music, or waits behind both.
        ArrangeLiveActivities(_currentMediaInfo, applyCompact: true);
        ApplyClaudeLiveMotion();
    }

    private void UpdateClaudeLiveText()
    {
        var session = _claudeLiveSession;
        if (session == null) return;
        if (_bubbleOpen && _bubbleActivity == LiveActivity.Claude) UpdateBubbleContent();
        ClaudeLiveText.Text = ControlCenterView.FormatElapsed(DateTime.UtcNow - (session.TurnStartedUtc ?? DateTime.UtcNow));
    }

    /// <summary>One word for what the session is at, like Claude Code's own spinner: the tool decides it.</summary>
    internal static string ClaudeLiveStatusKey(ClaudeSession session)
    {
        if (session.Status == ClaudeSessionStatus.Waiting)
        {
            return session.Tool switch
            {
                "AskUserQuestion" => "claude.status.question",
                "ExitPlanMode" => "claude.status.plan",
                _ => "claude.status.waiting"
            };
        }
        return session.Tool switch
        {
            null or "" => "claude.status.thinking",
            "Edit" or "MultiEdit" or "Write" or "NotebookEdit" => "claude.status.writing",
            "Read" or "Grep" or "Glob" or "LS" or "NotebookRead" => "claude.status.reading",
            "Bash" or "PowerShell" or "BashOutput" or "KillShell" or "KillBash" or "Monitor" => "claude.status.running",
            "WebFetch" or "WebSearch" => "claude.status.browsing",
            "Task" or "Agent" or "SendMessage" => "claude.status.delegating",
            "TodoWrite" or "TaskCreate" or "TaskUpdate" or "EnterPlanMode" => "claude.status.planning",
            _ => "claude.status.working"
        };
    }

    // Tools follow each other within a blink; each word stays long enough to be read.
    private static readonly TimeSpan ClaudeLiveStatusHold = TimeSpan.FromMilliseconds(700);
    private DateTime _claudeLiveStatusShownUtc = DateTime.MinValue;
    private string? _claudeLiveStatusNext;
    private DispatcherTimer? _claudeLiveStatusTimer;

    private void ShowClaudeLiveStatus(string word)
    {
        _claudeLiveStatusNext = word;
        if (ClaudeLiveStatus.Text == word)
        {
            _claudeLiveStatusTimer?.Stop();
            return;
        }
        var wait = ClaudeLiveStatusHold - (DateTime.UtcNow - _claudeLiveStatusShownUtc);
        if (wait <= TimeSpan.Zero || ClaudeLiveStatus.Text.Length == 0)
        {
            ApplyClaudeLiveStatus();
            return;
        }
        if (_claudeLiveStatusTimer == null)
        {
            _claudeLiveStatusTimer = new DispatcherTimer(DispatcherPriority.Background);
            _claudeLiveStatusTimer.Tick += (_, _) => ApplyClaudeLiveStatus();
        }
        if (_claudeLiveStatusTimer.IsEnabled) return;
        _claudeLiveStatusTimer.Interval = wait;
        _claudeLiveStatusTimer.Start();
    }

    private void ApplyClaudeLiveStatus()
    {
        _claudeLiveStatusTimer?.Stop();
        if (_claudeLiveStatusNext is not { } word || _cleanedUp) return;
        ClaudeLiveStatus.Text = word;
        _claudeLiveStatusShownUtc = DateTime.UtcNow;
    }

    private void ClaudeLive_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => ApplyClaudeLiveMotion();

    /// <summary>
    /// Turns the star while Claude works and pulses it while it waits, but only while one of the stars is on
    /// screen: hidden behind a fullscreen app or an open notch, nothing ticks.
    /// </summary>
    private void ApplyClaudeLiveMotion()
    {
        // Only the star on screen moves: the big one in the notch, or the small one in the orb beside it.
        bool big = ClaudeLive.IsVisible;
        bool inBubble = _bubbleShown && _bubbleActivity == LiveActivity.Claude && ActivityBubble.IsVisible;
        bool onScreen = big || inBubble;
        (ClaudeSessionStatus Status, bool Big)? motion = onScreen && _claudeLiveSession != null ? (_claudeLiveSession.Status, big) : null;

        if ((big || (inBubble && _bubbleOpen)) && _claudeLiveSession?.Status is ClaudeSessionStatus.Working or ClaudeSessionStatus.Waiting)
        {
            _claudeLiveClock ??= CreateClaudeLiveClock();
            _claudeLiveClock.Start();
        }
        else
        {
            _claudeLiveClock?.Stop();
        }

        if (motion == _claudeLiveMotion) return;
        _claudeLiveMotion = motion;
        foreach (var rotate in new[] { ClaudeLiveStarRotate, ClaudeLiveBadgeRotate })
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
        ClaudeLiveStatusShine.BeginAnimation(TranslateTransform.XProperty, null);
        foreach (var scale in new[] { ClaudeLiveStarScale, ClaudeLiveBadgeScale })
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }
        if (motion is not { } m || AnimationConfig.ReduceMotion) return;

        if (m.Status == ClaudeSessionStatus.Working)
        {
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(2.4))) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, ClaudeLiveFps);
            if (!m.Big)
            {
                ClaudeLiveBadgeRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
                return;
            }
            ClaudeLiveStarRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
            // At cover size a bare spin reads as a loading wheel; a slow breath makes it feel alive.
            Scale(ClaudeLiveStarScale, ClaudeLivePulse(0.86, 1200));
            // A light sweeps over the status word, then rests a moment before the next pass.
            var sweep = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
            sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sweep.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
            sweep.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
            Timeline.SetDesiredFrameRate(sweep, ClaudeLiveFps);
            ClaudeLiveStatusShine.BeginAnimation(TranslateTransform.XProperty, sweep);
        }
        else if (m.Status == ClaudeSessionStatus.Waiting)
        {
            // The big star stays inside the notch: 22 px may only grow a little.
            if (m.Big) Scale(ClaudeLiveStarScale, ClaudeLivePulse(1.12, 650));
            else Scale(ClaudeLiveBadgeScale, ClaudeLivePulse(1.3, 650));
        }

        static void Scale(ScaleTransform scale, DoubleAnimation pulse)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
    }

    private static DoubleAnimation ClaudeLivePulse(double to, int milliseconds)
    {
        var pulse = new DoubleAnimation(1, to, new Duration(TimeSpan.FromMilliseconds(milliseconds)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Timeline.SetDesiredFrameRate(pulse, ClaudeLiveFps);
        return pulse;
    }

    private DispatcherTimer CreateClaudeLiveClock()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => UpdateClaudeLiveText();
        return timer;
    }

    #endregion

    #region Claude peek (hover over the closed notch)

    private static readonly TimeSpan ClaudePeekDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ClaudePeekLinger = TimeSpan.FromMilliseconds(500);
    private DispatcherTimer? _claudePeekTimer;
    private bool _isClaudePeek;

    /// <summary>
    /// The pointer rests on the closed notch while Claude works: after a moment the session's name and what it
    /// is doing unfold. With music the cover keeps its own hover, so only the right half peeks.
    /// </summary>
    private void OnNotchHoverForClaudePeek(bool entered)
    {
        if (!entered)
        {
            _claudePeekTimer?.Stop();
            if (_isClaudePeek && _isClaudeNotificationVisible && _claudeNotificationTimer != null)
            {
                _claudeNotificationTimer.Stop();
                _claudeNotificationTimer.Interval = ClaudePeekLinger;
                _claudeNotificationTimer.Start();
            }
            return;
        }
        if (_claudeLiveSession == null || _isExpanded || _isClaudeNotificationVisible) return;
        if (_claudePeekTimer == null)
        {
            _claudePeekTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ClaudePeekDelay };
            _claudePeekTimer.Tick += (_, _) => TryShowClaudePeek();
        }
        _claudePeekTimer.Stop();
        _claudePeekTimer.Start();
    }

    private void TryShowClaudePeek()
    {
        // Keeps looking while the pointer stays, e.g. until it moves off the cover.
        if (!NotchBorder.IsMouseOver || _claudeLiveSession is not { } session || _isExpanded || _isAnimating)
        {
            _claudePeekTimer?.Stop();
            return;
        }
        if (_isClaudeNotificationVisible || _isCompactThumbnailHovered) return;
        // In the orb beside the music, Claude opens there on a tap instead.
        if (_mainActivity != LiveActivity.Claude) return;

        _claudePeekTimer?.Stop();
        ShowNotchPill(session.Name, ClaudePeekStatus(session), appIcon: null, isClaude: true, tap: null);
        if (!_isClaudeNotificationVisible) return;
        // Stays while hovered; leaving starts a short linger instead.
        _isClaudePeek = true;
        _claudeNotificationTimer?.Stop();
    }

    private static string ClaudePeekStatus(ClaudeSession session)
    {
        if (session.Status == ClaudeSessionStatus.Waiting) return Loc.Get("control.claude.waiting");
        string doing = session.Activity is { Length: > 0 } activity ? activity : Loc.Get("control.claude.thinking");
        return session.TurnStartedUtc is { } started
            ? $"{doing}  ·  {ControlCenterView.FormatElapsed(DateTime.UtcNow - started)}"
            : doing;
    }

    #endregion
}
