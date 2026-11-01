using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

public class ClaudeSessionTrackerTests
{
    private static JsonObject Hook(string hookEvent, string session = "s1", string? tool = null, string? command = null)
    {
        var payload = new JsonObject
        {
            ["session_id"] = session,
            ["hook_event_name"] = hookEvent,
            ["cwd"] = @"C:\work\notch-app"
        };
        if (tool != null) payload["tool_name"] = tool;
        if (command != null) payload["tool_input"] = new JsonObject { ["command"] = command };
        return payload;
    }

    [Fact]
    public async Task PermissionRequest_IsHeldUntilTheNotchDecides()
    {
        var tracker = new ClaudeSessionTracker();
        var request = Hook("PermissionRequest", tool: "Bash", command: "npm test");
        request["permission_suggestions"] = new JsonArray("Bash(npm test)");

        Task<string> answer = tracker.HandleHookAsync(request);
        Assert.False(answer.IsCompleted);

        var pending = Assert.Single(tracker.GetPending());
        Assert.Equal("Bash", pending.Tool);
        Assert.Equal("npm test", pending.Detail);
        Assert.Equal("Bash(npm test)", pending.AlwaysLabel);
        Assert.Equal(ClaudeSessionStatus.Waiting, Assert.Single(tracker.GetSessions()).Status);

        Assert.True(tracker.Decide(pending.Id, ClaudeDecision.Always));
        var body = JsonNode.Parse(await answer)!;
        Assert.Equal("PermissionRequest", (string?)body["hookSpecificOutput"]!["hookEventName"]);
        Assert.Equal("allow", (string?)body["hookSpecificOutput"]!["decision"]!["behavior"]);
        Assert.Equal("Bash(npm test)", (string?)body["hookSpecificOutput"]!["decision"]!["updatedPermissions"]![0]);
        Assert.Empty(tracker.GetPending());
        Assert.False(tracker.Decide(pending.Id, ClaudeDecision.Deny));
    }

    [Fact]
    public async Task Deny_SendsDenyWithAMessage()
    {
        var tracker = new ClaudeSessionTracker();
        Task<string> answer = tracker.HandleHookAsync(Hook("PermissionRequest", tool: "Bash", command: "rm -rf build"));
        tracker.Decide(Assert.Single(tracker.GetPending()).Id, ClaudeDecision.Deny);

        var decision = JsonNode.Parse(await answer)!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("deny", (string?)decision["behavior"]);
        Assert.False(string.IsNullOrWhiteSpace((string?)decision["message"]));
    }

    [Fact]
    public async Task AnsweringInTheTerminal_ReleasesTheHeldRequestWithoutADecision()
    {
        var tracker = new ClaudeSessionTracker();
        Task<string> held = tracker.HandleHookAsync(Hook("PermissionRequest", tool: "Bash", command: "npm test"));

        // Another tool finishing doesn't answer this one.
        await tracker.HandleHookAsync(Hook("PostToolUse", tool: "Bash", command: "git status"));
        Assert.False(held.IsCompleted);

        await tracker.HandleHookAsync(Hook("PostToolUse", tool: "Bash", command: "npm test"));
        Assert.Equal(ClaudeSessionTracker.NoDecision, await held);
        Assert.Empty(tracker.GetPending());
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("UserPromptSubmit")]
    [InlineData("SessionEnd")]
    public async Task TheSessionMovingOn_ReleasesHeldRequests(string hookEvent)
    {
        var tracker = new ClaudeSessionTracker();
        Task<string> held = tracker.HandleHookAsync(Hook("PermissionRequest", tool: "Bash", command: "npm test"));
        Task<string> other = tracker.HandleHookAsync(Hook("PermissionRequest", session: "s2", tool: "Bash", command: "ls"));

        await tracker.HandleHookAsync(Hook(hookEvent));

        Assert.Equal(ClaudeSessionTracker.NoDecision, await held);
        Assert.False(other.IsCompleted);
        tracker.ReleaseAll();
        Assert.Equal(ClaudeSessionTracker.NoDecision, await other);
    }

    [Fact]
    public async Task Plans_AndQuestionsWithoutOptions_StayInTheTerminal()
    {
        var tracker = new ClaudeSessionTracker();
        Assert.Equal(ClaudeSessionTracker.NoDecision, await tracker.HandleHookAsync(Hook("PermissionRequest", tool: "ExitPlanMode")));
        Assert.Equal(ClaudeSessionTracker.NoDecision, await tracker.HandleHookAsync(Hook("PermissionRequest", tool: "AskUserQuestion")));
        Assert.Empty(tracker.GetPending());
    }

    [Fact]
    public async Task Plan_IsHeld_AndCanBeSentBackToKeepPlanning()
    {
        var tracker = new ClaudeSessionTracker();
        var request = Hook("PermissionRequest", tool: "ExitPlanMode");
        request["tool_input"] = new JsonObject { ["plan"] = "## Add a Pi tab\n\n- Client for the dashboard\n- Tiles for switches" };
        Task<string> answer = tracker.HandleHookAsync(request);

        var pending = Assert.Single(tracker.GetPending());
        Assert.StartsWith("## Add a Pi tab", pending.Plan);
        Assert.Equal("Add a Pi tab", pending.Detail);
        Assert.True(tracker.Decide(pending.Id, ClaudeDecision.Deny));

        var verdict = JsonNode.Parse(await answer)!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("deny", verdict["behavior"]!.GetValue<string>());
        Assert.Contains("keep planning", verdict["message"]!.GetValue<string>());
    }

    private static JsonObject QuestionInput() => new()
    {
        ["questions"] = new JsonArray(
            new JsonObject
            {
                ["question"] = "Which database?",
                ["header"] = "Database",
                ["options"] = new JsonArray(
                    new JsonObject { ["label"] = "Postgres", ["description"] = "Relational" },
                    new JsonObject { ["label"] = "SQLite" }),
                ["multiSelect"] = false
            },
            new JsonObject
            {
                ["question"] = "Which extras?",
                ["header"] = "Extras",
                ["options"] = new JsonArray(new JsonObject { ["label"] = "Auth" }, new JsonObject { ["label"] = "Search" }),
                ["multiSelect"] = true
            })
    };

    [Fact]
    public async Task Question_IsHeldAndAnsweredWithTheChosenLabels()
    {
        var tracker = new ClaudeSessionTracker();
        var request = Hook("PermissionRequest", tool: "AskUserQuestion");
        request["tool_input"] = QuestionInput();

        Task<string> answer = tracker.HandleHookAsync(request);
        Assert.False(answer.IsCompleted);
        var pending = Assert.Single(tracker.GetPending());
        Assert.Equal("Which database?", pending.Detail);
        Assert.Equal(2, pending.Questions!.Count);
        Assert.Equal("Database", pending.Questions[0].Header);
        Assert.Equal("Relational", pending.Questions[0].Options[0].Description);
        Assert.Null(pending.Questions[0].Options[1].Description);
        Assert.True(pending.Questions[1].MultiSelect);

        Assert.True(tracker.Answer(pending.Id, new System.Collections.Generic.Dictionary<string, string>
        {
            ["Which database?"] = "SQLite",
            ["Which extras?"] = "Auth, Search"
        }));
        var decision = JsonNode.Parse(await answer)!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("allow", (string?)decision["behavior"]);
        // updatedInput replaces the input, so the questions travel with the answers.
        Assert.True(JsonNode.DeepEquals(QuestionInput()["questions"], decision["updatedInput"]!["questions"]));
        Assert.Equal("SQLite", (string?)decision["updatedInput"]!["answers"]!["Which database?"]);
        Assert.Equal("Auth, Search", (string?)decision["updatedInput"]!["answers"]!["Which extras?"]);
        Assert.Equal(ClaudeSessionStatus.Working, Assert.Single(tracker.GetSessions()).Status);
    }

    [Fact]
    public async Task QuestionAnsweredInTheTerminal_IsReleased()
    {
        var tracker = new ClaudeSessionTracker();
        var request = Hook("PermissionRequest", tool: "AskUserQuestion");
        request["tool_input"] = QuestionInput();
        Task<string> held = tracker.HandleHookAsync(request);

        // The tool's own input comes back with the answers added.
        var done = Hook("PostToolUse", tool: "AskUserQuestion");
        var input = QuestionInput();
        input["answers"] = new JsonObject { ["Which database?"] = "Postgres" };
        done["tool_input"] = input;
        await tracker.HandleHookAsync(done);

        Assert.Equal(ClaudeSessionTracker.NoDecision, await held);
        Assert.Empty(tracker.GetPending());
    }

    [Fact]
    public async Task Stop_ReportsTheFinishedTurn()
    {
        var tracker = new ClaudeSessionTracker();
        ClaudeSession? finished = null;
        tracker.TurnFinished += session => finished = session;

        await tracker.HandleHookAsync(Hook("UserPromptSubmit"));
        Assert.Null(finished);
        await tracker.HandleHookAsync(Hook("Stop"));

        Assert.NotNull(finished);
        Assert.Equal("notch-app", finished!.Project);
        Assert.Equal(ClaudeSessionStatus.Done, finished.Status);
    }

    [Fact]
    public async Task Session_IsNamedAfterItsTranscriptTitle()
    {
        DateTime now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        string? title = null;
        int reads = 0;
        var tracker = new ClaudeSessionTracker(() => now, path =>
        {
            reads++;
            Assert.Equal(@"C:\t\s1.jsonl", path);
            return title;
        });
        var prompt = Hook("UserPromptSubmit");
        prompt["transcript_path"] = @"C:\t\s1.jsonl";

        await tracker.HandleHookAsync(prompt);
        Assert.Equal("notch-app", Assert.Single(tracker.GetSessions()).Name);

        // Claude Code names it a little later: tool calls look again now and then, not on every event.
        title = "Fix the tab bar";
        await tracker.HandleHookAsync(Hook("PreToolUse", tool: "Bash", command: "ls"));
        Assert.Equal(1, reads);
        now += TimeSpan.FromSeconds(20);
        await tracker.HandleHookAsync(Hook("PreToolUse", tool: "Bash", command: "ls"));

        var session = Assert.Single(tracker.GetSessions());
        Assert.Equal(2, reads);
        Assert.Equal("Fix the tab bar", session.Name);
        Assert.Equal("notch-app", session.Project);
    }

    [Fact]
    public async Task Project_StaysTheFolderTheSessionStartedIn()
    {
        var tracker = new ClaudeSessionTracker();
        await tracker.HandleHookAsync(Hook("SessionStart"));
        var moved = Hook("PreToolUse", tool: "Bash", command: "ls");
        moved["cwd"] = @"C:\work\notch-app\bin";
        await tracker.HandleHookAsync(moved);

        Assert.Equal("notch-app", Assert.Single(tracker.GetSessions()).Project);
    }

    [Fact]
    public async Task Sweep_ReleasesRequestsClaudeCodeStoppedWaitingFor()
    {
        DateTime now = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var tracker = new ClaudeSessionTracker(() => now);
        Task<string> held = tracker.HandleHookAsync(Hook("PermissionRequest", tool: "Bash", command: "npm test"));

        now += ClaudeSessionTracker.MaxHold - TimeSpan.FromSeconds(1);
        tracker.Sweep();
        Assert.False(held.IsCompleted);

        now += TimeSpan.FromSeconds(2);
        tracker.Sweep();
        Assert.Equal(ClaudeSessionTracker.NoDecision, await held);
    }

    [Fact]
    public async Task Session_ShowsProjectPromptActivityAndReply()
    {
        var tracker = new ClaudeSessionTracker();
        var prompt = Hook("UserPromptSubmit");
        prompt["prompt"] = "Fix the **flaky**\n  test";
        await tracker.HandleHookAsync(prompt);

        var edit = Hook("PreToolUse", tool: "Edit");
        edit["tool_input"] = new JsonObject { ["file_path"] = @"C:\work\notch-app\src\Main.cs" };
        await tracker.HandleHookAsync(edit);

        var session = Assert.Single(tracker.GetSessions());
        Assert.Equal("notch-app", session.Project);
        Assert.Equal("Fix the flaky test", session.Prompt);
        Assert.Equal("Edit: Main.cs", session.Activity);
        Assert.Equal(ClaudeSessionStatus.Working, session.Status);
        Assert.NotNull(session.TurnStartedUtc);

        var stop = Hook("Stop");
        stop["last_assistant_message"] = "All green now.";
        await tracker.HandleHookAsync(stop);
        session = Assert.Single(tracker.GetSessions());
        Assert.Equal(ClaudeSessionStatus.Done, session.Status);
        Assert.Equal("All green now.", session.Summary);
    }

    [Fact]
    public void AlwaysLabel_ReadsRulesModesAndDirectories()
    {
        var suggestions = JsonNode.Parse("""
            [
              { "type": "addRules", "rules": [{ "toolName": "Bash", "ruleContent": "npm test" }], "behavior": "allow", "destination": "localSettings" },
              { "type": "setMode", "mode": "acceptEdits", "destination": "session" },
              { "type": "addDirectories", "directories": ["C:/other"], "destination": "session" }
            ]
            """);
        Assert.Equal("Bash(npm test), acceptEdits, C:/other", ClaudeSessionTracker.AlwaysLabel(suggestions));
        Assert.Null(ClaudeSessionTracker.AlwaysLabel(new JsonArray()));
        Assert.Null(ClaudeSessionTracker.AlwaysLabel(null));
    }

    [Fact]
    public void AllowWithoutSuggestions_IsAPlainAllow()
    {
        var decision = JsonNode.Parse(ClaudeSessionTracker.DecisionBody(ClaudeDecision.Always, null))!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("allow", (string?)decision["behavior"]);
        Assert.Null(decision["updatedPermissions"]);
    }
}
