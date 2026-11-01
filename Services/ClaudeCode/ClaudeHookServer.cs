using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace VNotch.Services.ClaudeCode;

/// <summary>
/// Loopback HTTP endpoint for Claude Code's <c>type: "http"</c> hooks, which POST the same JSON a command
/// hook gets on stdin. A plain socket server, so no URL reservation (and no admin rights) is needed.
/// </summary>
internal sealed class ClaudeHookServer : IDisposable
{
    private const string LogTag = "CLAUDE-HOOKS";
    public const int DefaultPort = 47833;
    public const string HookPath = "/vnotch/claude";
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(10);

    private readonly ClaudeSessionTracker _tracker;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public ClaudeHookServer(ClaudeSessionTracker tracker, int port = DefaultPort)
    {
        _tracker = tracker;
        Port = port;
    }

    /// <summary>What Claude Code's settings point at.</summary>
    public static string HookUrl => $"http://127.0.0.1:{DefaultPort}{HookPath}";

    /// <summary>The port in use; 0 asks Windows for a free one (tests).</summary>
    public int Port { get; private set; }

    /// <summary>False when the port was taken (another V-Notch instance, for one).</summary>
    public bool IsListening { get; private set; }

    public void Start()
    {
        if (_listener != null) return;
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            IsListening = true;
            _ = AcceptLoopAsync(_listener, _cts.Token);
        }
        catch (SocketException ex)
        {
            RuntimeLog.Warn(LogTag, $"Port {Port} unavailable: {ex.Message}");
            _listener = null;
            IsListening = false;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch (SocketException) { /* Already closed */ }
        _listener = null;
        IsListening = false;
        _tracker.ReleaseAll();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn(LogTag, $"Accept failed: {ex.Message}");
                continue;
            }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                HttpRequest? request;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    readTimeout.CancelAfter(ReadTimeout);
                    request = await ReadRequestAsync(stream, readTimeout.Token).ConfigureAwait(false);
                }
                if (request == null) return;

                int status = 404;
                string body = ClaudeSessionTracker.NoDecision;
                // Claude Code sends JSON; a web page can't without a preflight we never answer.
                if (request.Method == "POST" && request.Path == HookPath &&
                    request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                {
                    status = 200;
                    if (TryParse(request.Body) is { } payload)
                        body = await _tracker.HandleHookAsync(payload).WaitAsync(ct).ConfigureAwait(false);
                }
                await WriteResponseAsync(stream, status, body, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Claude Code went away or gave up on the hook; nothing to answer.
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn(LogTag, $"Hook request failed: {ex.Message}");
            }
        }
    }

    private static JsonObject? TryParse(byte[] body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal sealed record HttpRequest(string Method, string Path, string ContentType, byte[] Body);

    internal static async Task<HttpRequest?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var head = new MemoryStream();
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) return null;
            head.Write(buffer, 0, read);
            if (head.Length > MaxHeaderBytes + MaxBodyBytes) return null;
            headerEnd = IndexOfHeaderEnd(head.GetBuffer(), (int)head.Length);
            if (headerEnd < 0 && head.Length > MaxHeaderBytes) return null;
        }

        byte[] raw = head.GetBuffer();
        string headerText = Encoding.ASCII.GetString(raw, 0, headerEnd);
        string[] lines = headerText.Split("\r\n");
        string[] requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2) return null;

        string contentType = "";
        int contentLength = 0;
        bool chunked = false;
        for (int i = 1; i < lines.Length; i++)
        {
            int colon = lines[i].IndexOf(':');
            if (colon <= 0) continue;
            string name = lines[i][..colon].Trim();
            string value = lines[i][(colon + 1)..].Trim();
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) contentType = value;
            else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out contentLength);
            else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                chunked = value.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        }
        if (contentLength < 0 || contentLength > MaxBodyBytes) return null;

        // Whatever came in after the headers is the start of the body.
        using var rest = new MemoryStream();
        int bodyStart = headerEnd + 4;
        rest.Write(raw, bodyStart, (int)head.Length - bodyStart);

        byte[] body;
        if (chunked)
        {
            body = await ReadChunkedAsync(stream, rest, buffer, ct).ConfigureAwait(false) ?? [];
        }
        else
        {
            while (rest.Length < contentLength)
            {
                int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) return null;
                rest.Write(buffer, 0, read);
            }
            body = rest.ToArray();
            if (body.Length > contentLength) Array.Resize(ref body, contentLength);
        }

        string path = requestLine[1];
        int query = path.IndexOf('?');
        if (query >= 0) path = path[..query];
        return new HttpRequest(requestLine[0].ToUpperInvariant(), path, contentType, body);
    }

    private static async Task<byte[]?> ReadChunkedAsync(Stream stream, MemoryStream pending, byte[] buffer, CancellationToken ct)
    {
        using var body = new MemoryStream();
        while (true)
        {
            // Make sure the whole chunk-size line is there.
            int lineEnd;
            while ((lineEnd = IndexOfCrLf(pending.GetBuffer(), 0, (int)pending.Length)) < 0)
            {
                int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) return null;
                pending.Write(buffer, 0, read);
            }
            string sizeText = Encoding.ASCII.GetString(pending.GetBuffer(), 0, lineEnd);
            int semicolon = sizeText.IndexOf(';');
            if (semicolon >= 0) sizeText = sizeText[..semicolon];
            if (!int.TryParse(sizeText.Trim(), System.Globalization.NumberStyles.HexNumber, null, out int size) || size < 0) return null;
            if (size == 0) return body.ToArray();
            if (body.Length + size > MaxBodyBytes) return null;

            int needed = lineEnd + 2 + size + 2;
            while (pending.Length < needed)
            {
                int read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) return null;
                pending.Write(buffer, 0, read);
            }
            body.Write(pending.GetBuffer(), lineEnd + 2, size);

            byte[] leftover = pending.GetBuffer()[needed..(int)pending.Length];
            pending.SetLength(0);
            pending.Write(leftover, 0, leftover.Length);
        }
    }

    private static async Task WriteResponseAsync(Stream stream, int status, string body, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string reason = status == 200 ? "OK" : "Not Found";
        string header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (int i = 0; i + 3 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
        }
        return -1;
    }

    private static int IndexOfCrLf(byte[] data, int start, int length)
    {
        for (int i = start; i + 1 < length; i++)
        {
            if (data[i] == '\r' && data[i + 1] == '\n') return i;
        }
        return -1;
    }
}
