using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace VNotch.Services.ClaudeCode;

internal enum ClaudeSessionStatus
{
    Idle,
    Working,
    Waiting,
    Done
}

internal enum ClaudeDecision
{
    Allow,
    Always,
    Deny
}

internal sealed class ClaudeSession
{
    public required string Id { get; init; }
    public string Cwd { get; set; } = "";
    public ClaudeSessionStatus Status { get; set; }
    /// <summary>What it is doing right now, e.g. "Bash: npm test". Null while thinking.</summary>
    public string? Activity { get; set; }
    /// <summary>The tool behind <see cref="Activity"/> ("Edit", "AskUserQuestion"…); null while thinking.</summary>
    public string? Tool { get; set; }
    public string? Prompt { get; set; }
    public string? Summary { get; set; }
    public DateTime LastEventUtc { get; set; }
    /// <summary>When the notch first heard from it.</summary>
    public DateTime StartedUtc { get; init; }
    public DateTime? TurnStartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    /// <summary>The transcript Claude Code writes for the session, where its title lives.</summary>
    public string? TranscriptPath { get; set; }
    /// <summary>The name Claude Code shows for the session: the one given with /rename, else its own.</summary>
    public string? Title { get; set; }
    internal DateTime TitleReadUtc { get; set; }

    /// <summary>What the notch calls the session: its title, or the folder until Claude Code named it.</summary>
    public string Name => Title is { Length: > 0 } title ? title : Project;

    public string Project
    {
        get
        {
            string trimmed = Cwd.TrimEnd('\\', '/');
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? (string.IsNullOrEmpty(trimmed) ? "Claude Code" : trimmed) : name;
        }
    }

    public ClaudeSession Clone() => (ClaudeSession)MemberwiseClone();
}

internal sealed record ClaudeQuestionOption(string Label, string? Description);

/// <summary>One of the multiple-choice questions an <c>AskUserQuestion</c> call puts to the user.</summary>
internal sealed record ClaudeQuestion(string Text, string Header, IReadOnlyList<ClaudeQuestionOption> Options, bool MultiSelect);

/// <summary>A held <c>PermissionRequest</c> hook: Claude Code waits for our HTTP answer.</summary>
internal sealed class ClaudePermissionRequest
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }
    public required string Tool { get; init; }
    /// <summary>The command, file, URL… a person decides on.</summary>
    public string? Detail { get; init; }
    /// <summary>The "don't ask again" rules Claude Code suggests, for the Always button.</summary>
    public string? AlwaysLabel { get; init; }
    /// <summary>Set for <c>AskUserQuestion</c>: answered with <see cref="ClaudeSessionTracker.Answer"/>, not allowed or denied.</summary>
    public IReadOnlyList<ClaudeQuestion>? Questions { get; init; }
    /// <summary>Set for <c>ExitPlanMode</c>: the plan, in Markdown, to approve or send back.</summary>
    public string? Plan { get; init; }
    public DateTime SinceUtc { get; init; }

    internal JsonNode? Input { get; init; }
    internal JsonNode? Suggestions { get; init; }
    internal TaskCompletionSource<string> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Turns Claude Code hook events into live session state, and holds permission requests until they are
/// answered in the notch. Answering in the terminal keeps working: once the session moves on (the tool ran,
/// the turn ended, a new prompt arrived) the held request is released without a decision.
/// </summary>
internal sealed class ClaudeSessionTracker
{
    internal const string NoDecision = "{}";
    private const string DenyMessage = "The user denied this from V-Notch.";
    private const string KeepPlanningMessage = "The user wants to keep planning. Ask what should change in the plan.";
    private const int PreviewChars = 160;
    private const int DetailChars = 300;

    /// <summary>Answer before Claude Code gives up on the hook (<see cref="ClaudeCodeSetup.PermissionTimeoutSeconds"/>).</summary>
    internal static readonly TimeSpan MaxHold = TimeSpan.FromSeconds(ClaudeCodeSetup.PermissionTimeoutSeconds - 10);
    private static readonly TimeSpan StaleWorking = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ForgetSession = TimeSpan.FromHours(3);
    private static readonly TimeSpan TitleRetry = TimeSpan.FromSeconds(15);

    private const string QuestionTool = "AskUserQuestion";
    // A plan is read in the notch and approved there, or sent back to keep planning.
    private const string PlanTool = "ExitPlanMode";

    private readonly object _gate = new();
    private readonly Dictionary<string, ClaudeSession> _sessions = new(StringComparer.Ordinal);
    private readonly List<ClaudePermissionRequest> _pending = new();
    private readonly Func<DateTime> _now;
    private readonly Func<string, string?> _readTitle;
    private int _requestCounter;

    public ClaudeSessionTracker(Func<DateTime>? utcNow = null, Func<string, string?>? readTitle = null)
    {
        _now = utcNow ?? (() => DateTime.UtcNow);
        _readTitle = readTitle ?? ClaudeTranscript.ReadTitle;
    }

    /// <summary>Raised on any thread when sessions or requests change.</summary>
    public event Action? Changed;

    /// <summary>Raised on any thread when a new request waits for an answer.</summary>
    public event Action<ClaudePermissionRequest>? PermissionRequested;

    /// <summary>Raised on any thread when a turn ends or a session starts: the plan usage moved.</summary>
    public event Action? UsageMayHaveChanged;

    /// <summary>Raised on any thread when a session finishes its turn, with a copy of that session.</summary>
    public event Action<ClaudeSession>? TurnFinished;

    public List<ClaudeSession> GetSessions()
    {
        lock (_gate)
        {
            var list = new List<ClaudeSession>(_sessions.Count);
            foreach (var s in _sessions.Values) list.Add(s.Clone());
            list.Sort(CompareSessions);
            return list;
        }
    }

    public List<ClaudePermissionRequest> GetPending()
    {
        lock (_gate) return new List<ClaudePermissionRequest>(_pending);
    }

    /// <summary>Handles one hook POST. The task completes with the HTTP body to send back.</summary>
    public Task<string> HandleHookAsync(JsonObject payload)
    {
        string sessionId = Str(payload, "session_id");
        if (sessionId.Length == 0) return Task.FromResult(NoDecision);

        string hookEvent = Str(payload, "hook_event_name");
        string tool = Str(payload, "tool_name");
        DateTime now = _now();
        ClaudePermissionRequest? held = null;
        ClaudeSession? finished = null;
        string? titleSource = null;
        var released = new List<ClaudePermissionRequest>();
        bool usageMoved = hookEvent is "SessionStart" or "Stop";

        lock (_gate)
        {
            if (hookEvent == "SessionEnd")
            {
                _sessions.Remove(sessionId);
                TakePending(sessionId, released, match: null);
            }
            else
            {
                var s = GetOrAdd(sessionId, Str(payload, "cwd"), hookEvent == "SessionStart", now);
                s.LastEventUtc = now;
                string transcript = Str(payload, "transcript_path");
                if (transcript.Length > 0) s.TranscriptPath = transcript;
                if (s.TranscriptPath != null && TitleMayHaveChanged(s, hookEvent, now))
                {
                    s.TitleReadUtc = now;
                    titleSource = s.TranscriptPath;
                }
                switch (hookEvent)
                {
                    case "SessionStart":
                        s.Status = ClaudeSessionStatus.Idle;
                        break;
                    case "UserPromptSubmit":
                        s.Status = ClaudeSessionStatus.Working;
                        s.TurnStartedUtc = now;
                        s.FinishedUtc = null;
                        s.Summary = null;
                        s.Activity = s.Tool = null;
                        string prompt = Preview(Str(payload, "prompt"));
                        s.Prompt = prompt.Length > 0 ? prompt : s.Prompt;
                        TakePending(sessionId, released, match: null);
                        break;
                    case "PreToolUse":
                        StartTurnIfNeeded(s, now);
                        s.Status = ClaudeSessionStatus.Working;
                        s.Activity = Describe(tool, payload["tool_input"]);
                        s.Tool = tool;
                        break;
                    case "PostToolUse":
                    case "PostToolUseFailure":
                        StartTurnIfNeeded(s, now);
                        s.Status = ClaudeSessionStatus.Working;
                        s.Activity = s.Tool = null;
                        TakePending(sessionId, released, match: (tool, payload["tool_input"]));
                        break;
                    case "PermissionRequest":
                        s.Status = ClaudeSessionStatus.Waiting;
                        s.Activity = Describe(tool, payload["tool_input"]);
                        s.Tool = tool;
                        var questions = tool == QuestionTool ? ParseQuestions(payload["tool_input"]) : null;
                        string plan = tool == PlanTool ? Str(payload["tool_input"] as JsonObject, "plan").Trim() : "";
                        // A question or plan nobody can read here (none parsed) stays in the terminal.
                        if ((tool != PlanTool || plan.Length > 0) && (tool != QuestionTool || questions != null))
                        {
                            held = new ClaudePermissionRequest
                            {
                                Id = $"{sessionId}:{++_requestCounter}",
                                SessionId = sessionId,
                                Tool = tool,
                                Detail = Detail(tool, payload["tool_input"]),
                                AlwaysLabel = AlwaysLabel(payload["permission_suggestions"]),
                                Questions = questions,
                                Plan = plan.Length > 0 ? plan : null,
                                SinceUtc = now,
                                Input = payload["tool_input"]?.DeepClone(),
                                Suggestions = payload["permission_suggestions"]?.DeepClone()
                            };
                            _pending.Add(held);
                        }
                        break;
                    case "Notification":
                        string kind = Str(payload, "notification_type");
                        string message = Str(payload, "message");
                        if (kind == "permission_prompt" || message.Contains("permission", StringComparison.OrdinalIgnoreCase))
                            s.Status = ClaudeSessionStatus.Waiting;
                        break;
                    case "Stop":
                    case "StopFailure":
                        s.Status = ClaudeSessionStatus.Done;
                        s.FinishedUtc = now;
                        s.Activity = s.Tool = null;
                        string reply = Str(payload, "last_assistant_message");
                        s.Summary = reply.Length > 0 ? Preview(reply) : null;
                        TakePending(sessionId, released, match: null);
                        finished = s.Clone();
                        break;
                }
            }
        }

        // Read outside the lock: the transcript can be large and other hooks keep coming.
        if (titleSource != null && _readTitle(titleSource) is { Length: > 0 } title)
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out var named)) named.Title = title;
            }
            if (finished != null) finished.Title = title;
        }

        foreach (var request in released) request.Answer.TrySetResult(NoDecision);
        Changed?.Invoke();
        if (held != null) PermissionRequested?.Invoke(held);
        if (finished != null) TurnFinished?.Invoke(finished);
        if (usageMoved) UsageMayHaveChanged?.Invoke();
        return held?.Answer.Task ?? Task.FromResult(NoDecision);
    }

    /// <summary>The notch's answer. False when the request was already answered elsewhere.</summary>
    public bool Decide(string requestId, ClaudeDecision decision)
    {
        var request = Take(requestId);
        if (request == null) return false;
        request.Answer.TrySetResult(DecisionBody(decision, request.Suggestions, request.Plan != null ? KeepPlanningMessage : DenyMessage));
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Answers an <c>AskUserQuestion</c> call: question text to the chosen label, several labels joined with
    /// ", ". False when the request was already answered elsewhere.
    /// </summary>
    public bool Answer(string requestId, IReadOnlyDictionary<string, string> answers)
    {
        var request = Take(requestId);
        if (request == null) return false;
        request.Answer.TrySetResult(AnswerBody(request.Input, answers));
        Changed?.Invoke();
        return true;
    }

    private ClaudePermissionRequest? Take(string requestId)
    {
        lock (_gate)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var request = _pending[i];
                if (request.Id != requestId) continue;
                _pending.RemoveAt(i);
                if (_sessions.TryGetValue(request.SessionId, out var s))
                {
                    s.Status = ClaudeSessionStatus.Working;
                    s.Activity = s.Tool = null;
                    s.LastEventUtc = _now();
                }
                return request;
            }
            return null;
        }
    }

    /// <summary>Lets go of requests Claude Code stopped waiting for and forgets sessions that went quiet.</summary>
    public void Sweep()
    {
        DateTime now = _now();
        var released = new List<ClaudePermissionRequest>();
        bool changed = false;
        lock (_gate)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (now - _pending[i].SinceUtc < MaxHold) continue;
                released.Add(_pending[i]);
                _pending.RemoveAt(i);
            }

            var forget = new List<string>();
            foreach (var s in _sessions.Values)
            {
                if (now - s.LastEventUtc > ForgetSession) forget.Add(s.Id);
                else if (s.Status == ClaudeSessionStatus.Working && now - s.LastEventUtc > StaleWorking)
                {
                    // Closed without SessionEnd (terminal killed): stop showing it as busy.
                    s.Status = ClaudeSessionStatus.Idle;
                    s.Activity = s.Tool = null;
                    changed = true;
                }
            }
            foreach (var id in forget) _sessions.Remove(id);
            changed |= forget.Count > 0 || released.Count > 0;
        }
        foreach (var request in released) request.Answer.TrySetResult(NoDecision);
        if (changed) Changed?.Invoke();
    }

    /// <summary>On shutdown: every held request goes back to Claude Code's own dialog.</summary>
    public void ReleaseAll()
    {
        List<ClaudePermissionRequest> released;
        lock (_gate)
        {
            released = new List<ClaudePermissionRequest>(_pending);
            _pending.Clear();
        }
        foreach (var request in released) request.Answer.TrySetResult(NoDecision);
    }

    internal static string DecisionBody(ClaudeDecision decision, JsonNode? suggestions, string denyMessage = DenyMessage)
    {
        var verdict = new JsonObject();
        if (decision == ClaudeDecision.Deny)
        {
            verdict["behavior"] = "deny";
            verdict["message"] = denyMessage;
        }
        else
        {
            verdict["behavior"] = "allow";
            // The same rules the dialog's "don't ask again" would save.
            if (decision == ClaudeDecision.Always && suggestions is JsonArray { Count: > 0 } rules)
                verdict["updatedPermissions"] = rules.DeepClone();
        }
        return PermissionBody(verdict);
    }

    /// <summary>Allows the question tool with the answers filled in, so it returns them without a dialog.</summary>
    internal static string AnswerBody(JsonNode? input, IReadOnlyDictionary<string, string> answers)
    {
        // updatedInput replaces the whole input: keep the questions next to the answers.
        var updated = input is JsonObject original ? (JsonObject)original.DeepClone() : new JsonObject();
        var map = new JsonObject();
        foreach (var pair in answers) map[pair.Key] = pair.Value;
        updated["answers"] = map;
        return PermissionBody(new JsonObject { ["behavior"] = "allow", ["updatedInput"] = updated });
    }

    private static string PermissionBody(JsonObject verdict)
    {
        var body = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = verdict
            }
        };
        return body.ToJsonString();
    }

    /// <summary>The questions of an <c>AskUserQuestion</c> input, or null when there is none with options.</summary>
    internal static List<ClaudeQuestion>? ParseQuestions(JsonNode? input)
    {
        if ((input as JsonObject)?["questions"] is not JsonArray array) return null;
        var questions = new List<ClaudeQuestion>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject q) continue;
            string text = Str(q, "question");
            if (text.Length == 0 || q["options"] is not JsonArray optionArray) continue;
            var options = new List<ClaudeQuestionOption>(optionArray.Count);
            foreach (var o in optionArray)
            {
                string label = Str(o as JsonObject, "label");
                if (label.Length == 0) continue;
                string description = Str(o as JsonObject, "description");
                options.Add(new ClaudeQuestionOption(label, description.Length > 0 ? description : null));
            }
            if (options.Count == 0) continue;
            bool multi = q["multiSelect"] is JsonValue m && m.TryGetValue(out bool b) && b;
            questions.Add(new ClaudeQuestion(text, Str(q, "header"), options, multi));
        }
        return questions.Count == 0 ? null : questions;
    }

    /// <summary>"Bash(npm test)", "Read(src/**)" or a mode from Claude Code's suggestions.</summary>
    internal static string? AlwaysLabel(JsonNode? suggestions)
    {
        if (suggestions is not JsonArray array) return null;
        var parts = new List<string>();
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue(out string? rule))
            {
                parts.Add(rule);
                continue;
            }
            if (item is not JsonObject update) continue;
            if (update["rules"] is JsonArray rules)
            {
                foreach (var r in rules)
                {
                    string toolName = Str(r as JsonObject, "toolName");
                    string content = Str(r as JsonObject, "ruleContent");
                    if (content.Length > 0) parts.Add($"{toolName}({content})");
                    else if (toolName.Length > 0) parts.Add(toolName);
                }
            }
            else if (Str(update, "mode") is { Length: > 0 } mode)
            {
                parts.Add(mode);
            }
            else if (update["directories"] is JsonArray directories)
            {
                foreach (var d in directories)
                    if (d is JsonValue dv && dv.TryGetValue(out string? dir)) parts.Add(dir);
            }
        }
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>The part of the tool input a person decides on.</summary>
    /// <summary>The plan's first line without its Markdown heading marks: what the plan is about.</summary>
    internal static string PlanTitle(string plan)
    {
        foreach (string raw in plan.Split('\n'))
        {
            string line = raw.Trim().TrimStart('#', ' ', '*').TrimEnd('*').Trim();
            if (line.Length > 0) return line;
        }
        return "";
    }

    internal static string? Detail(string tool, JsonNode? input)
    {
        var obj = input as JsonObject;
        string text = tool switch
        {
            "Bash" or "PowerShell" => Str(obj, "command"),
            "Edit" or "MultiEdit" or "Write" or "Read" or "NotebookEdit" => FirstOf(obj, "file_path", "notebook_path"),
            "WebFetch" => Str(obj, "url"),
            "WebSearch" => Str(obj, "query"),
            "Glob" or "Grep" => Str(obj, "pattern"),
            QuestionTool => ParseQuestions(obj) is { } questions ? questions[0].Text : "",
            PlanTool => PlanTitle(Str(obj, "plan")),
            _ => ""
        };
        if (text.Length == 0 && obj is { Count: > 0 }) text = obj.ToJsonString();
        text = text.Trim();
        if (text.Length == 0) return null;
        return text.Length > DetailChars ? string.Concat(text.AsSpan(0, DetailChars), "…") : text;
    }

    /// <summary>One line for the session row: "Bash: npm test", "Edit: MainWindow.cs".</summary>
    internal static string Describe(string tool, JsonNode? input)
    {
        if (tool.Length == 0) return "";
        string? detail = Detail(tool, input);
        if (detail == null) return tool;
        if (tool is "Edit" or "MultiEdit" or "Write" or "Read" or "NotebookEdit")
            detail = Path.GetFileName(detail.TrimEnd('\\', '/'));
        string line = Flatten(detail);
        if (line.Length > 80) line = string.Concat(line.AsSpan(0, 80), "…");
        return $"{tool}: {line}";
    }

    /// <summary>One line, no Markdown markers, at most ~160 characters.</summary>
    internal static string Preview(string text)
    {
        string flat = Flatten(text.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal));
        return flat.Length > PreviewChars ? string.Concat(flat.AsSpan(0, PreviewChars), "…") : flat;
    }

    private static string Flatten(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space) sb.Append(' ');
            space = false;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private ClaudeSession GetOrAdd(string id, string cwd, bool starting, DateTime now)
    {
        if (!_sessions.TryGetValue(id, out var s))
        {
            s = new ClaudeSession { Id = id, LastEventUtc = now, StartedUtc = now };
            _sessions[id] = s;
        }
        // The folder Claude Code was started in names the project; a "cd" during the session does not.
        if (cwd.Length > 0 && (starting || s.Cwd.Length == 0)) s.Cwd = cwd;
        return s;
    }

    private static void StartTurnIfNeeded(ClaudeSession s, DateTime now)
    {
        if (s.Status is not (ClaudeSessionStatus.Working or ClaudeSessionStatus.Waiting) || s.TurnStartedUtc == null)
        {
            s.TurnStartedUtc = now;
            s.FinishedUtc = null;
        }
    }

    /// <summary>
    /// Claude Code names a session after its first prompt and renames it on /rename: look again when a turn
    /// starts or ends, and every little while until it has a name.
    /// </summary>
    private static bool TitleMayHaveChanged(ClaudeSession s, string hookEvent, DateTime now) =>
        hookEvent is "SessionStart" or "UserPromptSubmit" or "Stop"
        || (s.Title == null && now - s.TitleReadUtc >= TitleRetry);

    /// <summary>Removes the session's held requests, or only the one for that tool call when <paramref name="match"/> is set.</summary>
    private void TakePending(string sessionId, List<ClaudePermissionRequest> released, (string Tool, JsonNode? Input)? match)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (p.SessionId != sessionId) continue;
            if (match is { } m && !IsSameCall(p, m.Tool, m.Input)) continue;
            released.Add(p);
            _pending.RemoveAt(i);
        }
    }

    private static bool IsSameCall(ClaudePermissionRequest request, string tool, JsonNode? input)
    {
        if (request.Tool != tool) return false;
        // Answering in the terminal adds the answers to the input, so a question matches on what it asked.
        if (request.Questions != null)
            return JsonNode.DeepEquals((request.Input as JsonObject)?["questions"], (input as JsonObject)?["questions"]);
        return JsonNode.DeepEquals(request.Input, input);
    }

    private static int CompareSessions(ClaudeSession a, ClaudeSession b)
    {
        int rank = Rank(a.Status).CompareTo(Rank(b.Status));
        // Within a state, newest first by when it appeared, so busy cards keep their place.
        return rank != 0 ? rank : b.StartedUtc.CompareTo(a.StartedUtc);

        static int Rank(ClaudeSessionStatus status) => status switch
        {
            ClaudeSessionStatus.Waiting => 0,
            ClaudeSessionStatus.Working => 1,
            ClaudeSessionStatus.Done => 2,
            _ => 3
        };
    }

    private static string FirstOf(JsonObject? obj, string first, string second)
    {
        string value = Str(obj, first);
        return value.Length > 0 ? value : Str(obj, second);
    }

    private static string Str(JsonObject? obj, string key)
    {
        if (obj == null || !obj.TryGetPropertyValue(key, out var node) || node is not JsonValue value) return "";
        return value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? s) ? s : "";
    }
}
