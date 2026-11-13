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
        PageLayout.CapListPage(this);
        BuildJobs();
        MemSize.ValueChanged += (_, e) => MemSizeText.Text = $"{(int)e.NewValue} GB";
        long safe = MemoryTest.SafeMaxBytes() / 1024 / 1024 / 1024;
        MemSize.Maximum = Math.Clamp(safe, 1, 32);
        if (MemSize.Value > MemSize.Maximum) MemSize.Value = MemSize.Maximum;
    }

    private async void MemStart_Click(object sender, RoutedEventArgs e)
    {
        if (_memCts is not null) { _memCts.Cancel(); return; }

        // Nothing here changes a size: the progress bar only fades in and out (Opacity), and the
        // status line has a fixed height and trims, with the full text in its tooltip.
        _memCts = new CancellationTokenSource();
        MemStart.Content = "Stop";
        MemProgress.Value = 0;
        MemProgress.Opacity = 1;
        MemStatus("Starting…");
        MemSize.IsEnabled = MemPasses.IsEnabled = false;

        long bytes = (long)MemSize.Value * 1024 * 1024 * 1024;
        int passes = MemPasses.SelectedIndex switch { 0 => 1, 2 => 4, 3 => 8, _ => 2 };

        var progress = new Progress<MemoryTestProgress>(p =>
        {
            MemProgress.Value = Math.Clamp(p.Percent, 0, 100);
            MemStatus(p.Pass == 0
                ? $"{p.Phase}… {p.Percent:0}%"
                : $"Pass {p.Pass}/{p.TotalPasses} · {p.Phase} · {p.Percent:0}% · {p.MegabytesPerSecond / 1024:0.0} GB/s");
        });

        var cts = _memCts;
        var result = await Task.Run(() => MemoryTest.Run(bytes, passes, progress, cts.Token));

        MemProgress.Opacity = 0;
        MemStart.Content = "Start";
        MemSize.IsEnabled = MemPasses.IsEnabled = true;
        _memCts = null;
        cts.Dispose();

        MemStatus(result.Passed
            ? $"✔ No errors. Tested {Fmt.Bytes((ulong)result.BytesTested)} over {result.Passes} pass{(result.Passes == 1 ? "" : "es")} in {result.Elapsed:mm\\:ss} at {result.AverageMBps / 1024:0.0} GB/s."
            : $"x {result.Errors.Count} error{Fmt.S(result.Errors.Count)} found. This RAM, or its overclock, is not stable. First at byte offset 0x{result.Errors[0].ByteOffset:X}.");
    }

    private void MemStatus(string text)
    {
        MemResult.Text = text;
        // The line trims in a narrow window; the whole text is still there on hover.
        ToolTipService.SetToolTip(MemResult, text);
    }

    /// <summary>The Run button of every job, to switch them all off while one runs.</summary>
    private readonly List<Button> _runButtons = [];
    private Button? _activeRun;

    private static string CategoryGlyph(string category) => category switch
    {
        "Integrity" => "\uE90F",       // wrench
        "Disk" => "\uEDA2",            // hard drive
        "Network" => "\uE968",         // network
        "Windows Update" => "\uE895",  // sync
        "Reports" => "\uE9F9",         // report
        _ => "\uE9D2",                 // pulse
    };

    /// <summary>
    /// One card per category, with the category's icon and name as its header and the jobs as
    /// rows split by the shared row divider (the Settings layout). Each job was its own card
    /// before, which made a long, busy column of near-identical boxes.
    /// </summary>
    private void BuildJobs()
    {
        JobList.Children.Clear();
        _runButtons.Clear();
        var res = Application.Current.Resources;
        foreach (var group in CommandRunner.Jobs.GroupBy(j => j.Category))
        {
            var body = new StackPanel { Spacing = 12 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            header.Children.Add(new FontIcon
            {
                Glyph = CategoryGlyph(group.Key), FontSize = 16, VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)res["AccentTextFillColorPrimaryBrush"],
            });
            header.Children.Add(new TextBlock
            {
                Text = group.Key, Style = (Style)res["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(new TextBlock
            {
                Text = $"{group.Count()} tool{Fmt.S(group.Count())}", Style = (Style)res["MutedStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            body.Children.Add(header);
            body.Children.Add(new Border { Style = (Style)res["CardDividerStyle"] });

            bool first = true;
            foreach (var job in group)
            {
                if (!first) body.Children.Add(new Border { Style = (Style)res["RowDividerStyle"] });
                first = false;

                var run = new Button { Content = "Run", VerticalAlignment = VerticalAlignment.Center, MinWidth = 72 };
                run.Click += async (_, _) => await RunJob(job, run);
                _runButtons.Add(run);

                var text = new StackPanel { Spacing = 2 };
                // A Grid, not a horizontal StackPanel: a StackPanel measures the title at infinite
                // width, so in a narrow window the title never wraps and the whole row (description
                // included) is laid out wider than its column and clipped.
                var titleRow = new Grid { ColumnSpacing = 8 };
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                titleRow.Children.Add(new TextBlock { Text = job.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                if (job.Destructive)
                {
                    var chip = Chip("changes system state", (Brush)res["SystemFillColorCautionBrush"]);
                    chip.VerticalAlignment = VerticalAlignment.Top;
                    Grid.SetColumn(chip, 1);
                    titleRow.Children.Add(chip);
                }
                text.Children.Add(titleRow);
                text.Children.Add(new TextBlock { Text = job.Explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)res["TextFillColorSecondaryBrush"] });

                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(text, 0); Grid.SetColumn(run, 1);
                row.Children.Add(text); row.Children.Add(run);
                body.Children.Add(row);
            }

            JobList.Children.Add(new Border { Style = (Style)res["CardStyle"], Child = body });
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
        _activeRun = trigger;
        await RunSequence(job.Title, [job]);
        _activeRun = null;
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
        bool allOk = true;
        var started = DateTime.Now;
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
                if (run.ExitCode != 0) allOk = false;
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
            bool stopped = _cts?.IsCancellationRequested == true;
            ConsoleStatus.Text = (stopped ? "Stopped" : allOk ? "Finished" : "Finished with errors")
                + $" at {DateTime.Now:t}, took {Fmt.Duration(DateTime.Now - started)}";
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
        ConsoleTitle.Text = title;
        if (busy) ConsoleStatus.Text = $"Running since {DateTime.Now:t}";
        foreach (var b in _runButtons) b.IsEnabled = !busy;
        // The job that is running says so on its own row, so it can be found in the list.
        if (_activeRun is not null) _activeRun.Content = busy ? "Running…" : "Run";
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
