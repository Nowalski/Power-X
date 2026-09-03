using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Startup;

namespace PowerX.App.Views;

public sealed partial class StartupPage : Page
{
    private List<StartupEntry> _entries = [];
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
        try
        {
            _entries = (await Task.Run(() => StartupProvider.Enumerate())).ToList();
        }
        catch (Exception ex)
        {
            App.Log("Startup.Enumerate", ex);
            Summary.Text = "Could not read startup entries: " + ex.Message;
            return;
        }
        Summary.Text = $"{_entries.Count} startup entries · {_entries.Count(e => e.Enabled)} enabled";
        _loaded = true;
        Render();
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
