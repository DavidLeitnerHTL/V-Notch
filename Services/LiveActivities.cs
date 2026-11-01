using VNotch.Services.ClaudeCode;

namespace VNotch.Services;

/// <summary>What can live in the closed notch, or in the orb beside it.</summary>
internal enum LiveActivity
{
    None,
    Music,
    Claude,
    Countdown
}

/// <summary>
/// Two live things at once: the more important one holds the notch, the next one goes into the orb beside it
/// (a third waits). Claude Code waiting for you beats everything; then music, then a running timer, and Claude
/// Code at work comes last. A timer has no face in the notch itself, so it only ever takes the orb.
/// </summary>
internal static class LiveActivities
{
    /// <param name="moreClaude">More than one Claude Code session is live: with Claude in the notch and nothing
    /// else in the orb, the other sessions take the orb.</param>
    public static (LiveActivity Main, LiveActivity Bubble) Arrange(bool music, ClaudeSessionStatus? claude, bool countdown, bool moreClaude = false)
    {
        // Highest first.
        var ranked = new LiveActivity[3];
        int count = 0;
        if (claude == ClaudeSessionStatus.Waiting) ranked[count++] = LiveActivity.Claude;
        if (music) ranked[count++] = LiveActivity.Music;
        if (countdown) ranked[count++] = LiveActivity.Countdown;
        if (claude == ClaudeSessionStatus.Working) ranked[count++] = LiveActivity.Claude;

        LiveActivity main = LiveActivity.None;
        for (int i = 0; i < count; i++)
        {
            if (ranked[i] == LiveActivity.Countdown) continue;
            main = ranked[i];
            break;
        }
        LiveActivity bubble = LiveActivity.None;
        for (int i = 0; i < count; i++)
        {
            if (ranked[i] == main) continue;
            bubble = ranked[i];
            break;
        }
        if (bubble == LiveActivity.None && main == LiveActivity.Claude && moreClaude) bubble = LiveActivity.Claude;
        return (main, bubble);
    }
}
