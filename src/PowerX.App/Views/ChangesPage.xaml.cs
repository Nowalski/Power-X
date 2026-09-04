using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed partial class ChangesPage : Page
{
    private List<(DateTimeOffset When, string Path)> _snapshots = [];
    private bool _loading;

    public ChangesPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        try
        {
            if (Services.DemoData.Active)
            {
                RenderDiff(Services.DemoData.SnapshotDiff());
                Summary.Text = "Comparing this week against last week.";
                FromBox.Items.Clear(); FromBox.Items.Add("7 days ago"); FromBox.SelectedIndex = 0;
                ToBox.Items.Clear(); ToBox.Items.Add("Today"); ToBox.SelectedIndex = 0;
                _loading = false;
                return;
            }

            // First run: no snapshot yet, so take one to serve as a baseline.
            await Task.Run(() => SystemSnapshot.CaptureIfStale(TimeSpan.FromHours(20)));
            _snapshots = SystemSnapshot.List().ToList();

            FromBox.Items.Clear();
            ToBox.Items.Clear();
            foreach (var s in _snapshots)
            {
                FromBox.Items.Add(Label(s.When));
                ToBox.Items.Add(Label(s.When));
            }

            if (_snapshots.Count < 2)
            {
                Summary.Text = _snapshots.Count == 1
                    ? "First snapshot taken. Check back in a day or two, or make a change and take another snapshot to see a comparison."
                    : "No snapshots yet.";
                List.Children.Clear();
                if (ToBox.Items.Count > 0) ToBox.SelectedIndex = 0;
                if (FromBox.Items.Count > 0) FromBox.SelectedIndex = 0;
                _loading = false;
                return;
            }

            ToBox.SelectedIndex = 0;                                   // newest
            FromBox.SelectedIndex = Math.Min(_snapshots.Count - 1, PickBaseline());
            _loading = false;
            Compare();
        }
        catch (Exception ex)
        {
            App.Log("Changes.Load", ex);
            Summary.Text = "Could not read snapshots: " + ex.Message;
            _loading = false;
        }
    }

    private int PickBaseline()
    {
        // Prefer the newest snapshot that is at least ~a day older than the latest.
        var newest = _snapshots[0].When;
        for (int i = 1; i < _snapshots.Count; i++)
            if (newest - _snapshots[i].When >= TimeSpan.FromHours(20)) return i;
        return _snapshots.Count - 1;
    }

    private static string Label(DateTimeOffset when)
    {
        var ago = DateTimeOffset.Now - when;
        string rel = ago.TotalMinutes < 90 ? "just now"
            : ago.TotalHours < 36 ? $"{(int)Math.Round(ago.TotalHours)} h ago"
            : $"{(int)Math.Round(ago.TotalDays)} days ago";
        return $"{when.LocalDateTime:ddd d MMM, HH:mm}  ({rel})";
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) Compare();
    }

    private void Compare()
    {
        int from = FromBox.SelectedIndex, to = ToBox.SelectedIndex;
        if (from < 0 || to < 0 || from >= _snapshots.Count || to >= _snapshots.Count) return;

        var a = SystemSnapshot.Load(_snapshots[from].Path);
        var b = SystemSnapshot.Load(_snapshots[to].Path);
        if (a is null || b is null) { Summary.Text = "One of the snapshots could not be read."; return; }

        if (a.TakenAt > b.TakenAt) (a, b) = (b, a);   // Diff wants older then newer
        RenderDiff(SystemSnapshot.Diff(a, b));
    }

    private void RenderDiff(SnapshotDiff diff)
    {
        List.Children.Clear();
        int n = diff.Changes.Count;
        Summary.Text = n == 0
            ? "No configuration changes between these two snapshots."
            : $"{n} change{(n == 1 ? "" : "s")} between {diff.FromWhen.LocalDateTime:ddd d MMM} and {diff.ToWhen.LocalDateTime:ddd d MMM}.";

        if (n == 0)
        {
            List.Children.Add(new TextBlock
            {
                Text = "Startup entries, scheduled tasks, services, programs, drivers and tweaks all match.",
                Style = (Style)Application.Current.Resources["MutedStyle"],
            });
            return;
        }

        foreach (var group in diff.Changes.GroupBy(c => c.Category))
        {
            List.Children.Add(new TextBlock
            {
                Text = $"{CategoryName(group.Key)}  ({group.Count()})",
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                Margin = new Thickness(2, 10, 0, 2),
            });
            foreach (var c in group)
                List.Children.Add(BuildRow(c));
        }
    }

    private Border BuildRow(SnapshotChange c)
    {
        var (glyph, brush, verb) = c.Kind switch
        {
            ChangeKind.Added => ("", (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"], "added"),
            ChangeKind.Removed => ("", (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"], "removed"),
            _ => ("", (Brush)Application.Current.Resources["SystemFillColorCautionBrush"], "changed"),
        };

        var text = new StackPanel { Spacing = 1 };
        text.Children.Add(new TextBlock
        {
            Text = c.Label,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = c.Kind switch
            {
                ChangeKind.Added => $"new, now {c.After}",
                ChangeKind.Removed => $"was {c.Before}, now gone",
                _ => $"{c.Before}  to  {c.After}",
            },
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        var icon = new FontIcon
        {
            Glyph = glyph, FontSize = 15, Foreground = brush,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(text, 1);
        var chip = new Border
        {
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = verb, FontSize = 11, Foreground = brush },
        };
        Grid.SetColumn(chip, 2);
        grid.Children.Add(icon);
        grid.Children.Add(text);
        grid.Children.Add(chip);

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(12, 9, 12, 9),
            Child = grid,
        };
    }

    private static string CategoryName(SnapshotCategory c) => c switch
    {
        SnapshotCategory.Startup => "Startup entries",
        SnapshotCategory.ScheduledTask => "Scheduled tasks",
        SnapshotCategory.Service => "Services",
        SnapshotCategory.Program => "Installed programs",
        SnapshotCategory.Driver => "Drivers",
        SnapshotCategory.Tweak => "PowerX tweaks",
        _ => c.ToString(),
    };

    private async void Snap_Click(object sender, RoutedEventArgs e)
    {
        if (Services.DemoData.Active) return;
        SnapButton.IsEnabled = false;
        SnapButton.Content = "Taking snapshot...";
        try
        {
            await Task.Run(() => SystemSnapshot.Save(SystemSnapshot.Capture()));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            App.Log("Changes.Snap", ex);
        }
        finally
        {
            SnapButton.IsEnabled = true;
            SnapButton.Content = "Take snapshot now";
        }
    }
}
