using System.Collections.Concurrent;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed partial class RepairPage : Page
{
    private const int MaxConsoleChars = 60_000;

    private CancellationTokenSource? _cts;
    private bool _running;
    private CancellationTokenSource? _memCts;

    // Console output is buffered and flushed on a timer so a chatty tool (driverquery,
    // DISM) can't flood the dispatcher or blow up the TextBox with per-line updates.
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly StringBuilder _console = new();
    private DispatcherQueueTimer? _flush;

    public RepairPage()
    {
        InitializeComponent();
        BuildJobs();
        MemSize.ValueChanged += (_, e) => MemSizeText.Text = $"{(int)e.NewValue} GB";
        long safe = MemoryTest.SafeMaxBytes() / 1024 / 1024 / 1024;
        MemSize.Maximum = Math.Clamp(safe, 1, 32);
        if (MemSize.Value > MemSize.Maximum) MemSize.Value = MemSize.Maximum;
    }

    private async void MemStart_Click(object sender, RoutedEventArgs e)
    {
        if (_memCts is not null) { _memCts.Cancel(); return; }

        _memCts = new CancellationTokenSource();
        MemStart.Content = "Stop";
        MemProgress.Visibility = Visibility.Visible;
        MemProgress.Value = 0;
        MemResult.Text = "Starting…";
        MemSize.IsEnabled = MemPasses.IsEnabled = false;

        long bytes = (long)MemSize.Value * 1024 * 1024 * 1024;
        int passes = MemPasses.SelectedIndex switch { 0 => 1, 2 => 4, 3 => 8, _ => 2 };

        var progress = new Progress<MemoryTestProgress>(p =>
        {
            MemProgress.Value = Math.Clamp(p.Percent, 0, 100);
            MemResult.Text = p.Pass == 0
                ? $"{p.Phase}… {p.Percent:0}%"
                : $"Pass {p.Pass}/{p.TotalPasses} · {p.Phase} · {p.Percent:0}% · {p.MegabytesPerSecond / 1024:0.0} GB/s";
        });

        var cts = _memCts;
        var result = await Task.Run(() => MemoryTest.Run(bytes, passes, progress, cts.Token));

        MemProgress.Visibility = Visibility.Collapsed;
        MemStart.Content = "Start";
        MemSize.IsEnabled = MemPasses.IsEnabled = true;
        _memCts = null;
        cts.Dispose();

        MemResult.Text = result.Passed
            ? $"✔ No errors. Tested {Fmt.Bytes((ulong)result.BytesTested)} over {result.Passes} pass{(result.Passes == 1 ? "" : "es")} in {result.Elapsed:mm\\:ss} at {result.AverageMBps / 1024:0.0} GB/s."
            : $"x {result.Errors.Count} error{Fmt.S(result.Errors.Count)} found. This RAM, or its overclock, is not stable. First at byte offset 0x{result.Errors[0].ByteOffset:X}.";
    }

    private void BuildJobs()
    {
        JobList.Children.Clear();
        foreach (var group in CommandRunner.Jobs.GroupBy(j => j.Category))
        {
            JobList.Children.Add(new TextBlock
            {
                Text = group.Key,
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                Margin = new Thickness(2, 12, 0, 2),
            });

            foreach (var job in group)
            {
                var run = new Button { Content = "Run", VerticalAlignment = VerticalAlignment.Center, MinWidth = 64 };
                run.Click += async (_, _) => await RunJob(job, run);

                var text = new StackPanel { Spacing = 2 };
                // A Grid, not a horizontal StackPanel: a StackPanel measures the title at infinite
                // width, so in a narrow window the title never wraps and the whole card (description
                // included) is laid out wider than its column and clipped.
                var titleRow = new Grid { ColumnSpacing = 8 };
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleRow.Children.Add(new TextBlock { Text = job.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                if (job.Destructive)
                {
                    var chip = Chip("changes system state", (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]);
                    chip.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(chip, 1);
                    titleRow.Children.Add(chip);
                }
                text.Children.Add(titleRow);
                text.Children.Add(new TextBlock { Text = job.Explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

                var grid = new Grid { ColumnSpacing = 10 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(text, 0); Grid.SetColumn(run, 1);
                grid.Children.Add(text); grid.Children.Add(run);

                JobList.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Padding = new Thickness(14, 10, 14, 10), Child = grid });
            }
        }
    }

    private async void Recommended_Click(object sender, RoutedEventArgs e)
    {
        // Matched by title, so a rename would otherwise throw straight out of an async void
        // handler and take the app down. RepairJobCatalogTests pins both titles.
        var dism = CommandRunner.Jobs.FirstOrDefault(j => j.Title.Contains("RestoreHealth", StringComparison.Ordinal));
        var sfc = CommandRunner.Jobs.FirstOrDefault(j => j.Title.StartsWith("Scan & repair", StringComparison.Ordinal));
        if (dism is null || sfc is null)
        {
            App.Log("Recommended", new InvalidOperationException("recommended repair jobs not found"));
            return;
        }
        await RunSequence("Recommended repair", [dism, sfc]);
    }

    private async Task RunJob(CommandRunner.Job job, Button? trigger)
    {
        if (_running) return;
        if (job.Destructive && !await Confirm(job.Title,
                $"{job.Explanation}\n\nThis changes system state. Continue?")) return;
        await RunSequence(job.Title, [job]);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _cts?.Cancel();
        _memCts?.Cancel();
        _flush?.Stop();
    }

    private void StartFlushing()
    {
        _flush ??= DispatcherQueue.CreateTimer();
        _flush.Interval = TimeSpan.FromMilliseconds(120);
        _flush.IsRepeating = true;
        _flush.Tick -= FlushTick;
        _flush.Tick += FlushTick;
        _flush.Start();
    }

    private void FlushTick(DispatcherQueueTimer s, object e)
    {
        if (_pending.IsEmpty) return;
        while (_pending.TryDequeue(out var line)) _console.Append(line).Append('\n');
        if (_console.Length > MaxConsoleChars)
            _console.Remove(0, _console.Length - MaxConsoleChars);

        OutputBox.Text = _console.ToString();
        OutputBox.Select(OutputBox.Text.Length, 0);
        ConsoleHint.Visibility = Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        while (_pending.TryDequeue(out _)) { }
        _console.Clear();
        OutputBox.Text = "";
        ConsoleHint.Visibility = Visibility.Visible;
    }

    private async Task RunSequence(string title, IReadOnlyList<CommandRunner.Job> jobs)
    {
        if (_running) return;
        _running = true;
        _cts = new CancellationTokenSource();
        SetBusy(true, title);
        StartFlushing();

        void Line(string s) => _pending.Enqueue(s);

        string? reportToOpen = null;
        try
        {
            foreach (var job in jobs)
            {
                Line($"══ {job.Title} ══");
                CommandRunner.JobRun run;
                try
                {
                    run = await CommandRunner.RunAsync(job, Line, _cts.Token);
                }
                catch (Exception ex) { Line($"error: {ex.Message}"); run = new CommandRunner.JobRun(-1, null); }

                Line(run.ExitCode == 0 ? "✔ completed\n" : $"✖ exit code {run.ExitCode}\n");
                if (run.ExitCode == 0 && run.ReportPath is not null && System.IO.File.Exists(run.ReportPath))
                    reportToOpen = run.ReportPath;
                if (_cts.IsCancellationRequested) break;
            }
        }
        catch (Exception ex)
        {
            App.Log("RepairRun", ex);
            Line($"\nunexpected error: {ex.Message}");
        }
        finally
        {
            FlushTick(null!, null!);   // final flush
            _flush?.Stop();
            SetBusy(false, title);
            _running = false;
            _cts?.Dispose();
            _cts = null;
        }

        if (reportToOpen is not null)
        {
            // Through the shell with the user's normal rights: Process.Start from here would open
            // the report's viewer (a browser, for the HTML reports) as administrator.
            var opened = PowerX.Core.Processes.ShellLauncher.Open(reportToOpen);
            if (!opened.Success)
            {
                App.Log("OpenReport", new InvalidOperationException(opened.Message));
                _pending.Enqueue($"Could not open the report ({opened.Message}). It is at {reportToOpen}");
                FlushTick(null!, null!);
            }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void SetBusy(bool busy, string title)
    {
        RunSpinner.IsActive = busy;
        StopButton.IsEnabled = busy;
        RecommendedButton.IsEnabled = !busy;
        ConsoleTitle.Text = busy ? $"Running: {title}" : title;
        foreach (var b in FindButtons(JobList)) b.IsEnabled = !busy;
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (c is Button b) yield return b;
            foreach (var d in FindButtons(c)) yield return d;
        }
    }

    private async Task<bool> Confirm(string title, string body) => await new ContentDialog
    {
        Title = title, Content = body,
        PrimaryButtonText = "Run", CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
    }.ShowAsync() == ContentDialogResult.Primary;

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
