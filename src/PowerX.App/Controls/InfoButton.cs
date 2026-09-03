using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PowerX.App.Controls;

/// <summary>
/// A small "?" that opens a short explainer flyout. Teaches the concept behind a section
/// without cluttering it with paragraphs.
/// </summary>
public sealed class InfoButton : Button
{
    public InfoButton()
    {
        Content = new TextBlock { Text = "?", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Width = 22;
        Height = 22;
        Padding = new Thickness(0);
        CornerRadius = new CornerRadius(11);
        VerticalAlignment = VerticalAlignment.Center;
        Opacity = 0.75;
    }

    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Flyout ??= new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 6,
                MaxWidth = 340,
                Children =
                {
                    new TextBlock { Text = Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = Body, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                },
            },
        };
    }
}
