using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;
using PowerX.Core.Diagnostics.Crash;

namespace PowerX.App.Views;

public sealed partial class CrashPage : Page
{
    private bool _loaded;
    private int _scanGen;

    public CrashPage()
    {
        InitializeComponent();
        PageLayout.CenterCap(this, Root, 1000);
        _loaded = true;
        _ = ScanAsync();
    }

    private void Range_Changed(object sender, SelectionChangedEventArgs e) { if (_loaded) _ = ScanAsync(); }
    private void Dumps_Changed(object sender, RoutedEventArgs e) { if (_loaded) _ = ScanAsync(); }
    private void Rescan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync();

    private async Task ScanAsync()
    {
        int gen = ++_scanGen;   // supersede any scan still running
        RescanButton.IsEnabled = false;
        Summary.Text = "Scanning what Windows recorded…";
        List.Children.Clear();

        int days = RangeBox.SelectedIndex switch { 0 => 7, 2 => 90, _ => 30 };
        bool dumps = DumpsBox.IsChecked == true;

        var opt = new CrashScanner.ScanOptions
        {
            Window = TimeSpan.FromDays(days),
            ReadDumps = dumps,
            IncludeMachineStore = dumps || PrivilegeCheck.IsElevated(),
            Max = 80,
        };

        IReadOnlyList<CrashInsight> insights;
        try
        {
            insights = await Task.Run(() => CrashScanner.Scan(opt));
        }
        catch (Exception ex)
        {
            App.Log("CrashScan", ex);
            if (gen != _scanGen) return;
            Summary.Text = "Could not read crash history: " + ex.Message;
            RescanButton.IsEnabled = true;
            return;
        }

        if (gen != _scanGen) return;   // a newer scan started while this one ran
        RescanButton.IsEnabled = true;

        if (dumps && !PrivilegeCheck.IsElevated())
            List.Children.Add(Note("Run PowerX as administrator to read the machine crash store and inspect dumps. Showing the reports visible to your account only."));

        if (insights.Count == 0)
        {
            Summary.Text = $"No crashes, hangs or stop errors recorded in the last {days} days.";
            List.Children.Add(Note("Nothing to report. That's the good outcome."));
            return;
        }

        Summary.Text = $"{insights.Count} event{(insights.Count == 1 ? "" : "s")} in the last {days} days · "
            + string.Join(" · ", insights.GroupBy(i => i.Kind)
                .Select(g => $"{g.Count()} {KindWord(g.Key, g.Count() != 1)}"));

        foreach (var i in insights)
            List.Children.Add(BuildCard(i));
    }

    private Border BuildCard(CrashInsight i)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(new TextBlock
        {
            Text = i.Subject,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        header.Children.Add(Chip(KindWord(i.Kind), KindBrush(i.Kind)));
        header.Children.Add(Chip(ConfWord(i.Confidence), ConfBrush(i.Confidence)));
        header.Children.Add(new TextBlock
        {
            Text = i.When.LocalDateTime.ToString("ddd d MMM, HH:mm"),
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(0, 6, 0, 2) };
        AddSection(body, "Observed facts", i.Facts);
        AddSection(body, "Likely cause", i.LikelyCauses);
        AddSection(body, "What you can try", i.Remediation);
        AddSection(body, "Still missing", i.Missing, tertiary: true);

        if (i.ArtifactPath is { } path)
        {
            var open = new Button { Content = "Open the report folder", Margin = new Thickness(0, 4, 0, 0) };
            open.Click += (_, _) =>
            {
                try
                {
                    var target = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
                }
                catch (Exception ex) { App.Log("Crash.OpenFolder", ex); }
            };
            body.Children.Add(open);
        }
        body.Children.Add(new TextBlock
        {
            Text = $"Source: {i.Source}. PowerX did not download symbols, open a dump in a debugger, or upload anything.",
            FontSize = 11, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        var expander = new Expander
        {
            Header = header,
            Content = body,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = i.Confidence >= CrashConfidence.Moderate,
        };
        return new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(6, 0, 6, 0),
            Child = expander,
        };
    }

    private static void AddSection(StackPanel host, string title, IReadOnlyList<string> lines, bool tertiary = false)
    {
        if (lines.Count == 0) return;
        host.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        foreach (var l in lines)
            host.Children.Add(new TextBlock
            {
                Text = (l.StartsWith("  ") ? "" : "•  ") + l.Trim(),
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(l.StartsWith("  ") ? 14 : 0, 0, 0, 0),
                Foreground = tertiary
                    ? (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            });
    }

    private TextBlock Note(string text) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 0, 4),
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
    };

    private static string KindWord(CrashKind k, bool plural = false)
    {
        string s = k switch
        {
            CrashKind.Bugcheck => "stop error",
            CrashKind.AppHang => "hang",
            CrashKind.ManagedException => ".NET crash",
            CrashKind.LiveKernelReport => "kernel reset",
            _ => "crash",
        };
        if (!plural) return s;
        return s.EndsWith("crash") ? s + "es" : s + "s";
    }

    private static string ConfWord(CrashConfidence c) => c switch
    {
        CrashConfidence.High => "high confidence",
        CrashConfidence.Moderate => "moderate confidence",
        CrashConfidence.Low => "low confidence",
        _ => "insufficient evidence",
    };

    private Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };

    private static Brush KindBrush(CrashKind k) => (Brush)Application.Current.Resources[
        k == CrashKind.Bugcheck ? "SystemFillColorCriticalBrush"
        : k == CrashKind.AppHang ? "SystemFillColorCautionBrush"
        : "TextFillColorSecondaryBrush"];

    private static Brush ConfBrush(CrashConfidence c) => (Brush)Application.Current.Resources[
        c == CrashConfidence.High ? "SystemFillColorSuccessBrush"
        : c == CrashConfidence.Moderate ? "SystemFillColorCautionBrush"
        : "TextFillColorTertiaryBrush"];
}
