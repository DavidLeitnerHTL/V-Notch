using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static VNotch.Services.AnimationPrimitives;
using VNotch.Services;

namespace VNotch.Controls;

internal static class ButtonFeedback
{
    private static readonly DependencyProperty TransitionVersionProperty = DependencyProperty.RegisterAttached(
        "TransitionVersion", typeof(int), typeof(ButtonFeedback), new PropertyMetadata(0));

    private static int NextTransitionVersion(DependencyObject target)
    {
        int next = ((int)target.GetValue(TransitionVersionProperty)) + 1;
        target.SetValue(TransitionVersionProperty, next);
        return next;
    }

    private static bool IsTransitionVersionCurrent(DependencyObject target, int version)
        => (int)target.GetValue(TransitionVersionProperty) == version;

    internal static void AnimateIconSwitch(Canvas fromIcon, Canvas toIcon, TimeSpan duration, EasingFunctionBase easing)
    {
        int fromVersion = NextTransitionVersion(fromIcon);
        int toVersion = NextTransitionVersion(toIcon);

        var fromTransform = fromIcon.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        var toTransform = toIcon.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);

        fromIcon.RenderTransform = fromTransform;
        toIcon.RenderTransform = toTransform;
        bool entering = toIcon.Visibility != Visibility.Visible;
        toIcon.Visibility = Visibility.Visible;
        if (ReferenceEquals(fromIcon, toIcon)) return;
        Motion.To(fromTransform, ScaleTransform.ScaleXProperty, Motion.ExitToScale, Motion.Exit);
        Motion.To(fromTransform, ScaleTransform.ScaleYProperty, Motion.ExitToScale, Motion.Exit);
        Motion.To(fromIcon, UIElement.OpacityProperty, 0, Motion.Exit, done: () =>
        {
            if (!IsTransitionVersionCurrent(fromIcon, fromVersion)) return;
            fromIcon.Visibility = Visibility.Collapsed;
            Motion.Stop(fromTransform, ScaleTransform.ScaleXProperty, 1);
            Motion.Stop(fromTransform, ScaleTransform.ScaleYProperty, 1);
            Motion.Stop(fromIcon, UIElement.OpacityProperty, 1);
        });
        Motion.To(toTransform, ScaleTransform.ScaleXProperty, 1, Motion.Press, from: entering ? Motion.TileFromScale : null);
        Motion.To(toTransform, ScaleTransform.ScaleYProperty, 1, Motion.Press, from: entering ? Motion.TileFromScale : null);
        Motion.To(toIcon, UIElement.OpacityProperty, 1, Motion.Press, from: entering ? 0 : null, done: () =>
        {
            if (!IsTransitionVersionCurrent(toIcon, toVersion)) return;
            Motion.Stop(toIcon, UIElement.OpacityProperty, 1);
        });
    }

    internal static void PlayButtonPressAnimation(Border button, double pressedScale = 0.82)
    {
        var transform = button.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
        button.RenderTransform = transform;
        button.RenderTransformOrigin = new Point(0.5, 0.5);
        int version = NextTransitionVersion(button);
        Motion.To(transform, ScaleTransform.ScaleYProperty, pressedScale, Motion.PressDown);
        Motion.To(transform, ScaleTransform.ScaleXProperty, pressedScale, Motion.PressDown, done: () =>
        {
            if (!IsTransitionVersionCurrent(button, version)) return;
            Motion.To(transform, ScaleTransform.ScaleXProperty, 1, Motion.Press);
            Motion.To(transform, ScaleTransform.ScaleYProperty, 1, Motion.Press);
        });
    }

    internal static void PlayNextSkipAnimation(System.Windows.Shapes.Path arrow0, System.Windows.Shapes.Path arrow1)
    {
        const double slideDistance = 220;
        int version = NextTransitionVersion(arrow1);

        var arrow1Transform = arrow1.RenderTransform as TranslateTransform ?? new TranslateTransform();
        arrow1.RenderTransform = arrow1Transform;

        var slideOut1 = new DoubleAnimation(0, slideDistance, _dur250) { EasingFunction = _easeQuadOut };
        var fadeOut1 = new DoubleAnimation(1, 0, _dur250) { EasingFunction = _easeQuadOut };

        var arrow0Transform = arrow0.RenderTransform as TranslateTransform ?? new TranslateTransform();
        arrow0.RenderTransform = arrow0Transform;

        var slideIn0 = new DoubleAnimation(-slideDistance, 0, _dur250) { EasingFunction = _easeQuadOut };
        var fadeIn0 = new DoubleAnimation(0, 1, _dur250) { EasingFunction = _easeQuadOut };

        fadeOut1.Completed += (s, e) =>
        {
            if (!IsTransitionVersionCurrent(arrow1, version)) return;

            arrow1Transform.X = 0;
            arrow1.Opacity = 1;
            arrow0Transform.X = 0;
            arrow0.Opacity = 0;

            arrow1Transform.BeginAnimation(TranslateTransform.XProperty, null);
            arrow1.BeginAnimation(UIElement.OpacityProperty, null);
            arrow0Transform.BeginAnimation(TranslateTransform.XProperty, null);
            arrow0.BeginAnimation(UIElement.OpacityProperty, null);
        };

        Timeline.SetDesiredFrameRate(slideOut1, VNotch.Services.AnimationConfig.TargetFps);
        arrow1Transform.BeginAnimation(TranslateTransform.XProperty, slideOut1);
        Timeline.SetDesiredFrameRate(fadeOut1, VNotch.Services.AnimationConfig.TargetFps);
        arrow1.BeginAnimation(UIElement.OpacityProperty, fadeOut1);
        Timeline.SetDesiredFrameRate(slideIn0, VNotch.Services.AnimationConfig.TargetFps);
        arrow0Transform.BeginAnimation(TranslateTransform.XProperty, slideIn0);
        Timeline.SetDesiredFrameRate(fadeIn0, VNotch.Services.AnimationConfig.TargetFps);
        arrow0.BeginAnimation(UIElement.OpacityProperty, fadeIn0);
    }

    internal static void PlayPrevSkipAnimation(System.Windows.Shapes.Path arrow0, System.Windows.Shapes.Path arrow2)
    {
        const double slideDistance = 220;
        int version = NextTransitionVersion(arrow2);

        var arrow2Transform = arrow2.RenderTransform as TranslateTransform ?? new TranslateTransform();
        arrow2.RenderTransform = arrow2Transform;

        var slideOut2 = new DoubleAnimation(0, -slideDistance, _dur250) { EasingFunction = _easeQuadOut };
        var fadeOut2 = new DoubleAnimation(1, 0, _dur250) { EasingFunction = _easeQuadOut };

        var arrow0Transform = arrow0.RenderTransform as TranslateTransform ?? new TranslateTransform();
        arrow0.RenderTransform = arrow0Transform;

        var slideIn0 = new DoubleAnimation(slideDistance, 0, _dur250) { EasingFunction = _easeQuadOut };
        var fadeIn0 = new DoubleAnimation(0, 1, _dur250) { EasingFunction = _easeQuadOut };

        fadeOut2.Completed += (s, e) =>
        {
            if (!IsTransitionVersionCurrent(arrow2, version)) return;

            arrow2Transform.X = 0;
            arrow2.Opacity = 1;
            arrow0Transform.X = 0;
            arrow0.Opacity = 0;

            arrow2Transform.BeginAnimation(TranslateTransform.XProperty, null);
            arrow2.BeginAnimation(UIElement.OpacityProperty, null);
            arrow0Transform.BeginAnimation(TranslateTransform.XProperty, null);
            arrow0.BeginAnimation(UIElement.OpacityProperty, null);
        };

        Timeline.SetDesiredFrameRate(slideOut2, VNotch.Services.AnimationConfig.TargetFps);
        arrow2Transform.BeginAnimation(TranslateTransform.XProperty, slideOut2);
        Timeline.SetDesiredFrameRate(fadeOut2, VNotch.Services.AnimationConfig.TargetFps);
        arrow2.BeginAnimation(UIElement.OpacityProperty, fadeOut2);
        Timeline.SetDesiredFrameRate(slideIn0, VNotch.Services.AnimationConfig.TargetFps);
        arrow0Transform.BeginAnimation(TranslateTransform.XProperty, slideIn0);
        Timeline.SetDesiredFrameRate(fadeIn0, VNotch.Services.AnimationConfig.TargetFps);
        arrow0.BeginAnimation(UIElement.OpacityProperty, fadeIn0);
    }
}

