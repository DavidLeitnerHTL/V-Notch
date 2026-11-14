using System;
using System.Threading.Tasks;
using System.Windows.Media;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests;

public sealed class MotionTests
{
    [Fact]
    public void ASpringSentElsewhereMidwayHandsItsSpeedOver() => SharedStaTestRunner.RunAsync(async () =>
    {
        var scale = new ScaleTransform(1, 1);
        Motion.To(scale, ScaleTransform.ScaleXProperty, 2, Motion.Glide);
        await Task.Delay(90);

        double speed = Motion.VelocityOf(scale, ScaleTransform.ScaleXProperty);
        Assert.True(speed > 0.5, $"still moving toward 2, was {speed}");

        Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Glide);
        // Sent back, it keeps travelling the old way for a moment instead of starting from rest.
        Assert.True(Motion.VelocityOf(scale, ScaleTransform.ScaleXProperty) > 0.5);
    });

    [Fact]
    public void AnEntranceWaitsAtItsStart() => SharedStaTestRunner.Run(() =>
    {
        var scale = new ScaleTransform(1, 1);
        Motion.To(scale, ScaleTransform.ScaleXProperty, 1, Motion.Tile, from: Motion.TileFromScale, delay: Motion.Stagger(3));
        Assert.Equal(Motion.TileFromScale, scale.ScaleX, 3);
    });

    [Fact]
    public void ReducedMotionGoesStraightThere() => SharedStaTestRunner.Run(() =>
    {
        bool before = AnimationConfig.ReduceMotion;
        AnimationConfig.SetReduceMotion(true);
        try
        {
            var scale = new ScaleTransform(1, 1);
            bool done = false;
            Motion.To(scale, ScaleTransform.ScaleXProperty, 3, Motion.Pop, done: () => done = true);
            Assert.Equal(3, scale.ScaleX);
            Assert.True(done);
        }
        finally
        {
            AnimationConfig.SetReduceMotion(before);
        }
    });

    [Fact]
    public void LeavingIsShorterThanArrivingAndNeverOvershoots()
    {
        var arrive = Motion.Pop.Animate(0, 100);
        var leave = Motion.Exit.Animate(100, 0);
        Assert.True(leave.Duration.TimeSpan < arrive.Duration.TimeSpan);
        for (double t = 0; t < 1; t += 0.004) Assert.True(leave.ValueAt(t) >= -0.01);
        Assert.Equal(TimeSpan.FromMilliseconds(Motion.StaggerCapMs), Motion.Stagger(500));
    }
}
