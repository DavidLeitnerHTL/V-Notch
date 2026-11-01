using System;
using System.Linq;
using VNotch.Services;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

/// <summary>Who holds the notch and who goes into the orb beside it.</summary>
public class LiveActivitiesTests
{
    [Theory]
    // Alone: in the notch; a timer alone only has the orb.
    [InlineData(true, null, false, "Music", "None")]
    [InlineData(false, "Working", false, "Claude", "None")]
    [InlineData(false, null, true, "None", "Countdown")]
    // Music outranks Claude at work, which goes into the orb.
    [InlineData(true, "Working", false, "Music", "Claude")]
    // Claude waiting for you takes the notch; the music moves to the orb.
    [InlineData(true, "Waiting", false, "Claude", "Music")]
    // A timer outranks Claude at work for the orb, never the music.
    [InlineData(true, "Working", true, "Music", "Countdown")]
    [InlineData(false, "Working", true, "Claude", "Countdown")]
    [InlineData(true, null, true, "Music", "Countdown")]
    [InlineData(true, "Waiting", true, "Claude", "Music")]
    // Idle and finished sessions are not live.
    [InlineData(false, "Done", false, "None", "None")]
    [InlineData(true, "Idle", false, "Music", "None")]
    public void TheMoreImportantOneHoldsTheNotch(bool music, string? claude, bool countdown, string main, string bubble)
    {
        ClaudeSessionStatus? status = claude == null ? null : Enum.Parse<ClaudeSessionStatus>(claude);
        var (actualMain, actualBubble) = LiveActivities.Arrange(music, status, countdown);
        Assert.Equal((main, bubble), (actualMain.ToString(), actualBubble.ToString()));
    }

    [Theory]
    // Several Claude sessions: with Claude in the notch and nothing else live, the others take the orb.
    [InlineData(false, "Working", false, "Claude", "Claude")]
    [InlineData(false, "Waiting", false, "Claude", "Claude")]
    // Something else ranks higher for the orb: it keeps it (the other sessions wait behind).
    [InlineData(true, "Waiting", false, "Claude", "Music")]
    [InlineData(false, "Working", true, "Claude", "Countdown")]
    // Music in the notch: the orb holds all the sessions anyway.
    [InlineData(true, "Working", false, "Music", "Claude")]
    public void OtherClaudeSessionsTakeAFreeOrb(bool music, string claude, bool countdown, string main, string bubble)
    {
        var (actualMain, actualBubble) = LiveActivities.Arrange(music, Enum.Parse<ClaudeSessionStatus>(claude), countdown, moreClaude: true);
        Assert.Equal((main, bubble), (actualMain.ToString(), actualBubble.ToString()));
    }

    [Fact]
    public void LiveSessionsPutWhoWaitsFirstThenWhoWorksLongest()
    {
        var now = DateTime.UtcNow;
        ClaudeSession S(string id, ClaudeSessionStatus status, int minutesAgo) =>
            new() { Id = id, Status = status, TurnStartedUtc = now.AddMinutes(-minutesAgo) };
        var ordered = MainWindow.LiveSessions(new[]
        {
            S("short", ClaudeSessionStatus.Working, 1),
            S("idle", ClaudeSessionStatus.Idle, 30),
            S("waits", ClaudeSessionStatus.Waiting, 2),
            S("long", ClaudeSessionStatus.Working, 9),
            S("done", ClaudeSessionStatus.Done, 5)
        });
        Assert.Equal(new[] { "waits", "long", "short" }, ordered.Select(s => s.Id));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(0.4, "0:01")]
    [InlineData(59, "0:59")]
    [InlineData(61, "1:01")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-3, "0:00")]
    public void TheOrbCountsTheTimerDown(double seconds, string expected)
    {
        Assert.Equal(expected, MainWindow.FormatCountdown(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void TheRingIsFullAtTheStartAndGoneAtTheEnd()
    {
        Assert.IsType<System.Windows.Media.EllipseGeometry>(MainWindow.TimerArcGeometry(1));
        Assert.True(MainWindow.TimerArcGeometry(0).IsEmpty());
        var half = MainWindow.TimerArcGeometry(0.5).Bounds;
        // Half a ring drawn clockwise from the top: the right half of the circle.
        Assert.InRange(half.Left, 49, 51);
        Assert.InRange(half.Width, 41, 43);
    }
}
