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

    public StoragePage()
    {
        InitializeComponent();
        var roots = DemoData.Active
            ? new[] { @"C:\", @"C:\Users\user" }
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
        _current = root;
        PathText.Text = root;
        List.Children.Clear();
        Summary.Text = $"Ready. Press Scan to size the folders under {root}.";
        UpButton.IsEnabled = false;
    }

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _current is null) return;
        var parent = System.IO.Directory.GetParent(_current.TrimEnd('\\'));
        if (parent is null) return;
        _current = parent.FullName.EndsWith('\\') ? parent.FullName : parent.FullName + "\\";
        _ = ScanAsync();
    }

    private void Scan_Click(object sender, RoutedEventArgs e) => _ = ScanAsync();
    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private async Task ScanAsync()
    {
        if (_busy || _current is null) return;
        _busy = true;
        _cts = new CancellationTokenSource();
        var cts = _cts;

        PathText.Text = _current;
        List.Children.Clear();
        Progress.Visibility = Visibility.Visible;
        ScanButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        UpButton.IsEnabled = System.IO.Directory.GetParent(_current.TrimEnd('\\')) is not null;
        Summary.Text = "Scanning...";

        IReadOnlyList<FolderEntry> entries = [];
        try
        {
            if (DemoData.Active)
            {
                await Task.Delay(300, cts.Token);
                entries = DemoData.FolderEntries(_current);
            }
            else
            {
                var progress = new Progress<string>(name => Summary.Text = $"Scanning... {name}");
                string target = _current;
                entries = await Task.Run(() => FolderSizer.ScanAsync(target, progress, cts.Token), cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            Summary.Text = "Scan stopped.";
        }
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
            _cts = null;
            _busy = false;
        }

        if (entries.Count > 0) Render(entries);
        else if (!cts.IsCancellationRequested) Summary.Text = $"{_current} has no readable sub-folders or files.";
    }

    private void Render(IReadOnlyList<FolderEntry> entries)
    {
        long total = entries.Sum(x => x.SizeBytes);
        long max = entries.Count > 0 ? entries.Max(x => x.SizeBytes) : 1;
        Summary.Text = $"{Fmt.Bytes((ulong)total)} across {entries.Count} item(s). Largest first. Click a folder to go deeper.";

        var accent = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0x8B, 0xF5));
        var fileFill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x8A, 0x93, 0xA6));
        var dim = (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"];

        foreach (var entry in entries.Take(300))
        {
            double frac = Math.Clamp((double)entry.SizeBytes / Math.Max(1, max), 0.004, 1);
            var bar = new Grid { Height = 7, CornerRadius = new CornerRadius(3.5), Background = dim, Margin = new Thickness(0, 5, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - frac, GridUnitType.Star) });
            var fill = new Grid
            {
                Background = entry.IsDirectory ? accent : fileFill,
                CornerRadius = new CornerRadius(3.5),
            };
            bar.Children.Add(fill);

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
            if (entry.IsDirectory && entry.FileCount > 0)
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
                Text = Fmt.Bytes((ulong)entry.SizeBytes),
                Style = (Style)Application.Current.Resources["MonoStyle"],
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
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
        if (_busy || sender is not Button { Tag: FolderEntry { IsDirectory: true } entry }) return;
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
