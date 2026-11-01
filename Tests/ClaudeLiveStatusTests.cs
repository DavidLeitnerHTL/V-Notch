using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

/// <summary>The word the closed notch shows for a session, from the hook events the tracker saw.</summary>
public class ClaudeLiveStatusTests
{
    private static JsonObject Hook(string hookEvent, string? tool = null)
    {
        var payload = new JsonObject { ["session_id"] = "s1", ["hook_event_name"] = hookEvent, ["cwd"] = @"C:\work\app" };
        if (tool != null)
        {
            payload["tool_name"] = tool;
            payload["tool_input"] = new JsonObject { ["command"] = "x", ["file_path"] = @"C:\work\app\a.cs" };
        }
        return payload;
    }

    private static string StatusOf(ClaudeSessionTracker tracker) =>
        MainWindow.ClaudeLiveStatusKey(Assert.Single(tracker.GetSessions()));

    [Theory]
    [InlineData("Edit", "claude.status.writing")]
    [InlineData("Write", "claude.status.writing")]
    [InlineData("Read", "claude.status.reading")]
    [InlineData("Grep", "claude.status.reading")]
    [InlineData("Bash", "claude.status.running")]
    [InlineData("PowerShell", "claude.status.running")]
    [InlineData("WebSearch", "claude.status.browsing")]
    [InlineData("Agent", "claude.status.delegating")]
    [InlineData("TodoWrite", "claude.status.planning")]
    [InlineData("mcp__github__get_me", "claude.status.working")]
    public async Task ARunningTool_NamesWhatClaudeDoes(string tool, string expected)
    {
        var tracker = new ClaudeSessionTracker();
        await tracker.HandleHookAsync(Hook("UserPromptSubmit"));
        await tracker.HandleHookAsync(Hook("PreToolUse", tool));
        Assert.Equal(expected, StatusOf(tracker));
    }

    [Fact]
    public async Task BetweenTools_ClaudeIsThinking()
    {
        var tracker = new ClaudeSessionTracker();
        await tracker.HandleHookAsync(Hook("UserPromptSubmit"));
        Assert.Equal("claude.status.thinking", StatusOf(tracker));
        await tracker.HandleHookAsync(Hook("PreToolUse", "Edit"));
        await tracker.HandleHookAsync(Hook("PostToolUse", "Edit"));
        Assert.Equal("claude.status.thinking", StatusOf(tracker));
    }

    [Fact]
    public async Task APermissionPrompt_IsWaiting()
    {
        var tracker = new ClaudeSessionTracker();
        _ = tracker.HandleHookAsync(Hook("PermissionRequest", "Bash"));
        await Task.Yield();
        Assert.Equal("claude.status.waiting", StatusOf(tracker));
    }

    [Fact]
    public async Task AskUserQuestion_IsAQuestion()
    {
        var tracker = new ClaudeSessionTracker();
        var ask = Hook("PermissionRequest");
        ask["tool_name"] = "AskUserQuestion";
        ask["tool_input"] = JsonNode.Parse("""
            {"questions":[{"question":"Which?","header":"Pick","multiSelect":false,
              "options":[{"label":"A","description":""},{"label":"B","description":""}]}]}
            """);
        _ = tracker.HandleHookAsync(ask);
        await Task.Yield();
        Assert.Equal("claude.status.question", StatusOf(tracker));
    }
}
