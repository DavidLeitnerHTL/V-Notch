using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using Xunit;

namespace VNotch.Tests;

/// <summary>The struck-out mic: when a call is on, when it shows, and what Discord tells.</summary>
public class MicMutedTests
{
    [Theory]
    // Hold the mic all day: the audio mixer, NVIDIA's instant replay, Medal, the audio engine.
    [InlineData("Elgato.WaveLink", false)]
    [InlineData("WavelinkSEService", false)]
    [InlineData("nvcontainer", false)]
    [InlineData("NVIDIA Broadcast", false)]
    [InlineData("Medal", false)]
    [InlineData("audiodg", false)]
    [InlineData("", false)]
    // A call, or voice chat in a game.
    [InlineData("Discord", true)]
    [InlineData("VALORANT-Win64-Shipping", true)]
    [InlineData("chrome", true)]
    [InlineData("obs64", true)]
    public void OnlyRealCallsCount(string process, bool isCall)
    {
        Assert.Equal(isCall, MainWindow.IsCallRecorder(process));
    }

    [Theory]
    [InlineData(true, true, false, true)]    // muted at the XLR (sets the Windows flag) during a call
    [InlineData(false, true, false, false)]  // muted, but no call: nothing to warn about
    [InlineData(true, false, false, false)]  // call, mic open
    [InlineData(false, false, true, true)]   // Discord: muted or deafened in a voice channel
    [InlineData(true, false, true, true)]
    public void ShowsOnlyWhenTalkingIntoAMutedMicIsPossible(bool inCall, bool windowsMuted, bool discordMuted, bool expected)
    {
        Assert.Equal(expected, MainWindow.ShouldShowMicMuted(inCall, windowsMuted, discordMuted));
    }

    [Fact]
    public void DiscordCountsMuteAndDeafen()
    {
        Assert.True(DiscordVoice.MutedFrom(JsonNode.Parse("""{"mute":true,"deaf":false}""")));
        Assert.True(DiscordVoice.MutedFrom(JsonNode.Parse("""{"mute":false,"deaf":true}""")));
        Assert.False(DiscordVoice.MutedFrom(JsonNode.Parse("""{"mute":false,"deaf":false,"input":{"volume":100}}""")));
        Assert.False(DiscordVoice.MutedFrom(null));
    }

    [Fact]
    public void DiscordFramesAreOpcodeLengthThenJson()
    {
        byte[] frame = DiscordVoice.Frame(1, new JsonObject { ["cmd"] = "SUBSCRIBE", ["evt"] = "VOICE_SETTINGS_UPDATE" });
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(frame));
        int length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4));
        Assert.Equal(frame.Length - 8, length);
        var body = JsonNode.Parse(Encoding.UTF8.GetString(frame, 8, length))!;
        Assert.Equal("VOICE_SETTINGS_UPDATE", (string?)body["evt"]);
    }
}
