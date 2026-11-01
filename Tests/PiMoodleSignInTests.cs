using System.IO;
using System.Net;
using System.Text;
using VNotch.Services.ControlCenter;
using Xunit;

namespace VNotch.Tests;

/// <summary>
/// Opening a Moodle page signed in: the Pi swaps the link for Moodle's one-time sign-in link. Only ever a page of
/// the same Moodle comes back; anything else, or no Pi, leaves the page link.
/// </summary>
public sealed class PiMoodleSignInTests : IDisposable
{
    private const string Moodle = "https://edufs.edu.htl-leonding.ac.at/moodle";
    private const string Page = Moodle + "/mod/assign/view.php?id=232546";

    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private readonly CancellationTokenSource _stop = new();
    private volatile string _reply = "{}";
    private volatile string _seenBody = "";

    public PiMoodleSignInTests()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        _prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _ = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception) { return; }
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                _seenBody = context.Request.Url!.AbsolutePath + " " + await reader.ReadToEndAsync().ConfigureAwait(false);
            byte[] bytes = Encoding.UTF8.GetBytes(_reply);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }

    [Fact]
    public async Task TheSignInLinkFromThePiOpens()
    {
        string signIn = Moodle + "/admin/tool/mobile/autologin.php?userid=42&key=K&urltogo=" + Uri.EscapeDataString(Page);
        _reply = $$"""{"url":"{{signIn}}","autologin":true}""";
        using var client = new PiDashboardClient(() => _prefix);
        Assert.Equal(signIn, await client.SignedInMoodleLinkAsync(Page, Moodle));
        Assert.StartsWith("/api/moodle/autologin ", _seenBody);
        Assert.Contains("232546", _seenBody);
    }

    [Theory]
    [InlineData("https://evil.example/moodle/admin/tool/mobile/autologin.php")]
    [InlineData("https://edufs.edu.htl-leonding.ac.at/moodlex/phish")]
    [InlineData("")]
    public async Task AnythingButASamePageLinkLeavesThePage(string reply)
    {
        _reply = $$"""{"url":"{{reply}}"}""";
        using var client = new PiDashboardClient(() => _prefix);
        Assert.Equal(Page, await client.SignedInMoodleLinkAsync(Page, Moodle));
    }

    private const string Untis = "https://htl-leonding.webuntis.com/today";

    [Fact]
    public async Task Untis_OpensThePisOneTimeSignInPage()
    {
        _reply = """{"path":"/untis-login/AbC_123-xyz","validSeconds":60}""";
        using var client = new PiDashboardClient(() => _prefix);
        Assert.Equal(_prefix + "untis-login/AbC_123-xyz", await client.SignedInSchoolLinkAsync(Untis, Moodle, Untis));
        Assert.StartsWith("/api/untis/login ", _seenBody);
    }

    [Theory]
    [InlineData("https://evil.example/untis-login/x")]
    [InlineData("/api/todos")]
    [InlineData("/untis-login/")]
    [InlineData("/untis-login/../api/x")]
    public async Task Untis_AnythingButTheSignInPageLeavesUntis(string path)
    {
        _reply = $$"""{"path":"{{path}}"}""";
        using var client = new PiDashboardClient(() => _prefix);
        Assert.Equal(Untis, await client.SignedInSchoolLinkAsync(Untis, Moodle, Untis));
    }

    [Fact]
    public async Task SchoolLinks_GoToTheirOwnSignIn()
    {
        string signIn = Moodle + "/admin/tool/mobile/autologin.php?userid=1&key=K";
        _reply = $$"""{"url":"{{signIn}}"}""";
        using var client = new PiDashboardClient(() => _prefix);
        Assert.Equal(signIn, await client.SignedInSchoolLinkAsync(Page, Moodle, Untis));
        Assert.StartsWith("/api/moodle/autologin ", _seenBody);
        _seenBody = "";
        Assert.Equal("https://example.com/x", await client.SignedInSchoolLinkAsync("https://example.com/x", Moodle, Untis));
        Assert.Equal("", _seenBody);
    }

    [Fact]
    public async Task NotMoodleOrNoPi_LeavesTheLink()
    {
        using var client = new PiDashboardClient(() => _prefix);
        _seenBody = "";
        Assert.Equal(Untis, await client.SignedInMoodleLinkAsync(Untis, Moodle));
        Assert.Equal("", _seenBody);

        using var unreachable = new PiDashboardClient(() => "");
        Assert.Equal(Page, await unreachable.SignedInMoodleLinkAsync(Page, Moodle));
    }
}
