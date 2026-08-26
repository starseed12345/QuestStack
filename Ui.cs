using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace QuestStack;

internal static class Ui
{
    public static readonly IBrush Background = Brush("#0B0E12");
    public static readonly IBrush Panel = Brush("#12171D");
    public static readonly IBrush Raised = Brush("#171D24");
    public static readonly IBrush Border = Brush("#2A323D");
    public static readonly IBrush PrimaryText = Brush("#F2F5F7");
    public static readonly IBrush SecondaryText = Brush("#98A4B3");
    public static readonly IBrush Accent = Brush("#D7A84B");
    public static readonly IBrush Success = Brush("#56C596");
    public static readonly IBrush Danger = Brush("#F06A6A");
    public static readonly IBrush Info = Brush("#6EA8D9");

    public static Border Card(Control content, Thickness? padding = null)
    {
        return new Border
        {
            Background = Panel,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = padding ?? new Thickness(20),
            Child = content
        };
    }

    public static TextBlock Text(string text, double size = 14, IBrush? color = null, FontWeight? weight = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = color ?? PrimaryText,
            FontWeight = weight ?? FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };
    }

    public static Button PrimaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = Accent,
            Foreground = Brush("#17130B"),
            FontWeight = FontWeight.SemiBold,
            Padding = new Thickness(18, 11),
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }

    public static Button SecondaryButton(string text)
    {
        return new Button
        {
            Content = text,
            Background = Raised,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Foreground = PrimaryText,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
    }

    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
