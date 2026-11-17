using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VNotch.Controls;
using VNotch.Services;
using VNotch.Services.ControlCenter;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class SystemTabTests
{
    [Fact]
    public void LoadWarningRequiresSustainedLoadAndClearsWithHysteresis()
    {
        var state = new ControlCenterView.LoadWarningState();
        var now = DateTime.UtcNow;
        Assert.Equal("", state.Update(now, 20, 97, 60, 70));
        Assert.Equal("", state.Update(now.AddSeconds(9), 20, 97, 60, 70));
        Assert.StartsWith("GPU 97", state.Update(now.AddSeconds(10), 20, 97, 60, 70));
        Assert.NotEmpty(state.Update(now.AddSeconds(11), 20, 79, 60, 79));
        Assert.NotEmpty(state.Update(now.AddSeconds(20), 20, 79, 60, 79));
        Assert.Equal("", state.Update(now.AddSeconds(21), 20, 79, 60, 79));
        Assert.StartsWith("CPU", state.Update(now.AddSeconds(22), 20, 40, 86, 60));
        state.Pause();
        Assert.NotEmpty(state.Update(now.AddHours(1), 20, 20, 60, 60));
        Assert.Equal("", state.Update(now.AddHours(1).AddSeconds(10), 20, 20, 60, 60));
    }

    [Fact]
    public void AHiddenIntervalDoesNotQualifyAsSustainedLoad()
    {
        var state = new ControlCenterView.LoadWarningState(); var now = DateTime.UtcNow;
        Assert.Equal("", state.Update(now, 95, 20, 60, 60));
        state.Pause();
        Assert.Equal("", state.Update(now.AddHours(1), 95, 20, 60, 60));
    }

    [Fact]
    public void WaveMuteSelectsTheHardwareInputAndRejectsUnknownOrMalformedDevices()
    {
        var result = JsonNode.Parse("""
        {"inputDevices":[
          {"id":"other","name":"Default microphone","inputs":[{"id":"wrong","isMuted":false}]},
          {"id":"xlr","name":"Elgato Wave XLR MK.2","inputs":[{"id":"mic","isMuted":true}]}]}
        """);
        Assert.Equal(new WaveXlrState("xlr", "mic", true), WaveXlrControl.Parse(result));
        Assert.Null(WaveXlrControl.Parse(JsonNode.Parse("[]")));
        Assert.Null(WaveXlrControl.Parse(JsonNode.Parse("{\"inputDevices\":[{\"name\":\"Wave XLR\",\"id\":\"bad\",\"inputs\":[{}]}]}")));
    }

    [Theory]
    [InlineData("Wave Link Stream (Elgato Virtual Audio)", "Stream", "Wave")]
    [InlineData("Headphones (AirPods Pro)", "AirPods Pro", "AirPods")]
    [InlineData("Speakers", "Speakers", "Speakers")]
    public void OutputChipKeepsTheNameShort(string friendly, string name, string expected) =>
        Assert.Equal(expected, ControlCenterView.ShortOutputName(new("id", name, friendly, true, false)));

    [Theory]
    [InlineData("Kopfhörer (MOONDROP Rays)", 0xE7F6)]
    [InlineData("Headphones (AirPods Pro)", 0xE7F6)]
    [InlineData("Wave Link Stream", 0xE7F5)]
    public void OutputIconShowsTheDeviceTypeEvenWithoutABluetoothMatch(string friendly, int glyph) =>
        Assert.Equal(glyph, ControlCenterView.OutputGlyph(new("id", "", friendly, true, false)));

    [Fact]
    public void CompactSystemHasOneFluentIconFamilyAndInterruptibleSidewaysOutputLabels() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool previous = AnimationConfig.ReduceMotion; bool? client = AnimationConfig.ClientAreaAnimationOverride;
        BackgroundTestWindows.EnsureApplicationResources();
        AnimationConfig.ClientAreaAnimationOverride = true; AnimationConfig.SetReduceMotion(false);
        using var view = CreateView();
        var scene = new Border { Width = 568, Background = Brushes.Black, Child = view };
        var window = new Window { Content = scene, Width = 600, Height = 420, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); view.UpdateLayout();
            var chip = (Border)HomeSchoolTabTests.Field(view, "_systemOutputChip");
            Assert.InRange(chip.ActualWidth, 50, 90);
            Assert.Equal("Wave Link Stream", chip.ToolTip);
            Assert.InRange(view.MeasureContentHeight(568), 45, 60);
            var icons = HomeSchoolTabTests.Descendants<TextBlock>(view).Where(t => t.FontFamily.Source.Contains("Fluent", StringComparison.Ordinal)).ToList();
            Assert.True(icons.Count >= 7);
            Assert.All(icons, i => Assert.Equal(16, i.FontSize));
            var sound = (StackPanel)HomeSchoolTabTests.Field(view, "_systemSound");
            var outputs = HomeSchoolTabTests.Descendants<Border>(sound).Where(b => b.ToolTip is string title &&
                (title == "Wave Link Stream" || title == "Headphones (AirPods Pro)")).ToArray();
            Assert.Equal(2, outputs.Length);
            Assert.All(outputs, output => Assert.Equal(System.Windows.Input.Cursors.Hand, output.Cursor));
            var holder = (Grid)HomeSchoolTabTests.Field(view, "_systemOutputName");
            HomeSchoolTabTests.SetField(view, "_outputs", new List<OutputDevice> { new("air", "AirPods", "Headphones (AirPods Pro)", true, true) });
            HomeSchoolTabTests.Invoke(view, "ShowCompactOutput");
            Assert.Equal(2, holder.Children.Count);
            var incoming = Assert.IsType<TextBlock>(holder.Children[^1]);
            Assert.True(((TranslateTransform)incoming.RenderTransform).X > 0);
            Assert.Equal("Headphones (AirPods Pro)", chip.ToolTip);
            await WpfFrameWaiter.UntilAsync(() => holder.Children.Count == 1, "output label leaves sideways", ct);
            view.Visibility = Visibility.Collapsed;
            Assert.False(((TranslateTransform)incoming.RenderTransform).HasAnimatedProperties);
        }
        finally
        {
            HomeSchoolTabTests.SetField(view, "_gpuSampling", false);
            window.Close(); AnimationConfig.ClientAreaAnimationOverride = client; AnimationConfig.SetReduceMotion(previous);
        }
    });

    internal static ControlCenterView CreateView()
    {
        var view = new ControlCenterView { Page = ControlCenterPage.System };
        HomeSchoolTabTests.SetField(view, "_refreshing", true);
        HomeSchoolTabTests.SetField(view, "_gpuSampling", true);
        HomeSchoolTabTests.SetField(view, "_readingCompactDisplays", true);
        view.AttachLegion();
        HomeSchoolTabTests.SetField(view, "_legionBusy", true);
        HomeSchoolTabTests.SetField(view, "_legion", new LegionState((int)LegionMode.Balanced, false, false, 61, 44));
        var type = typeof(ControlCenterView).GetNestedType("Snapshot", BindingFlags.NonPublic)!;
        var snapshot = Activator.CreateInstance(type)!;
        void Set(string name, object value) => type.GetField(name)!.SetValue(snapshot, value);
        Set("WifiOn", true); Set("BluetoothOn", true); Set("WaveXlr", new WaveXlrState("device", "mic", false));
        Set("Stats", new SystemStats(34, 56, 18, 32, 12, 9, 1024, 128));
        Set("Outputs", new List<OutputDevice> { new("wave", "Wave", "Wave Link Stream", true, false), new("air", "AirPods", "Headphones (AirPods Pro)", false, true) });
        Set("Apps", new List<AudioSessionInfo> { new() { ProcessId = 1, DisplayName = "Spotify", Volume = .8f }, new() { ProcessId = 2, DisplayName = "Discord", Volume = .45f } });
        Set("Devices", new List<BluetoothDevice> { new() { Name = "AirPods Pro", Address = "air", Battery = 80, Connected = true } });
        HomeSchoolTabTests.SetField(view, "_snapshot", snapshot);
        HomeSchoolTabTests.Invoke(view, "BuildCompactSystem", snapshot);
        HomeSchoolTabTests.Invoke(view, "ShowCompactVolume", .64f, false);
        return view;
    }
}
