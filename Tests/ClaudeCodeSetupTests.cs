using System.Text.Json.Nodes;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

public class ClaudeCodeSetupTests
{
    private const string Url = "http://127.0.0.1:47833/vnotch/claude";

    [Fact]
    public void Install_AddsOneHttpHookPerEvent_AndKeepsTheUsersOwn()
    {
        var settings = JsonNode.Parse("""
            {
              "model": "opus",
              "hooks": {
                "Stop": [ { "hooks": [ { "type": "command", "command": "notify.exe" } ] } ]
              }
            }
            """)!.AsObject();

        ClaudeCodeSetup.Install(settings, Url);
        ClaudeCodeSetup.Install(settings, Url); // twice is still once

        Assert.True(ClaudeCodeSetup.IsInstalled(settings, Url));
        Assert.Equal("opus", (string?)settings["model"]);
        var stop = settings["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("notify.exe", (string?)stop[0]!["hooks"]![0]!["command"]);
        foreach (var hookEvent in ClaudeCodeSetup.Events)
        {
            var groups = settings["hooks"]![hookEvent]!.AsArray();
            var ours = groups[^1]!["hooks"]![0]!;
            Assert.Equal("http", (string?)ours["type"]);
            Assert.Equal(Url, (string?)ours["url"]);
            int expected = hookEvent == "PermissionRequest" ? ClaudeCodeSetup.PermissionTimeoutSeconds : ClaudeCodeSetup.HookTimeoutSeconds;
            Assert.Equal(expected, (int)ours["timeout"]!);
        }
    }

    [Fact]
    public void Uninstall_RemovesOnlyOurHooks()
    {
        var settings = JsonNode.Parse("""
            { "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "notify.exe" } ] } ] } }
            """)!.AsObject();
        ClaudeCodeSetup.Install(settings, Url);

        ClaudeCodeSetup.Uninstall(settings, Url);

        Assert.False(ClaudeCodeSetup.IsInstalled(settings, Url));
        var hooks = settings["hooks"]!.AsObject();
        Assert.Single(hooks);
        Assert.Equal("notify.exe", (string?)hooks["Stop"]![0]!["hooks"]![0]!["command"]);
    }

    [Fact]
    public void Uninstall_DropsTheHooksBlockWhenNothingIsLeft()
    {
        var settings = new JsonObject();
        ClaudeCodeSetup.Install(settings, Url);
        ClaudeCodeSetup.Uninstall(settings, Url);
        Assert.False(settings.ContainsKey("hooks"));
    }

    [Fact]
    public void IsInstalled_NeedsEveryEvent()
    {
        var settings = new JsonObject();
        ClaudeCodeSetup.Install(settings, Url);
        settings["hooks"]!.AsObject().Remove("PermissionRequest");
        Assert.False(ClaudeCodeSetup.IsInstalled(settings, Url));
    }
}
