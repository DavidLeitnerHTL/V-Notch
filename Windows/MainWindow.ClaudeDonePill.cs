using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Threading;
using VNotch.Controllers;
using VNotch.Services;
using VNotch.Services.ClaudeCode;

namespace VNotch;

public partial class MainWindow
{
    #region Claude done pill

    // A finished Claude turn stays in the closed notch until it is tapped (that goes to the session), pushed away
    // with a right-click, or the session gets a new prompt. Other alerts may pass it; it comes back afterwards.
    private static readonly TimeSpan ClaudeDoneReturnCheck = TimeSpan.FromSeconds(1);

    private sealed record ClaudeDoneNote(string SessionId, string Name, string? Title);

    // Oldest first; the newest is the one on screen.
    private readonly List<ClaudeDoneNote> _claudeDone = new();
    private DispatcherTimer? _claudeDoneReturnTimer;
    // The session of the done pill on screen; null while the pill shows something else or nothing.
    private string? _claudeDoneShownId;

    private bool IsClaudeDonePillShown => _isClaudeNotificationVisible && _claudeDoneShownId != null;

    private void RememberClaudeDone(ClaudeSession session)
    {
        _claudeDone.RemoveAll(n => n.SessionId == session.Id);
        _claudeDone.Add(new ClaudeDoneNote(session.Id, session.Name, session.Title));
        ShowClaudeDonePill();
        WatchForClaudeDoneReturn();
    }

    private void ShowClaudeDonePill()
    {
        if (_claudeDone.Count == 0) return;
        var note = _claudeDone[^1];
        ShowNotchPill(note.Name, Loc.Get("control.claude.done"), appIcon: null, isClaude: true,
            tap: () => ClaudeSessionWindow.TryActivate(note.SessionId, note.Title), doneSessionId: note.SessionId);
    }

    private void ForgetClaudeDone(string sessionId) => _claudeDone.RemoveAll(n => n.SessionId == sessionId);

    /// <summary>
    /// While a finished turn waits, looks once a second whether the closed notch is free again (another alert
    /// ended, the notch closed, the fullscreen app left) and brings the pill back.
    /// </summary>
    private void WatchForClaudeDoneReturn()
    {
        if (_claudeDoneReturnTimer == null)
        {
            _claudeDoneReturnTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = ClaudeDoneReturnCheck };
            _claudeDoneReturnTimer.Tick += (_, _) =>
            {
                if (_claudeDone.Count == 0 || _cleanedUp)
                {
                    _claudeDoneReturnTimer.Stop();
                    return;
                }
                if (_isClaudeNotificationVisible || _compactPillArbiter.ActiveSlot != CompactPillSlot.None) return;
                ShowClaudeDonePill();
            };
        }
        if (!_claudeDoneReturnTimer.IsEnabled) _claudeDoneReturnTimer.Start();
    }

    /// <summary>A session that got a new prompt, waits for you again or has ended no longer counts as done.</summary>
    private void PruneClaudeDone()
    {
        if (_claudeDone.Count == 0 || _claudeTracker == null) return;
        var sessions = _claudeTracker.GetSessions();
        int removed = _claudeDone.RemoveAll(n =>
            sessions.Find(s => s.Id == n.SessionId) is not { Status: ClaudeSessionStatus.Done });
        if (removed == 0 || !IsClaudeDonePillShown) return;
        if (_claudeDone.Exists(n => n.SessionId == _claudeDoneShownId)) return;
        _claudeNotificationTimer?.Stop();
        AnimateClaudeNotificationOut();
    }

    /// <summary>Another alert wants the closed notch: the done pill steps aside and returns once it is free.</summary>
    private void StepClaudeDonePillAside()
    {
        int token = _claudeNotificationToken;
        CancelClaudeNotificationImmediate();
        _compactPillArbiter.Release(token);
        RestorePrivacyDotVisibility();
        // The pill had hidden what rests below; the alert taking over starts from it like from an empty notch.
        System.Windows.FrameworkElement content = _isMusicCompactMode && _currentMediaInfo != null
            ? MusicCompactContent
            : CollapsedContent;
        content.BeginAnimation(OpacityProperty, null);
        content.Opacity = 1;
        content.Visibility = System.Windows.Visibility.Visible;
    }

    /// <summary>A right-click pushes the done pill away without going to the session.</summary>
    private void NotchWrapper_DismissClaudeDone(object sender, MouseButtonEventArgs e)
    {
        if (!IsClaudeDonePillShown || IsSpeakerDragSwitch) return;
        e.Handled = true;
        ForgetClaudeDone(_claudeDoneShownId!);
        _claudeNotificationTimer?.Stop();
        AnimateClaudeNotificationOut();
    }

    #endregion
}
