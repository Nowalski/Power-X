using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Startup;

namespace PowerX.App.Views;

public sealed partial class StartupPage : Page
{
    private List<StartupEntry> _entries = [];
    private IReadOnlyList<BootItem> _bootItems = [];
    private string _filter = "";
    private bool _loaded;

    public StartupPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loaded = false;
        BootTimeline? boot;
        try
        {
            if (Services.DemoData.Active)
            {
                _entries = Services.DemoData.StartupEntries().ToList();
                boot = Services.DemoData.BootTimeline();
                _bootItems = Services.DemoData.BootItems();
            }
            else
            {
                (_entries, (boot, _bootItems)) = await Task.Run(() =>
                    (StartupProvider.Enumerate().ToList(), BootPerformance.Read()));
            }
        }
        catch (Exception ex)
        {
            App.Log("Startup.Enumerate", ex);
            Summary.Text = "Could not read startup entries: " + ex.Message;
            return;
        }
        Summary.Text = $"{_entries.Count} startup entries · {_entries.Count(e => e.Enabled)} enabled";
        ShowBootCard(boot);
        _loaded = true;
        Render();
    }

    private void ShowBootCard(BootTimeline? b)
    {
        if (b is null || b.LastBootMs <= 0) { BootCard.Visibility = Visibility.Collapsed; return; }
        BootCard.Visibility = Visibility.Visible;

        double lastS = b.LastBootMs / 1000.0;
        BootHeadline.Text = $"Last boot took {lastS:0.0} s";
        if (b.MainPathMs > 0)
            BootHeadline.Text += $"  ({b.MainPathMs / 1000.0:0.0} s to the desktop)";

        var parts = new List<string>();
        if (b.AverageBootMs > 0)
        {
            double avgS = b.AverageBootMs / 1000.0;
            double delta = lastS - avgS;
            parts.Add(Math.Abs(delta) < 1.5
                ? $"about the same as your recent average ({avgS:0.0} s)"
                : delta > 0 ? $"{delta:0.0} s slower than your recent average" : $"{-delta:0.0} s faster than your recent average");
        }
        if (b.StartupAppCount > 0) parts.Add($"{b.StartupAppCount} startup apps");
        if (b.Degraded) parts.Add("Windows flagged this boot as slower than usual");
        BootDetail.Text = string.Join(".   ", parts) + (parts.Count > 0 ? "." : "")
                        + "   From the same data as Task Manager's Startup impact.";
    }

    private BootItem? BootFor(StartupEntry e)
    {
        if (_bootItems.Count == 0) return null;
        string? exe = e.ExecutablePath is { } p ? System.IO.Path.GetFileName(p) : null;
        return _bootItems.FirstOrDefault(b =>
            (exe is not null && b.Path is { } bp && string.Equals(System.IO.Path.GetFileName(bp), exe, StringComparison.OrdinalIgnoreCase)) ||
            b.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase) ||
            (e.Publisher is not null && b.Name.Equals(e.Publisher, StringComparison.OrdinalIgnoreCase)));
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _filter = Filter.Text.Trim();
        Render();
    }

    private void Render()
    {
        var shown = _entries.AsEnumerable();
        if (_filter.Length > 0)
            shown = shown.Where(e =>
                e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                e.Command.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                (e.Publisher?.Contains(_filter, StringComparison.OrdinalIgnoreCase) ?? false));

        List.Children.Clear();
        foreach (var group in shown.GroupBy(e => e.SourceLabel).OrderBy(g => g.Key))
        {
            List.Children.Add(new TextBlock
            {
                Text = group.Key,
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                Margin = new Thickness(2, 12, 0, 2),
            });
            foreach (var entry in group.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                List.Children.Add(BuildRow(entry));
        }
        if (List.Children.Count == 0)
            List.Children.Add(new TextBlock { Text = "No entries match.", Style = (Style)Application.Current.Resources["MutedStyle"], Margin = new Thickness(2, 8, 0, 0) });
    }

    private Border BuildRow(StartupEntry entry)
    {
        var text = new StackPanel { Spacing = 2 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock { Text = entry.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
        if (entry.Publisher is not null)
            titleRow.Children.Add(Chip(entry.Publisher, (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
        if (entry.RequiresAdmin)
            titleRow.Children.Add(Chip("all users", (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
        if (BootFor(entry) is { Impact: not StartupImpact.NotMeasured } b)
        {
            var brush = b.Impact switch
            {
                StartupImpact.High => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                StartupImpact.Medium => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
                _ => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            titleRow.Children.Add(Chip($"{b.Impact} impact · +{b.DegradationMs / 1000.0:0.0}s at boot", brush));
        }
        text.Children.Add(titleRow);
        text.Children.Add(new TextBlock
        {
            Text = entry.Command, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        bool canToggle = StartupProvider.CanToggle(entry);
        var toggle = new ToggleSwitch
        {
            IsOn = entry.Enabled, OnContent = "On", OffContent = "Off",
            VerticalAlignment = VerticalAlignment.Center, Tag = entry, IsEnabled = canToggle,
        };
        toggle.Toggled += Toggle_Toggled;
        if (!canToggle)
            text.Children.Add(new TextBlock
            {
                Text = "Runs once at the next sign-in. It cannot be disabled, only removed from the ... menu.",
                FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            });

        var menuBtn = new Button { Content = "…", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4, 10, 4) };
        var flyout = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Open file location" };
        open.Click += (_, _) => { var r = StartupProvider.OpenLocation(entry); if (!r.Success) _ = Info("Open location", r.Message); };
        flyout.Items.Add(open);
        if (StartupProvider.CanRemove(entry))
        {
            var remove = new MenuFlyoutItem { Text = "Remove entry" };
            remove.Click += async (_, _) => await RemoveEntry(entry);
            flyout.Items.Add(remove);
        }
        menuBtn.Flyout = flyout;

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0); Grid.SetColumn(toggle, 1); Grid.SetColumn(menuBtn, 2);
        grid.Children.Add(text); grid.Children.Add(toggle); grid.Children.Add(menuBtn);

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Margin = new Thickness(0, 3, 0, 3), Child = grid };
    }

    private async void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: StartupEntry entry } sw) return;
        if (sw.IsOn == entry.Enabled) return;

        var result = StartupProvider.SetEnabled(entry, sw.IsOn);
        if (!result.Success)
        {
            sw.Toggled -= Toggle_Toggled; sw.IsOn = entry.Enabled; sw.Toggled += Toggle_Toggled;
            await Info("Could not change this entry", result.Message);
            return;
        }
        await LoadAsync();
    }

    private async Task RemoveEntry(StartupEntry entry)
    {
        var confirm = new ContentDialog
        {
            Title = $"Remove “{entry.Name}”?",
            Content = "This deletes the RunOnce value so it won't run at the next sign-in. "
                    + "PowerX saves a copy under HKCU\\SOFTWARE\\PowerX\\RemovedRunOnce so you can put it back by hand.",
            PrimaryButtonText = "Remove", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        var r = StartupProvider.Remove(entry);
        if (!r.Success) await Info("Could not remove this entry", r.Message);
        await LoadAsync();
    }

    private async Task Info(string title, string? body) => await new ContentDialog
    {
        Title = title, Content = body ?? "", CloseButtonText = "OK", XamlRoot = XamlRoot,
    }.ShowAsync();

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
