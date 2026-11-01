using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

public class ClaudeHookServerTests
{
    [Fact]
    public async Task ReadRequest_ReadsAContentLengthBody()
    {
        const string body = """{"session_id":"s1"}""";
        var raw = "POST /vnotch/claude?x=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\n" +
                  $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));

        var request = await ClaudeHookServer.ReadRequestAsync(stream, CancellationToken.None);

        Assert.NotNull(request);
        Assert.Equal("POST", request!.Method);
        Assert.Equal(ClaudeHookServer.HookPath, request.Path);
        Assert.StartsWith("application/json", request.ContentType);
        Assert.Equal(body, Encoding.UTF8.GetString(request.Body));
    }

    [Fact]
    public async Task ReadRequest_ReadsAChunkedBody()
    {
        var raw = "POST /vnotch/claude HTTP/1.1\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n" +
                  "5\r\n{\"a\":\r\n3\r\n12}\r\n0\r\n\r\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(raw));

        var request = await ClaudeHookServer.ReadRequestAsync(stream, CancellationToken.None);

        Assert.Equal("""{"a":12}""", Encoding.ASCII.GetString(request!.Body));
    }

    [Fact]
    public async Task Server_AnswersHooksOverHttp_AndHoldsPermissionRequests()
    {
        var tracker = new ClaudeSessionTracker();
        using var server = new ClaudeHookServer(tracker, port: 0);
        server.Start();
        Assert.True(server.IsListening);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        string url = $"http://127.0.0.1:{server.Port}{ClaudeHookServer.HookPath}";

        using var start = await http.PostAsync(url, Json("""{"session_id":"s1","hook_event_name":"SessionStart","cwd":"C:\\work\\app"}"""));
        Assert.Equal("{}", await start.Content.ReadAsStringAsync());
        Assert.Equal("app", Assert.Single(tracker.GetSessions()).Project);

        Task<HttpResponseMessage> permission = http.PostAsync(url,
            Json("""{"session_id":"s1","hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"npm test"}}"""));
        var pending = await WaitForPendingAsync(tracker);
        Assert.False(permission.IsCompleted);

        tracker.Decide(pending.Id, ClaudeDecision.Allow);
        using var answer = await permission;
        var decision = JsonNode.Parse(await answer.Content.ReadAsStringAsync())!["hookSpecificOutput"]!["decision"]!;
        Assert.Equal("allow", (string?)decision["behavior"]);
    }

    [Fact]
    public async Task Server_IgnoresRequestsThatAreNotJson()
    {
        var tracker = new ClaudeSessionTracker();
        using var server = new ClaudeHookServer(tracker, port: 0);
        server.Start();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        using var response = await http.PostAsync($"http://127.0.0.1:{server.Port}{ClaudeHookServer.HookPath}",
            new StringContent("""{"session_id":"s1","hook_event_name":"SessionStart"}""", Encoding.UTF8, "text/plain"));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(tracker.GetSessions());
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<ClaudePermissionRequest> WaitForPendingAsync(ClaudeSessionTracker tracker)
    {
        for (int i = 0; i < 200; i++)
        {
            var pending = tracker.GetPending().FirstOrDefault();
            if (pending != null) return pending;
            await Task.Delay(25);
        }
        throw new TimeoutException("The permission request never arrived.");
    }
}
