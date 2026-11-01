using System.IO;
using System.Text;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

public class ClaudeTranscriptTests
{
    [Fact]
    public void Title_PrefersTheNameGivenWithRename()
    {
        string tail = string.Join('\n',
            "{\"type\":\"ai-title\",\"aiTitle\":\"Old idea\",\"sessionId\":\"s\"}",
            "{\"type\":\"custom-title\",\"customTitle\":\"My name\",\"sessionId\":\"s\"}",
            "{\"type\":\"ai-title\",\"aiTitle\":\"Newer idea\",\"sessionId\":\"s\"}",
            "");

        Assert.Equal("My name", ClaudeTranscript.FindTitle(tail));
    }

    [Fact]
    public void Title_IsTheLatestEntry_NotAQuotedOneOrAHalfWrittenOne()
    {
        string tail = string.Join('\n',
            "{\"type\":\"ai-title\",\"aiTitle\":\"First\"}",
            "{\"type\":\"ai-title\",\"aiTitle\":\"Second\"}",
            "{\"type\":\"user\",\"toolUseResult\":{\"type\":\"ai-title\",\"aiTitle\":\"Fake\"}}",
            "{\"type\":\"ai-title\",\"aiTi");

        Assert.Equal("Second", ClaudeTranscript.FindTitle(tail));
    }

    [Fact]
    public void Title_IsNullUntilClaudeCodeNamedTheSession()
    {
        Assert.Null(ClaudeTranscript.FindTitle("{\"type\":\"user\",\"message\":{}}\n"));
    }

    [Fact]
    public void ReadTitle_FindsTheTitleBehindALongTranscript()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vnotch-transcript-{System.Guid.NewGuid():N}.jsonl");
        try
        {
            var text = new StringBuilder();
            text.Append("{\"type\":\"custom-title\",\"customTitle\":\"Too far back\"}\n");
            string filler = "{\"type\":\"assistant\",\"message\":\"" + new string('x', 1000) + "\"}\n";
            for (int i = 0; i < 400; i++) text.Append(filler);
            text.Append("{\"type\":\"ai-title\",\"aiTitle\":\"Tab-Leiste umbauen\"}\n");
            File.WriteAllText(path, text.ToString());

            Assert.Equal("Tab-Leiste umbauen", ClaudeTranscript.ReadTitle(path));
            Assert.Null(ClaudeTranscript.ReadTitle(Path.ChangeExtension(path, ".txt")));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
