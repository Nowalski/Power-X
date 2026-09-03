using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerX.Core.Transactions;

namespace PowerX.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        var records = new ChangeLog().ReadAll();
        Timeline.Children.Clear();

        if (records.Count == 0)
        {
            Timeline.Children.Add(new TextBlock
            {
                Text = "No changes recorded yet. Changes you make in Tweaks appear here.",
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            return;
        }

        foreach (var day in records.OrderByDescending(r => r.Timestamp)
                     .GroupBy(r => r.Timestamp.LocalDateTime.Date))
        {
            Timeline.Children.Add(new TextBlock
            {
                Text = day.Key.ToString("dddd, d MMMM"),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(0, 12, 0, 0),
            });

            foreach (var r in day)
            {
                var card = new Border
                {
                    Style = (Style)Application.Current.Resources["CardStyle"],
                    Child = new StackPanel
                    {
                        Spacing = 2,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"{r.Timestamp.LocalDateTime:HH:mm}  ·  {r.Action}  {r.TweakName}",
                                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                            },
                            new TextBlock
                            {
                                Text = r.Success
                                    ? (r.PreviousState == r.ResultingState
                                        ? $"No change, already {r.ResultingState}"
                                        : $"{r.PreviousState} → {r.ResultingState}")
                                    : $"Failed: {r.Message}",
                                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            },
                            new TextBlock
                            {
                                Text = $"{r.TweakId}  ·  build {r.WindowsBuild}  ·  session {r.SessionId}",
                                FontSize = 11,
                                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                            },
                        },
                    },
                };
                Timeline.Children.Add(card);
            }
        }
    }
}
