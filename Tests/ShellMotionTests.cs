using System.Windows;
using System.Windows.Controls;
using VNotch.Controllers;
using VNotch.Models;
using VNotch.Presenters;
using VNotch.Services;
using Xunit;

namespace VNotch.Tests;

[Collection("Localization")]
public sealed class ShellMotionTests
{
    [Fact]
    public void ReversingTheShellCarriesBothDimensionsAndTheCornerFromTheRenderedFrame() => SharedStaTestRunner.RunAsync(async ct =>
    {
        bool previous = AnimationConfig.ReduceMotion;
        try
        {
            AnimationConfig.SetReduceMotion(false);
            var border = new Border { Width = 68, Height = 28, CornerRadius = new CornerRadius(14) };
            using var presenter = new NotchShellPresenter(new() { NotchBorder = border });
            var motion = new TransitionMotionConfig(TimeSpan.FromMilliseconds(400), AnimationPrimitives._easeUiOut, 60, false);
            var opening = new TransitionPlan(1, NotchView.Compact, NotchView.Media, NotchShapeState.Expanding,
                528, 300, 36, motion);
            int staleCompletions = 0;
            presenter.AnimateShell(opening, _ => staleCompletions++);
            await WpfFrameWaiter.UntilAsync(() => border.Width > 120, "shell starts expanding", ct);
            double width = border.Width, height = border.Height, radius = border.CornerRadius.BottomLeft;
            double widthSpeed = Motion.VelocityOf(border, FrameworkElement.WidthProperty);
            double heightSpeed = Motion.VelocityOf(border, FrameworkElement.HeightProperty);
            Assert.True(widthSpeed > 100);
            Assert.True(heightSpeed > 100);
            presenter.CancelCurrentAnimation();
            bool completed = false;
            presenter.AnimateShell(opening with
            {
                SessionId = 2, FromView = NotchView.Media, TargetView = NotchView.Compact,
                TargetWidth = 68, TargetHeight = 28, TargetCornerRadius = 14
            }, _ => completed = true);
            Assert.Equal(width, border.Width, 6);
            Assert.Equal(height, border.Height, 6);
            Assert.Equal(radius, border.CornerRadius.BottomLeft, 6);
            Assert.InRange(Motion.VelocityOf(border, FrameworkElement.WidthProperty), widthSpeed * .9, widthSpeed * 1.1);
            Assert.InRange(Motion.VelocityOf(border, FrameworkElement.HeightProperty), heightSpeed * .9, heightSpeed * 1.1);
            await WpfFrameWaiter.UntilAsync(() => completed, "reversed shell settles", ct);
            Assert.Equal(68, border.Width);
            Assert.Equal(28, border.Height);
            Assert.Equal(0, staleCompletions);
            await WpfFrameWaiter.UntilAsync(() => Math.Abs(border.CornerRadius.BottomLeft - 14) < .001,
                "corner settles with shell", ct);
        }
        finally { AnimationConfig.SetReduceMotion(previous); }
    });

    [Fact]
    public void ReducedMotionSetsTheWholeShellImmediately() => SharedStaTestRunner.Run(() =>
    {
        var border = new Border { Width = 68, Height = 28, CornerRadius = new CornerRadius(14) };
        using var presenter = new NotchShellPresenter(new() { NotchBorder = border });
        bool completed = false;
        presenter.AnimateShell(new TransitionPlan(1, NotchView.Compact, NotchView.Media, NotchShapeState.Expanding,
            528, 300, 36, new(TimeSpan.FromMilliseconds(400), AnimationPrimitives._easeUiOut, 60, true)), _ => completed = true);
        Assert.True(completed);
        Assert.Equal(528, border.Width);
        Assert.Equal(300, border.Height);
        Assert.Equal(new CornerRadius(36), border.CornerRadius);
        Assert.False(border.HasAnimatedProperties);
    });
}
