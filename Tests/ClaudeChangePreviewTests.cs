using System.Text.Json.Nodes;
using VNotch.Services.ClaudeCode;
using Xunit;

namespace VNotch.Tests;

public class ClaudeChangePreviewTests
{
    [Fact]
    public void Edit_ShowsOnlyTheLinesThatChange_WithoutTheirSharedIndent()
    {
        var input = new JsonObject
        {
            ["file_path"] = @"C:\work\App.cs",
            ["old_string"] = "    void Run()\n    {\n        Start(1);\n    }",
            ["new_string"] = "    void Run()\n    {\n        Start(2);\n        Log();\n    }"
        };

        var lines = ClaudeChangePreview.Lines("Edit", input);

        Assert.Equal(new[]
        {
            new ClaudeChangeLine('-', "Start(1);"),
            new ClaudeChangeLine('+', "Start(2);"),
            new ClaudeChangeLine('+', "Log();")
        }, lines);
    }

    [Fact]
    public void Write_IsAllAdded_AndOtherToolsHaveNoPreview()
    {
        var write = new JsonObject { ["file_path"] = "a.txt", ["content"] = "one\r\ntwo" };

        Assert.Equal(new[] { new ClaudeChangeLine('+', "one"), new ClaudeChangeLine('+', "two") },
            ClaudeChangePreview.Lines("Write", write));
        Assert.Empty(ClaudeChangePreview.Lines("Bash", new JsonObject { ["command"] = "ls" }));
    }

    [Fact]
    public void MultiEdit_ListsEveryEdit()
    {
        var input = new JsonObject
        {
            ["edits"] = new JsonArray(
                new JsonObject { ["old_string"] = "a", ["new_string"] = "b" },
                new JsonObject { ["old_string"] = "c", ["new_string"] = "" })
        };

        Assert.Equal(new[]
        {
            new ClaudeChangeLine('-', "a"),
            new ClaudeChangeLine('+', "b"),
            new ClaudeChangeLine('-', "c")
        }, ClaudeChangePreview.Lines("MultiEdit", input));
    }
}
