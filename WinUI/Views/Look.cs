using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace VNotch.WinUI.Views;

/// <summary>Fonts and colours of the WPF notch.</summary>
internal static class Look
{
    // The WPF notch ships SF Pro Display (Fonts/) and falls back to Segoe UI.
    public static readonly FontFamily Regular = new("ms-appx:///Fonts/SF-Pro-Display-Regular.otf#SF Pro Display, Segoe UI Variable Text");
    public static readonly FontFamily Medium = new("ms-appx:///Fonts/SF-Pro-Display-Medium.otf#SF Pro Display, Segoe UI Variable Text");
    public static readonly FontFamily Semibold = new("ms-appx:///Fonts/SF-Pro-Display-Semibold.otf#SF Pro Display, Segoe UI Variable Text");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static readonly SolidColorBrush White = new(Color.FromArgb(255, 255, 255, 255));
    public static readonly SolidColorBrush Secondary = new(Color.FromArgb(0xA6, 255, 255, 255));
    public static readonly SolidColorBrush Tertiary = new(Color.FromArgb(0x8C, 255, 255, 255));
    public static readonly SolidColorBrush Track = new(Color.FromArgb(0x33, 255, 255, 255));
    public static readonly SolidColorBrush Fill = new(Color.FromArgb(0xE6, 255, 255, 255));
    public static readonly SolidColorBrush Clear = new(Color.FromArgb(0, 0, 0, 0));

    public static TextBlock Text(string text, double size, FontFamily font, Brush brush, FontWeight weight) => new()
    {
        Text = text,
        FontSize = size,
        FontFamily = font,
        FontWeight = weight,
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        IsHitTestVisible = false,
    };

    public static FontIcon Icon(string glyph, double size, Brush brush) => new()
    {
        Glyph = glyph,
        FontSize = size,
        FontFamily = Icons,
        Foreground = brush,
        IsHitTestVisible = false,
    };

    public static UIElement At(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        return element;
    }
}
