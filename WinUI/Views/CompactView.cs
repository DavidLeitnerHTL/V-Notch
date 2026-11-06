using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static VNotch.WinUI.Views.Look;

namespace VNotch.WinUI.Views;

/// <summary>The closed notch with nothing playing: the WPF IdleGlance, the time on the left.</summary>
internal sealed class CompactView
{
    private readonly TextBlock _clock;

    public Grid Root { get; }

    public CompactView(double width, double height)
    {
        Root = new Grid
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        _clock = Text("", 12.5, Semibold, White, FontWeights.SemiBold);
        _clock.Margin = new Thickness(13, 0, 12, 0);
        _clock.VerticalAlignment = VerticalAlignment.Center;
        _clock.HorizontalAlignment = HorizontalAlignment.Left;
        Root.Children.Add(_clock);
    }

    public void SetClock(DateTime now) => _clock.Text = now.ToString("t", CultureInfo.CurrentCulture);
}
