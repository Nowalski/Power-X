using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed partial class HealthCheckPage : Page
{
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _scannedOnce;

    public HealthCheckPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (!_scannedOnce) { _scannedOnce = true; _ = ScanAsync(); } };
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync();

    private async Task ScanAsync()
    {
        if (_busy) return;
        _busy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var cts = _cts;

        ScanButton.IsEnabled = false;
        DeepToggle.IsEnabled = false;
        ScanHeadline.Text = "Scanning...";
        ScanDetail.Text = DeepToggle.IsChecked == true
            ? "This includes the component-store analysis, which can take up to a minute."
            : "Usually a few seconds.";

        try
        {
            HealthReport report = Services.DemoData.Active
                ? Services.DemoData.HealthReport()
                : await HealthCheck.ScanAsync(DeepToggle.IsChecked == true, cts.Token);
            if (cts.IsCancellationRequested) return;
            Render(report);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log("HealthCheck.Scan", ex);
            ScanHeadline.Text = "Could not finish the scan";
            ScanDetail.Text = ex.Message;
        }
        finally
        {
            ScanButton.IsEnabled = true;
            DeepToggle.IsEnabled = true;
            _busy = false;
        }
    }

    private void Render(HealthReport report)
    {
        ScoreText.Text = report.Score.ToString();
        ScoreText.Foreground = (Brush)Application.Current.Resources[report.Score switch
        {
            >= 85 => "SystemFillColorSuccessBrush",
            >= 60 => "SystemFillColorCautionBrush",
            _ => "SystemFillColorCriticalBrush",
        }];

        ScanHeadline.Text = report.Items.Count == 0
            ? "Nothing to report"
            : $"{report.Items.Count} item{(report.Items.Count == 1 ? "" : "s")} worth a look "
              + $"({report.High} high, {report.Medium} medium, {report.Low} low impact)";
        ScanDetail.Text = $"Scanned {report.When.LocalDateTime:ddd d MMM, HH:mm}"
                         + (report.Deep ? "  ·  included the component-store analysis." : "");

        List.Children.Clear();
        if (report.Items.Count == 0)
        {
            List.Children.Add(new TextBlock
            {
                Text = "Nothing outstanding in any of PowerX's checks. That is a good sign — it does not mean the machine is perfect, only that nothing here needs attention right now.",
                Style = (Style)Application.Current.Resources["MutedStyle"],
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (var group in report.Items.GroupBy(i => i.Impact).OrderBy(g => g.Key))
        {
            List.Children.Add(new TextBlock
            {
                Text = $"{ImpactHeading(group.Key)}  ({group.Count()})",
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                Margin = new Thickness(2, 10, 0, 2),
            });
            foreach (var item in group) List.Children.Add(BuildRow(item));
        }
    }

    private Border BuildRow(Recommendation r)
    {
        var brush = (Brush)Application.Current.Resources[r.Impact switch
        {
            RecommendationImpact.High => "SystemFillColorCriticalBrush",
            RecommendationImpact.Medium => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        }];

        var text = new StackPanel { Spacing = 2 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock
        {
            Text = r.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        head.Children.Add(Chip(r.Category, (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
        text.Children.Add(head);
        text.Children.Add(new TextBlock
        {
            Text = r.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bar = new Border { Background = brush, CornerRadius = new CornerRadius(3) };
        Grid.SetColumn(bar, 0);
        Grid.SetColumn(text, 1);
        grid.Children.Add(bar);
        grid.Children.Add(text);

        if (r.NavigateTag is not null)
        {
            var go = new Button { Content = r.NavigateLabel ?? "Go to page", VerticalAlignment = VerticalAlignment.Center, Tag = r.NavigateTag };
            go.Click += (s, _) => App.Window?.Navigate((string)((Button)s).Tag);
            Grid.SetColumn(go, 2);
            grid.Children.Add(go);
        }

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Padding = new Thickness(12, 10, 12, 11), Child = grid };
    }

    private static string ImpactHeading(RecommendationImpact i) => i switch
    {
        RecommendationImpact.High => "Worth doing soon",
        RecommendationImpact.Medium => "Worth a look",
        _ => "Minor",
    };

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
