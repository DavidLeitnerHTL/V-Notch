using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VNotch.Services.ClaudeCode;

/// <summary>Reads what Claude Code keeps about a session at the end of its transcript.</summary>
internal static class ClaudeTranscript
{
    // Claude Code appends the session's title again after every turn, so it sits in the last lines.
    private const int TailBytes = 256 * 1024;
    private const int MaxLinesTried = 8;

    /// <summary>The title given with /rename, else the one Claude Code made up; null while there is none.</summary>
    public static string? ReadTitle(string transcriptPath)
    {
        if (!transcriptPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, stream.Length - TailBytes);
            var buffer = new byte[stream.Length - start];
            stream.Seek(start, SeekOrigin.Begin);
            stream.ReadExactly(buffer);
            return FindTitle(Encoding.UTF8.GetString(buffer));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// When the session's last prompt was sent (UTC), for a notch that joins a turn already running; null when
    /// the transcript has none. A long turn writes a lot after its prompt, so this looks further back step by step.
    /// </summary>
    public static DateTime? ReadTurnStart(string transcriptPath)
    {
        if (!transcriptPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            for (long window = TailBytes; ; window *= 4)
            {
                long start = Math.Max(0, length - window);
                var buffer = new byte[length - start];
                stream.Seek(start, SeekOrigin.Begin);
                stream.ReadExactly(buffer);
                if (FindTurnStart(Encoding.UTF8.GetString(buffer)) is { } found) return found;
                if (start == 0 || window >= MaxTurnScanBytes) return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const long MaxTurnScanBytes = 64L * 1024 * 1024;

    /// <summary>The time of the last prompt the user sent in these transcript lines (tool results and side chains do not count).</summary>
    internal static DateTime? FindTurnStart(string tail)
    {
        const string marker = "\"type\":\"user\"";
        int end = tail.Length;
        while (end > 0)
        {
            int at = tail.LastIndexOf(marker, end - 1, StringComparison.Ordinal);
            if (at < 0) return null;
            int lineStart = tail.LastIndexOf('\n', at) + 1;
            int lineEnd = tail.IndexOf('\n', at);
            string line = lineEnd < 0 ? tail[lineStart..] : tail[lineStart..lineEnd];
            end = lineStart;
            // Most user lines are tool results; skip them before parsing what can be a large output.
            if (line.Contains("\"type\":\"tool_result\"", StringComparison.Ordinal)) continue;
            if (Parse(line) is { } entry && IsPrompt(entry) && Text(entry, "timestamp") is { } stamp &&
                DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
                return utc;
        }
        return null;
    }

    private static bool IsPrompt(JsonObject entry)
    {
        if (Text(entry, "type") != "user" || Flag(entry, "isMeta") || Flag(entry, "isSidechain")) return false;
        var content = (entry["message"] as JsonObject)?["content"];
        if (content is JsonValue value) return value.TryGetValue(out string? text) && text.Trim().Length > 0;
        if (content is not JsonArray parts) return false;
        bool said = false;
        foreach (var part in parts)
        {
            string? kind = part is JsonObject p ? Text(p, "type") : null;
            if (kind == "tool_result") return false;
            if (kind is "text" or "image") said = true;
        }
        return said;
    }

    private static bool Flag(JsonObject entry, string key) =>
        entry[key] is JsonValue value && value.TryGetValue(out bool flag) && flag;

    internal static string? FindTitle(string tail) =>
        LastValue(tail, "custom-title", "customTitle") ?? LastValue(tail, "ai-title", "aiTitle");

    private static string? LastValue(string tail, string type, string field)
    {
        string marker = $"\"type\":\"{type}\"";
        int end = tail.Length;
        for (int tries = 0; tries < MaxLinesTried; tries++)
        {
            int at = end <= 0 ? -1 : tail.LastIndexOf(marker, end - 1, StringComparison.Ordinal);
            if (at < 0) return null;
            int lineStart = tail.LastIndexOf('\n', at) + 1;
            int lineEnd = tail.IndexOf('\n', at);
            string line = lineEnd < 0 ? tail[lineStart..] : tail[lineStart..lineEnd];
            end = lineStart;
            // The marker can also turn up quoted inside another entry: only the entry itself counts.
            if (Parse(line) is { } entry && Text(entry, "type") == type && Text(entry, field) is { Length: > 0 } value)
                return value;
        }
        return null;
    }

    private static JsonObject? Parse(string line)
    {
        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            // Cut off by the tail window or still being written.
            return null;
        }
    }

    private static string? Text(JsonObject entry, string key) =>
        entry[key] is JsonValue value && value.TryGetValue(out string? text) ? text.Trim() : null;
}
