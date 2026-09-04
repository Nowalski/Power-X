using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed partial class EventLogPage : Page
{
    private IReadOnlyList<EventGroup> _all = [];
    private string _filter = "";
    private bool _loaded;

    public EventLogPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (_loaded) { _filter = Filter.Text.Trim(); Render(); } }
    private void Reload(object sender, RoutedEventArgs e) { if (_loaded) _ = LoadAsync(); }
    private void Reload(object sender, SelectionChangedEventArgs e) { if (_loaded) _ = LoadAsync(); }

    private async Task LoadAsync()
    {
        _loaded = false;
        RefreshButton.IsEnabled = false;
        Summary.Text = "Reading the event logs...";
        var window = WindowBox.SelectedIndex switch { 0 => TimeSpan.FromHours(24), 2 => TimeSpan.FromDays(30), _ => TimeSpan.FromDays(7) };
        bool warnings = WarnToggle.IsChecked == true;

        try
        {
            _all = Services.DemoData.Active
                ? Services.DemoData.EventGroups()
                : await EventLogBrowser.ReadAsync(window, warnings);
        }
        catch (Exception ex)
        {
            App.Log("EventLog.Load", ex);
            Summary.Text = "Could not read the event logs: " + ex.Message;
            return;
        }
        finally { RefreshButton.IsEnabled = true; }
        _loaded = true;
        Render();
    }

    private void Render()
    {
        int crit = _all.Count(g => g.Level == EventLevel2.Critical);
        int err = _all.Count(g => g.Level == EventLevel2.Error);
        Summary.Text = _all.Count == 0
            ? "Nothing logged in this window. That is a good sign."
            : $"{_all.Count} distinct entries: {crit} critical, {err} error source(s). Grouped by source and id, most frequent first.";

        IEnumerable<EventGroup> shown = _all;
        if (_filter.Length > 0)
            shown = shown.Where(g =>
                g.Provider.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                g.SampleMessage.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                g.EventId.ToString().Contains(_filter) ||
                (g.Explanation?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false));

        List.Children.Clear();
        foreach (var g in shown.Take(400))
            List.Children.Add(BuildRow(g));
        if (List.Children.Count == 0)
            List.Children.Add(new TextBlock { Text = "No entries match.", Style = (Style)Application.Current.Resources["MutedStyle"] });
    }

    private Border BuildRow(EventGroup g)
    {
        var (levelText, brush) = g.Level switch
        {
            EventLevel2.Critical => ("critical", (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]),
            EventLevel2.Error => ("error", (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]),
            _ => ("warning", (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]),
        };

        var text = new StackPanel { Spacing = 2 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock
        {
            Text = g.Provider, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        head.Children.Add(Chip($"id {g.EventId}", (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
        head.Children.Add(Chip(levelText, brush));
        head.Children.Add(Chip($"x{g.Count}", (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
        head.Children.Add(Chip(g.Log.ToLowerInvariant(), (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
        text.Children.Add(head);

        if (g.Explanation is { } why)
            text.Children.Add(new TextBlock
            {
                Text = why, FontSize = 12.5, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
            });
        else if (!string.IsNullOrWhiteSpace(g.SampleMessage))
            text.Children.Add(new TextBlock
            {
                Text = g.SampleMessage, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });

        text.Children.Add(new TextBlock
        {
            Text = g.Count == 1
                ? $"{g.LastSeen.LocalDateTime:g}"
                : $"{g.Count} times, {g.FirstSeen.LocalDateTime:d} to {g.LastSeen.LocalDateTime:g}",
            FontSize = 11,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Margin = new Thickness(0, 3, 0, 3), Child = text };
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            nint hwnd = App.Window is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;
            string? path = Services.NativeFileDialog.SaveFile(hwnd, "powerx-event-log.csv", "csv", "Save the event log summary");
            if (string.IsNullOrEmpty(path)) return;

            Services.CsvExport.Write(path,
                ["Log", "Provider", "EventId", "Level", "Count", "FirstSeen", "LastSeen", "Message"],
                _all.Select(g => (IReadOnlyList<string>)[
                    g.Log, g.Provider, g.EventId.ToString(), g.Level.ToString(), g.Count.ToString(),
                    g.FirstSeen.LocalDateTime.ToString("yyyy-MM-dd HH:mm"), g.LastSeen.LocalDateTime.ToString("yyyy-MM-dd HH:mm"),
                    g.Explanation ?? g.SampleMessage,
                ]));
        }
        catch (Exception ex) { App.Log("EventLog.Export", ex); }
    }

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
