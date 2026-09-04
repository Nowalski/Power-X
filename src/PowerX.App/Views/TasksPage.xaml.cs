using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Startup;

namespace PowerX.App.Views;

public sealed partial class TasksPage : Page
{
    private IReadOnlyList<ScheduledTaskInfo> _all = [];
    private string _filter = "";
    private bool _reviewedOnly;
    private bool _loaded;

    public TasksPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (_loaded) { _filter = Filter.Text.Trim(); Render(); } }
    private void Filter_Toggled(object sender, RoutedEventArgs e) { _reviewedOnly = ReviewedOnly.IsChecked == true; Render(); }

    private async Task LoadAsync()
    {
        _loaded = false;
        RefreshButton.IsEnabled = false;
        Summary.Text = "Reading the Task Scheduler...";
        try
        {
            _all = Services.DemoData.Active
                ? Services.DemoData.ScheduledTasks()
                : await Task.Run(TaskInventory.Enumerate);
        }
        catch (Exception ex)
        {
            App.Log("Tasks.Load", ex);
            Summary.Text = "Could not read scheduled tasks: " + ex.Message;
            return;
        }
        finally { RefreshButton.IsEnabled = true; }
        _loaded = true;
        Render();
    }

    private void Render()
    {
        int telemetry = _all.Count(t => t.Stance == TaskStance.Telemetry && t.Enabled);
        Summary.Text = $"{_all.Count} tasks, {_all.Count(t => t.Enabled)} enabled."
                     + (telemetry > 0 ? $"  {telemetry} enabled telemetry task(s) you can turn off." : "");

        IEnumerable<ScheduledTaskInfo> shown = _all;
        if (_reviewedOnly) shown = shown.Where(t => t.Stance != TaskStance.Unreviewed);
        if (_filter.Length > 0)
            shown = shown.Where(t =>
                t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                t.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                t.Action.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        List.Children.Clear();
        var order = new[] { TaskStance.Telemetry, TaskStance.Optional, TaskStance.Unreviewed, TaskStance.KeepSystem };
        foreach (var stance in order)
        {
            var items = shown.Where(t => t.Stance == stance)
                             .OrderBy(t => t.Folder, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                             .ToList();
            if (items.Count == 0) continue;

            List.Children.Add(new TextBlock
            {
                Text = $"{StanceHeading(stance)}  ({items.Count})",
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                Margin = new Thickness(2, 12, 0, 2),
            });
            foreach (var t in items) List.Children.Add(BuildRow(t));
        }
        if (List.Children.Count == 0)
            List.Children.Add(new TextBlock { Text = "No tasks match.", Style = (Style)Application.Current.Resources["MutedStyle"] });
    }

    private Border BuildRow(ScheduledTaskInfo t)
    {
        var text = new StackPanel { Spacing = 2 };

        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        titleRow.Children.Add(new TextBlock
        {
            Text = t.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        titleRow.Children.Add(Chip(t.Folder == "\\" ? "\\" : t.Folder, (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
        if (t.Stance != TaskStance.Unreviewed)
            titleRow.Children.Add(Chip(StanceTag(t.Stance), StanceBrush(t.Stance)));
        if (t.Hidden)
            titleRow.Children.Add(Chip("hidden", (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
        text.Children.Add(titleRow);

        if (!string.IsNullOrWhiteSpace(t.StanceNote))
            text.Children.Add(new TextBlock
            {
                Text = t.StanceNote, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(t.Triggers)) meta.Add(t.Triggers);
        if (t.LastRun is { } lr) meta.Add($"last ran {lr.LocalDateTime:d}"
            + (t.LastResult != 0 ? $" (result 0x{t.LastResult:X})" : ""));
        text.Children.Add(new TextBlock
        {
            Text = string.Join("   ", meta.Prepend(t.Action.Length > 0 ? t.Action : "(no exec action)")),
            FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        bool canToggle = t.Stance != TaskStance.KeepSystem;
        var toggle = new ToggleSwitch
        {
            IsOn = t.Enabled, OnContent = "On", OffContent = "Off", IsEnabled = canToggle,
            VerticalAlignment = VerticalAlignment.Center, Tag = t,
        };
        toggle.Toggled += Toggle_Toggled;

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0); Grid.SetColumn(toggle, 1);
        grid.Children.Add(text); grid.Children.Add(toggle);

        return new Border { Style = (Style)Application.Current.Resources["CardStyle"], Margin = new Thickness(0, 3, 0, 3), Child = grid };
    }

    private async void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: ScheduledTaskInfo t } sw) return;
        if (sw.IsOn == t.Enabled) return;
        if (Services.DemoData.Active) return;

        var r = ScheduledTasks.SetEnabled(t.Path, sw.IsOn);
        if (!r.Success)
        {
            sw.Toggled -= Toggle_Toggled; sw.IsOn = t.Enabled; sw.Toggled += Toggle_Toggled;
            await new ContentDialog
            {
                Title = "Could not change this task", Content = r.Message ?? "Unknown error.",
                CloseButtonText = "OK", XamlRoot = XamlRoot,
            }.ShowAsync();
            return;
        }
        await LoadAsync();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            nint hwnd = App.Window is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;
            string? path = Services.NativeFileDialog.SaveFile(hwnd, "powerx-scheduled-tasks.csv", "csv", "Save the task list");
            if (string.IsNullOrEmpty(path)) return;

            Services.CsvExport.Write(path,
                ["Path", "Name", "Enabled", "Stance", "Triggers", "Action", "LastRun"],
                _all.Select(t => (IReadOnlyList<string>)[
                    t.Path, t.Name, t.Enabled ? "yes" : "no", t.Stance.ToString(), t.Triggers, t.Action,
                    t.LastRun?.ToString("yyyy-MM-dd HH:mm") ?? "",
                ]));
        }
        catch (Exception ex) { App.Log("Tasks.Export", ex); }
    }

    private static string StanceHeading(TaskStance s) => s switch
    {
        TaskStance.Telemetry => "Telemetry and reporting",
        TaskStance.Optional => "Third-party updaters and helpers",
        TaskStance.Unreviewed => "Everything else",
        TaskStance.KeepSystem => "Windows components (left alone)",
        _ => s.ToString(),
    };

    private static string StanceTag(TaskStance s) => s switch
    {
        TaskStance.Telemetry => "telemetry",
        TaskStance.Optional => "optional",
        TaskStance.KeepSystem => "keep",
        _ => "",
    };

    private static Brush StanceBrush(TaskStance s) => (Brush)Application.Current.Resources[s switch
    {
        TaskStance.Telemetry => "SystemFillColorCautionBrush",
        TaskStance.Optional => "SystemFillColorSuccessBrush",
        _ => "TextFillColorSecondaryBrush",
    }];

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
