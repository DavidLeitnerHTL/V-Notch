using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;

namespace VNotch.Services;

/// <summary>
/// The notch's one motion language: a handful of named springs and a way to send any number there.
/// Every spring started through <see cref="To"/> remembers where it is going, so sending the same property
/// somewhere else while it still moves carries position and speed over instead of starting from rest.
/// </summary>
internal static partial class Motion
{
    // Shell: the notch itself opening and closing. Width leads, height swings a little longer.
    public static readonly SpringMotion ShellOpenWidth = Ms(420, 0.32);
    public static readonly SpringMotion ShellOpenHeight = Ms(460, 0.26);
    public static readonly SpringMotion ShellCloseWidth = Ms(300, 0.12);
    public static readonly SpringMotion ShellCloseHeight = Ms(280, 0.05);

    // Pop: whatever grows out of the closed notch (pills, HUDs, the orb, the phone) and goes back in.
    public static readonly SpringMotion Pop = Ms(380, 0.38);
    public static readonly SpringMotion PopBack = Ms(300, 0.10);

    // Glide: size and layout changes (tab switch, folding sections, sliders settling).
    public static readonly SpringMotion Glide = Ms(380, 0.15);

    // Tile: content arriving, staggered by StaggerMs from TileFromScale.
    public static readonly SpringMotion Tile = Ms(400, 0.38);
    public const double TileFromScale = 0.72;
    public const int StaggerMs = 30;
    public const int StaggerCapMs = 240;

    // Press: hover, press release, switches, small glyph hops.
    public static readonly SpringMotion Press = Ms(300, 0.45);
    // How fast something gives way under the pointer before Press brings it back.
    public static readonly SpringMotion PressDown = Ms(140, 0);

    // Exit: leaving is shorter than arriving and never overshoots.
    public static readonly SpringMotion Exit = Ms(220, 0);
    public const double ExitToScale = 0.86;

    private static SpringMotion Ms(double response, double bounce) => new(TimeSpan.FromMilliseconds(response), bounce);

    private sealed class Running
    {
        public required SpringDoubleAnimation Spring;
        public long StartedAt;
        public double DelaySeconds;
    }

    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Running>> Live = new();

    /// <summary>The delay of the <paramref name="index"/>th item of a staggered group.</summary>
    public static TimeSpan Stagger(int index, int stepMs = StaggerMs) =>
        TimeSpan.FromMilliseconds(Math.Min(Math.Max(index, 0) * stepMs, StaggerCapMs));

    /// <summary>
    /// Springs <paramref name="property"/> of <paramref name="target"/> to <paramref name="to"/>. A spring already
    /// running on it hands over its speed. <paramref name="from"/> places the value first (an entrance);
    /// leave it out to continue from wherever the value is. <paramref name="velocity"/> (units per second) starts
    /// it with a speed of its own, e.g. the pointer's when a drag lets go.
    /// </summary>
    public static void To(DependencyObject target, DependencyProperty property, double to, SpringMotion spring,
        double? from = null, TimeSpan? delay = null, int? fps = null, Action? done = null, double? velocity = null)
    {
        if (target is not IAnimatable animatable) throw new ArgumentException("Target cannot be animated.", nameof(target));
        var live = Live.GetOrCreateValue(target);

        if (AnimationConfig.ReduceMotion)
        {
            live.Remove(property);
            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
            done?.Invoke();
            return;
        }

        double speed = velocity ?? 0;
        if (velocity is null && from is null && live.TryGetValue(property, out var old))
            speed = old.Spring.VelocityAt(Stopwatch.GetElapsedTime(old.StartedAt).TotalSeconds - old.DelaySeconds);
        double start = from ?? (double)target.GetValue(property);
        if (!double.IsFinite(start)) start = to;

        // Arrived means within half a percent of the way, so a scale going 0.72 -> 1 settles as finely as a
        // width going 150 -> 720.
        double settle = Math.Clamp(Math.Abs(to - start) * 0.005, 0.0005, SpringDoubleAnimation.PixelSettle);
        var animation = spring.Animate(start, to, speed, settle);
        if (animation.Duration.TimeSpan <= TimeSpan.Zero)
        {
            live.Remove(property);
            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
            done?.Invoke();
            return;
        }
        if (delay is { } wait && wait > TimeSpan.Zero) animation.BeginTime = wait;
        Timeline.SetDesiredFrameRate(animation, fps ?? AnimationConfig.TargetFps);

        var running = new Running { Spring = animation, StartedAt = Stopwatch.GetTimestamp(), DelaySeconds = delay?.TotalSeconds ?? 0 };
        live[property] = running;
        animation.Completed += (_, _) =>
        {
            if (live.TryGetValue(property, out var current) && ReferenceEquals(current, running)) live.Remove(property);
            else return; // replaced on the way: the newer spring owns the ending
            done?.Invoke();
        };
        // An entrance that waits must already sit at its start, or it shows its resting look until its turn.
        if (from is { } placed && delay is { } d && d > TimeSpan.Zero) target.SetValue(property, placed);
        animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>Speed of the spring running on the property right now, in units per second; zero when at rest.</summary>
    public static double VelocityOf(DependencyObject target, DependencyProperty property) =>
        Live.TryGetValue(target, out var live) && live.TryGetValue(property, out var running)
            ? running.Spring.VelocityAt(Stopwatch.GetElapsedTime(running.StartedAt).TotalSeconds - running.DelaySeconds)
            : 0;

    /// <summary>Stops the spring and leaves the property at <paramref name="value"/> (its current value if omitted).</summary>
    public static void Stop(DependencyObject target, DependencyProperty property, double? value = null)
    {
        if (target is not IAnimatable animatable) return;
        double rest = value ?? (double)target.GetValue(property);
        if (Live.TryGetValue(target, out var live)) live.Remove(property);
        animatable.BeginAnimation(property, null);
        target.SetValue(property, rest);
    }
}
