using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace VNotch.Services.ClaudeCode;

/// <summary>One line of a change preview: '-' removed, '+' added.</summary>
internal readonly record struct ClaudeChangeLine(char Sign, string Text);

/// <summary>
/// What an Edit, MultiEdit or Write call would change, as removed and added lines, so it can be judged in
/// the notch before allowing it. Lines both sides share at the start and end are left out.
/// </summary>
internal static class ClaudeChangePreview
{
    private const int MaxLines = 200;

    public static List<ClaudeChangeLine> Lines(string tool, JsonNode? input)
    {
        var lines = new List<ClaudeChangeLine>();
        if (input is not JsonObject call) return lines;
        switch (tool)
        {
            case "Edit":
                AddEdit(lines, call);
                break;
            case "MultiEdit":
                if (call["edits"] is JsonArray edits)
                {
                    foreach (var edit in edits)
                        if (edit is JsonObject one) AddEdit(lines, one);
                }
                break;
            case "Write":
                foreach (string line in Split(Text(call, "content")))
                    Add(lines, '+', line);
                break;
        }
        Unindent(lines);
        return lines;
    }

    private static void AddEdit(List<ClaudeChangeLine> lines, JsonObject edit)
    {
        string[] before = Split(Text(edit, "old_string"));
        string[] after = Split(Text(edit, "new_string"));
        int head = 0;
        while (head < before.Length && head < after.Length && before[head] == after[head]) head++;
        int tail = 0;
        while (tail < before.Length - head && tail < after.Length - head
               && before[before.Length - 1 - tail] == after[after.Length - 1 - tail])
            tail++;
        for (int i = head; i < before.Length - tail; i++) Add(lines, '-', before[i]);
        for (int i = head; i < after.Length - tail; i++) Add(lines, '+', after[i]);
    }

    private static void Add(List<ClaudeChangeLine> lines, char sign, string text)
    {
        if (lines.Count < MaxLines) lines.Add(new ClaudeChangeLine(sign, text.Replace("\t", "    ").TrimEnd()));
    }

    /// <summary>Drops the indentation every line shares, so deep code still fits the card.</summary>
    private static void Unindent(List<ClaudeChangeLine> lines)
    {
        int shared = int.MaxValue;
        foreach (var line in lines)
        {
            if (line.Text.Length == 0) continue;
            int indent = 0;
            while (indent < line.Text.Length && line.Text[indent] == ' ') indent++;
            shared = Math.Min(shared, indent);
        }
        if (shared is 0 or int.MaxValue) return;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Text.Length >= shared) lines[i] = line with { Text = line.Text[shared..] };
        }
    }

    private static string[] Split(string text) =>
        text.Length == 0 ? Array.Empty<string>() : text.Replace("\r\n", "\n").Split('\n');

    private static string Text(JsonObject obj, string key) =>
        obj[key] is JsonValue value && value.TryGetValue(out string? text) ? text : "";
}
