using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VNotch.Services.ClaudeCode;

/// <summary>
/// Adds and removes V-Notch's hooks in Claude Code's user settings (<c>~/.claude/settings.json</c>).
/// Only runs on an explicit click. Writes a timestamped backup first, keeps everything else as it was
/// and touches nothing but our own entries.
/// </summary>
internal static class ClaudeCodeSetup
{
    private const string LogTag = "CLAUDE-SETUP";

    /// <summary>Hook events the notch listens to.</summary>
    internal static readonly string[] Events =
    [
        "SessionStart",
        "UserPromptSubmit",
        "PreToolUse",
        "PostToolUse",
        "PermissionRequest",
        "Notification",
        "Stop",
        "SessionEnd"
    ];

    /// <summary>Most hooks only report, so they shouldn't hold Claude Code up.</summary>
    internal const int HookTimeoutSeconds = 3;

    /// <summary>The permission hook waits while the user decides in the notch.</summary>
    internal const int PermissionTimeoutSeconds = 600;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // Keep the user's paths and text readable instead of \u-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ClaudeDirectory
    {
        get
        {
            string? custom = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(custom)) return custom;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }
    }

    private static string SettingsPath => Path.Combine(ClaudeDirectory, "settings.json");

    /// <summary>Claude Code is (or was) used on this PC.</summary>
    public static bool IsClaudeCodePresent => Directory.Exists(ClaudeDirectory);

    public static bool IsInstalled(string hookUrl)
    {
        try
        {
            return IsInstalled(ReadSettings(), hookUrl);
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool IsInstalled(JsonObject settings, string hookUrl)
    {
        if (settings["hooks"] is not JsonObject hooks) return false;
        foreach (var hookEvent in Events)
        {
            if (!HasOurHook(hooks[hookEvent] as JsonArray, hookUrl)) return false;
        }
        return true;
    }

    public static bool Install(string hookUrl)
    {
        try
        {
            var settings = ReadSettings();
            Install(settings, hookUrl);
            WriteSettings(settings);
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"Install failed: {ex.Message}");
            return false;
        }
    }

    public static bool Uninstall(string hookUrl)
    {
        try
        {
            var settings = ReadSettings();
            Uninstall(settings, hookUrl);
            WriteSettings(settings);
            return true;
        }
        catch (Exception ex)
        {
            RuntimeLog.Warn(LogTag, $"Uninstall failed: {ex.Message}");
            return false;
        }
    }

    internal static void Install(JsonObject settings, string hookUrl)
    {
        if (settings["hooks"] is not JsonObject hooks)
        {
            if (settings.ContainsKey("hooks")) throw new InvalidDataException("\"hooks\" is not an object");
            hooks = new JsonObject();
            settings["hooks"] = hooks;
        }

        foreach (var hookEvent in Events)
        {
            if (hooks[hookEvent] is not JsonArray groups)
            {
                if (hooks.ContainsKey(hookEvent)) throw new InvalidDataException($"\"{hookEvent}\" is not a list");
                groups = new JsonArray();
                hooks[hookEvent] = groups;
            }

            int timeout = hookEvent == "PermissionRequest" ? PermissionTimeoutSeconds : HookTimeoutSeconds;
            bool present = false;
            foreach (var group in groups)
            {
                if (group?["hooks"] is not JsonArray list) continue;
                foreach (var hook in list)
                {
                    if (!IsOurHook(hook, hookUrl)) continue;
                    hook!["timeout"] = timeout; // An older setup: bring the timeout up to date.
                    present = true;
                }
            }
            if (present) continue;

            groups.Add(new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject { ["type"] = "http", ["url"] = hookUrl, ["timeout"] = timeout }
                }
            });
        }
    }

    internal static void Uninstall(JsonObject settings, string hookUrl)
    {
        if (settings["hooks"] is not JsonObject hooks) return;
        foreach (var hookEvent in Events)
        {
            if (hooks[hookEvent] is not JsonArray groups) continue;
            for (int g = groups.Count - 1; g >= 0; g--)
            {
                if (groups[g]?["hooks"] is not JsonArray list) continue;
                for (int h = list.Count - 1; h >= 0; h--)
                {
                    if (IsOurHook(list[h], hookUrl)) list.RemoveAt(h);
                }
                if (list.Count == 0) groups.RemoveAt(g);
            }
            if (groups.Count == 0) hooks.Remove(hookEvent);
        }
        if (hooks.Count == 0) settings.Remove("hooks");
    }

    private static bool HasOurHook(JsonArray? groups, string hookUrl)
    {
        if (groups == null) return false;
        foreach (var group in groups)
        {
            if (group?["hooks"] is not JsonArray list) continue;
            foreach (var hook in list)
                if (IsOurHook(hook, hookUrl)) return true;
        }
        return false;
    }

    private static bool IsOurHook(JsonNode? hook, string hookUrl) =>
        hook is JsonObject obj && obj["url"] is JsonValue url &&
        url.TryGetValue(out string? value) && string.Equals(value, hookUrl, StringComparison.OrdinalIgnoreCase);

    private static JsonObject ReadSettings()
    {
        string path = SettingsPath;
        if (!File.Exists(path)) return new JsonObject();
        string text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text, documentOptions: ReadOptions) as JsonObject
            ?? throw new InvalidDataException("settings.json is not an object");
    }

    private static void WriteSettings(JsonObject settings)
    {
        string path = SettingsPath;
        if (File.Exists(path))
        {
            string backup = Path.Combine(ClaudeDirectory, $"settings.json.vnotch-backup-{DateTime.Now:yyyyMMdd-HHmmss}");
            File.Copy(path, backup, overwrite: true);
        }
        else
        {
            Directory.CreateDirectory(ClaudeDirectory);
        }

        string temp = path + ".vnotch-tmp";
        File.WriteAllText(temp, settings.ToJsonString(WriteOptions) + Environment.NewLine);
        File.Move(temp, path, overwrite: true);
    }
}
