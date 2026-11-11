using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using VNotch.Services;
using VNotch.Services.ClaudeCode;
using VNotch.Tests.Fakes;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class ClaudeAskPillTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static JsonObject Ask(string tool, string? command = null) => new()
    {
        ["session_id"] = "pill-session",
        ["hook_event_name"] = "PermissionRequest",
        ["cwd"] = @"C:\work\notch-app",
        ["tool_name"] = tool,
        ["tool_input"] = command == null ? new JsonObject() : new JsonObject { ["command"] = command }
    };

    [Fact]
    public void AToolToAllowWaitsAsAPillInTheClosedNotchUntilItIsSettledElsewhere() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = new GreetingAcceptanceTests.MainWindowFixture("en", greeting: false,
            configureSettings: settings => settings.EnableLocalOnlyMode = true,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
        var window = fixture.Window;
        window.Show();
        var tracker = Field<ClaudeSessionTracker>(window, "_claudeTracker");

        var answer = tracker.HandleHookAsync(Ask("Bash", "git push"));
        await WpfFrameWaiter.UntilAsync(() => Field<ClaudePermissionRequest?>(window, "_askRequest") != null, "ask pill shown", ct);
        Assert.Contains("git push", window.ClaudeNotificationStatus.Text);
        Assert.Equal("notch-app", window.ClaudeNotificationTitle.Text);
        // The pill, not the tab: the notch stays closed (a pill only shows on the closed notch).
        Assert.Equal(Visibility.Visible, window.ClaudeNotification.Visibility);
        Assert.False(Field<System.Windows.Threading.DispatcherTimer>(window, "_claudeNotificationTimer").IsEnabled);

        // Answered in the terminal: the pill goes.
        var request = tracker.GetPending().Single();
        Assert.True(tracker.Decide(request.Id, ClaudeDecision.Allow));
        await answer;
        await WpfFrameWaiter.UntilAsync(() => window.ClaudeNotification.Visibility != Visibility.Visible, "pill gone", ct);
        Assert.Null(Field<ClaudePermissionRequest?>(window, "_askRequest"));
    });

    [Fact]
    public void SettlingInThePillNamesTheAnswerInItsColour() => SharedStaTestRunner.RunAsync(async ct =>
    {
        await using var fixture = new GreetingAcceptanceTests.MainWindowFixture("en", greeting: false,
            configureSettings: settings => settings.EnableLocalOnlyMode = true,
            configureServices: services => services.AddSingleton<IMediaDetectionService>(new FakeMediaDetectionService()));
        var window = fixture.Window;
        window.Show();
        var tracker = Field<ClaudeSessionTracker>(window, "_claudeTracker");
        _ = tracker.HandleHookAsync(Ask("Edit"));
        await WpfFrameWaiter.UntilAsync(() => Field<ClaudePermissionRequest?>(window, "_askRequest") != null, "ask pill shown", ct);

        // As the swipe does: decide, then show it, in one go.
        Assert.True(tracker.Decide(tracker.GetPending().Single().Id, ClaudeDecision.Deny));
        typeof(MainWindow).GetMethod("PlayAskSettled", Private)!.Invoke(window, new object[] { false });
        Assert.Equal("Denied", window.ClaudeNotificationStatus.Text);
        Assert.Null(Field<ClaudePermissionRequest?>(window, "_askRequest"));
        await WpfFrameWaiter.UntilAsync(() => window.ClaudeNotification.Visibility != Visibility.Visible, "pill gone after the linger", ct);
    });

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, Private)!.GetValue(window)!;
}
