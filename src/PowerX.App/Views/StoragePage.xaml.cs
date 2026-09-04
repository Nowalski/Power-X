using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.App.Services;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed partial class StoragePage : Page
{
    private string? _current;
    private CancellationTokenSource? _cts;
    private bool _busy;
    private bool _settingRoot;
    private bool _scannedOnce;

    private readonly Dictionary<string, FolderEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private int _dirsDone, _dirsTotal;
    private bool _renderQueued;

    public StoragePage()
    {
        InitializeComponent();
        var roots = DemoData.Active
            ? new[] { @"C:\Users\user", @"C:\" }
            : FolderSizer.Roots().ToArray();
        _settingRoot = true;
        foreach (var r in roots) RootBox.Items.Add(r);
        RootBox.SelectedIndex = 0;
        _settingRoot = false;
        _current = roots.FirstOrDefault();
        PathText.Text = _current ?? "";
        Loaded += (_, _) => { if (!_scannedOnce) { _scannedOnce = true; _ = ScanAsync(); } };
    }

    private void Root_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settingRoot || RootBox.SelectedItem is not string root) return;
        _cts?.Cancel();
        _current = root;
        _ = ScanAsync();
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var parent = System.IO.Directory.GetParent(_current.TrimEnd('\\'));
        if (parent is null) return;
        _cts?.Cancel();
        _current = parent.FullName.EndsWith('\\') ? parent.FullName : parent.FullName + "\\";
        _ = ScanAsync();
    }

    private void Scan_Click(object sender, RoutedEventArgs e) { _cts?.Cancel(); _ = ScanAsync(); }
    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task ScanAsync()
    {
        // Wait for any previous scan to unwind so its callbacks don't bleed into this one.
        while (_busy) await Task.Delay(30);
        if (_current is null) return;

        _busy = true;
        _cts = new CancellationTokenSource();
        var cts = _cts;
        string target = _current;

        PathText.Text = target;
        UpButton.IsEnabled = System.IO.Directory.GetParent(target.TrimEnd('\\')) is not null;
        Progress.Visibility = Visibility.Visible;
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _entries.Clear();
        _dirsDone = 0;

        // Draw the child folders straightaway as "measuring", so the page is never blank.
        var childDirs = DemoData.Active ? [] : FolderSizer.ChildDirectories(target);
        _dirsTotal = childDirs.Count;
        foreach (var d in childDirs)
            _entries[d] = new FolderEntry(d, System.IO.Path.GetFileName(d), true, -1, 0);
        RenderNow();
        Summary.Text = _dirsTotal == 0 ? "Measuring..." : $"Measuring {_dirsTotal} folder(s)...";

        bool cancelled = false;
        try
        {
            if (DemoData.Active)
            {
                await Task.Delay(250, cts.Token);
                foreach (var fe in DemoData.FolderEntries(target)) _entries[fe.Path] = fe;
                _dirsDone = _dirsTotal = _entries.Count(e => e.Value.IsDirectory);
                RenderNow();
            }
            else
            {
                var onEntry = new Progress<FolderEntry>(fe =>
                {
                    _entries[fe.Path] = fe;
                    ScheduleRender();
                });
                var onProgress = new Progress<(int Done, int Total)>(p =>
                {
                    _dirsDone = p.Done; _dirsTotal = p.Total;
                    Summary.Text = $"Measured {p.Done} of {p.Total} folder(s)...  {Fmt.Bytes((ulong)MeasuredTotal())} so far";
                });
                await FolderSizer.ScanAsync(target, onEntry, onProgress, cts.Token);
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex)
        {
            App.Log("Storage.Scan", ex);
            Summary.Text = "Could not scan this folder: " + ex.Message;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            ScanButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            cts.Dispose();
            if (_cts == cts) _cts = null;
            _busy = false;
        }

        RenderNow();
        long total = MeasuredTotal();
        int measured = _entries.Count(e => !e.Value.Pending);
        Summary.Text = cancelled
            ? $"Stopped. Measured {measured} of {_entries.Count} item(s), {Fmt.Bytes((ulong)total)}."
            : measured == 0
                ? $"{target} has nothing readable in it."
                : $"{Fmt.Bytes((ulong)total)} across {measured} item(s). Largest first. Click a folder to go deeper.";
    }

    private long MeasuredTotal() => _entries.Values.Where(e => !e.Pending).Sum(e => Math.Max(0, e.SizeBytes));

    private void ScheduleRender()
    {
        if (_renderQueued) return;
        _renderQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _renderQueued = false;
            RenderNow();
        });
    }

    private void RenderNow()
    {
        var ordered = _entries.Values
            .OrderBy(e => e.Pending)                       // measured first
            .ThenByDescending(e => e.SizeBytes)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(400)
            .ToList();

        long max = ordered.Where(e => !e.Pending).Select(e => e.SizeBytes).DefaultIfEmpty(1).Max();

        var accent = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0x8B, 0xF5));
        var fileFill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x8A, 0x93, 0xA6));
        var dim = (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"];

        List.Children.Clear();
        foreach (var entry in ordered)
        {
            double frac = entry.Pending ? 0 : Math.Clamp((double)entry.SizeBytes / Math.Max(1, max), 0.004, 1);
            var bar = new Grid { Height = 7, CornerRadius = new CornerRadius(3.5), Background = dim, Margin = new Thickness(0, 5, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
            bar.Children.Add(new Grid { Background = entry.IsDirectory ? accent : fileFill, CornerRadius = new CornerRadius(3.5) });

            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(new FontIcon
            {
                Glyph = entry.IsDirectory ? "\uE8B7" : "\uE7C3",
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            title.Children.Add(new TextBlock
            {
                Text = entry.Name,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (!entry.Pending && entry.IsDirectory && entry.FileCount > 0)
                title.Children.Add(new TextBlock
                {
                    Text = $"{entry.FileCount:N0} files",
                    FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                });

            var left = new StackPanel { Spacing = 0 };
            left.Children.Add(title);
            left.Children.Add(bar);

            var size = new TextBlock
            {
                Text = entry.Pending ? "measuring..." : Fmt.Bytes((ulong)entry.SizeBytes),
                Style = (Style)Application.Current.Resources["MonoStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources[entry.Pending ? "TextFillColorTertiaryBrush" : "TextFillColorSecondaryBrush"],
            };

            var open = new Button { Content = "Open", VerticalAlignment = VerticalAlignment.Center, Tag = entry.Path };
            open.Click += Open_Click;

            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(left, 0); Grid.SetColumn(size, 1); Grid.SetColumn(open, 2);
            grid.Children.Add(left); grid.Children.Add(size); grid.Children.Add(open);

            if (entry.IsDirectory)
            {
                var card = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 9, 12, 11),
                    Margin = new Thickness(0, 2, 0, 2),
                    Content = grid,
                    Tag = entry,
                };
                card.Click += Drill_Click;
                List.Children.Add(card);
            }
            else
            {
                List.Children.Add(new Border
                {
                    Style = (Style)Application.Current.Resources["CardStyle"],
                    Padding = new Thickness(12, 9, 12, 11),
                    Margin = new Thickness(0, 2, 0, 2),
                    Child = grid,
                });
            }
        }
    }

    private void Drill_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FolderEntry { IsDirectory: true } entry }) return;
        _cts?.Cancel();
        _current = entry.Path;
        _ = ScanAsync();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path }) return;
        try
        {
            var psi = System.IO.Directory.Exists(path)
                ? new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }
                : new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex) { App.Log("Storage.Open", ex); }
    }

    protected override void OnNavigatingFrom(Microsoft.UI.Xaml.Navigation.NavigatingCancelEventArgs e)
    {
        _cts?.Cancel();
        base.OnNavigatingFrom(e);
    }
}
