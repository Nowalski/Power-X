using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Debloat;

namespace PowerX.App.Views;

public sealed partial class DebloatPage : Page
{
    private readonly AppInventory _inventory = new();
    private List<AppVm> _apps = [];
    private string _filter = "";
    private string _category = "All categories";
    private bool _loaded;

    public DebloatPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loaded = false;
        Summary.Text = "Scanning installed apps…";
        Sections.Children.Clear();
        RefreshButton.IsEnabled = false;

        List<PowerX.Core.Debloat.InstalledApp> installed;
        try
        {
            installed = (await Task.Run(() => _inventory.Enumerate())).ToList();
        }
        catch (Exception ex)
        {
            App.Log("Debloat.Enumerate", ex);
            Summary.Text = "Could not read installed apps: " + ex.Message;
            RefreshButton.IsEnabled = true;
            return;
        }
        _apps = installed.Select(a => new AppVm { App = a }).ToList();
        foreach (var vm in _apps) vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AppVm.Selected)) UpdateSelection(); };

        int removable = _apps.Count(a => a.CanRemove);
        Summary.Text = $"{_apps.Count} apps installed · {removable} removable · {_apps.Count(a => a.App.Catalog is not null)} in the curated catalog";

        var cats = new List<string> { "All categories" };
        cats.AddRange(_apps.Select(a => a.Category).Distinct().OrderBy(c => c));
        CategoryBox.ItemsSource = cats;
        CategoryBox.SelectedIndex = 0;

        RefreshButton.IsEnabled = true;
        _loaded = true;
        Render();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _filter = Filter.Text.Trim();
        Render();
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || CategoryBox.SelectedItem is not string c) return;
        _category = c;
        Render();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Render()
    {
        IEnumerable<AppVm> shown = _apps;
        if (_category != "All categories") shown = shown.Where(a => a.Category == _category);
        if (_filter.Length > 0)
            shown = shown.Where(a =>
                a.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                a.App.PackageFamilyName.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        Sections.Children.Clear();
        foreach (var group in shown.GroupBy(a => a.Category).OrderBy(g => g.Key))
        {
            Sections.Children.Add(new TextBlock
            {
                Text = group.Key,
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                Margin = new Thickness(2, 12, 0, 2),
            });
            foreach (var vm in group.OrderByDescending(a => a.App.Class == RemovalClass.RecommendedRemovable).ThenBy(a => a.DisplayName))
                Sections.Children.Add(BuildRow(vm));
        }

        if (Sections.Children.Count == 0)
            Sections.Children.Add(new TextBlock { Text = "No apps match.", Style = (Style)Application.Current.Resources["MutedStyle"], Margin = new Thickness(2, 8, 0, 0) });
    }

    private Border BuildRow(AppVm vm)
    {
        var check = new CheckBox { IsChecked = vm.Selected, IsEnabled = vm.CanRemove, VerticalAlignment = VerticalAlignment.Center, MinWidth = 32 };
        check.Checked += (_, _) => vm.Selected = true;
        check.Unchecked += (_, _) => vm.Selected = false;

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(Chip(vm.ClassLabel, ClassBrush(vm.App.Class)));
        if (vm.CanRemove) chips.Children.Add(Chip(vm.RestoreLabel, (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock { Text = vm.DisplayName, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(chips);

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = vm.Description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        text.Children.Add(new TextBlock { Text = $"{vm.Publisher} · {vm.App.PackageFamilyName}", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] });

        var remove = new Button { Content = "Remove", VerticalAlignment = VerticalAlignment.Center, IsEnabled = vm.CanRemove };
        remove.Click += async (_, _) => await RemoveOne(vm, remove);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(check, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(remove, 2);
        grid.Children.Add(check);
        grid.Children.Add(text);
        grid.Children.Add(remove);

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Margin = new Thickness(0, 3, 0, 3), Child = grid };
    }

    private async Task RemoveOne(AppVm vm, Button trigger)
    {
        if (!await Confirm([vm])) return;
        trigger.IsEnabled = false;
        vm.Removing = true;
        var result = await _inventory.RemoveAsync(vm.App.PackageFullName, vm.App.PackageFamilyName);
        vm.Removing = false;

        if (result.Success)
        {
            _apps.Remove(vm);
            Render();
        }
        else
        {
            trigger.IsEnabled = true;
            await Error(vm.DisplayName, result.Message);
        }
        UpdateSelection();
    }

    private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        var picked = _apps.Where(a => a.Selected && a.CanRemove).ToList();
        if (picked.Count == 0 || !await Confirm(picked)) return;

        RemoveSelected.IsEnabled = false;
        int ok = 0, failed = 0;
        var errors = new List<string>();
        foreach (var vm in picked)
        {
            vm.Removing = true;
            var r = await _inventory.RemoveAsync(vm.App.PackageFullName, vm.App.PackageFamilyName);
            vm.Removing = false;
            if (r.Success) { _apps.Remove(vm); ok++; }
            else { failed++; errors.Add($"{vm.DisplayName}: {r.Message}"); }
        }
        Render();
        UpdateSelection();

        await new ContentDialog
        {
            Title = "Removal complete",
            Content = $"{ok} removed" + (failed > 0 ? $", {failed} failed:\n\n{string.Join('\n', errors)}" : "."),
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private void UpdateSelection()
    {
        int n = _apps.Count(a => a.Selected && a.CanRemove);
        SelectionText.Text = n == 0 ? "Nothing selected" : $"{n} app{(n == 1 ? "" : "s")} selected";
        RemoveSelected.IsEnabled = n > 0;
    }

    private async Task<bool> Confirm(IReadOnlyList<AppVm> apps)
    {
        string body = apps.Count == 1
            ? $"Remove “{apps[0].DisplayName}” for your user account?\n\n{apps[0].RestoreLabel}."
            : $"Remove these {apps.Count} apps for your user account?\n\n" +
              string.Join('\n', apps.Select(a => $"• {a.DisplayName}"));
        return await new ContentDialog
        {
            Title = apps.Count == 1 ? "Remove app" : "Remove apps",
            Content = body,
            PrimaryButtonText = "Remove",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        }.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task Error(string name, string? message) => await new ContentDialog
    {
        Title = $"Could not remove {name}",
        Content = message ?? "Unknown error.",
        CloseButtonText = "Close",
        XamlRoot = XamlRoot,
    }.ShowAsync();

    private static Brush ClassBrush(RemovalClass c) => c switch
    {
        RemovalClass.RecommendedRemovable => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x3A, 0xA0, 0x55)),
        RemovalClass.Optional => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x86, 0x8E, 0x96)),
        RemovalClass.Advanced => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x7B, 0x2A)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xD1, 0x34, 0x38)),
    };

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
