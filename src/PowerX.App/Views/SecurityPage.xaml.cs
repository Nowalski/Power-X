using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed partial class SecurityPage : Page
{
    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _hashCts;
    private bool _onPage;

    public SecurityPage()
    {
        InitializeComponent();
        PageLayout.CenterCap(this, Root, 1000);
        _onPage = true;
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _onPage = false;
        _scanCts?.Cancel();
        _hashCts?.Cancel();
    }

    private async Task LoadAsync()
    {
        DefenderStatus status;
        IReadOnlyList<DefenderThreat> threats;
        try
        {
            (status, threats) = Services.DemoData.Active
                ? (Services.DemoData.DefenderStatus(), Services.DemoData.DefenderThreats())
                : await Task.Run(() => (Defender.Status(), Defender.ThreatHistory(60)));
        }
        catch (Exception ex)
        {
            App.Log("Defender.Load", ex);
            if (_onPage) Summary.Text = "Could not read Defender status: " + ex.Message;
            return;
        }
        if (!_onPage) return;

        Summary.Text = status.Detail
            ?? (status.Unprotected ? "No active real-time antivirus." : "Defender's own status and history. PowerX is not an antivirus.");
        UnprotectedBar.IsOpen = status.Unprotected;

        ModeText.Text = status.ModeText;
        Chips.Children.Clear();
        AddChip("Real-time", status.RealTimeProtection);
        AddChip("Cloud", status.CloudProtection);
        AddChip("Behavior monitor", status.BehaviorMonitor);
        AddChip("Tamper protection", status.TamperProtection);
        AddChip("Network protection", status.NetworkProtection);
        AddChip($"PUA: {status.PuaProtection}", status.PuaProtection == "on", neutral: status.PuaProtection is "audit");

        DefsText.Text = string.IsNullOrEmpty(status.SignatureVersion)
            ? "Definition version unknown."
            : $"Definitions {status.SignatureVersion}, {status.SignatureAgeDays} day{Fmt.S(status.SignatureAgeDays)} old"
              + (status.SignatureUpdated is { } su ? $" (updated {su.LocalDateTime:g})" : "")
              + (status.ExclusionCount > 0 ? $".   {status.ExclusionCount} exclusion{Fmt.S(status.ExclusionCount)} configured." : ".");

        ScansText.Text = (status.LastQuickScan is { } q ? $"Last quick scan {q.LocalDateTime:g}." : "No quick scan recorded.")
                       + (status.LastFullScan is { } f ? $"   Last full scan {f.LocalDateTime:g}." : "");

        RenderThreats(threats);
    }

    private void AddChip(string label, bool on, bool neutral = false)
    {
        var fg = neutral
            ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            : on ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                 : (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        Chips.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(9, 3, 9, 4),
            Child = new TextBlock { Text = (on || neutral ? "" : "off ") + label, FontSize = 11.5, Foreground = fg },
        });
    }

    private void RenderThreats(IReadOnlyList<DefenderThreat> threats)
    {
        ThreatList.Children.Clear();
        if (threats.Count == 0)
        {
            ThreatList.Children.Add(new TextBlock
            {
                Text = "Nothing recorded. Defender has not reported any detections on this machine.",
                Style = (Style)Application.Current.Resources["MutedStyle"],
            });
            return;
        }

        foreach (var t in threats)
        {
            var row = new StackPanel { Spacing = 2 };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new TextBlock
            {
                Text = t.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            head.Children.Add(Chip(t.Severity, t.Severity is "Severe" or "High"
                ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                : t.Severity == "Moderate" ? (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
                : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
            head.Children.Add(Chip(t.State.ToString().ToLowerInvariant(),
                t.State == DefenderThreatState.ActionFailed
                    ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
            if (t.Active)
                head.Children.Add(Chip("still active", (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]));
            row.Children.Add(head);

            row.Children.Add(new TextBlock
            {
                Text = t.When.LocalDateTime.ToString("ddd d MMM yyyy, HH:mm")
                     + (t.Resource is { } r ? $"   {r}" : ""),
                FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            });

            ThreatList.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["CardStyle"],
                Padding = new Thickness(12, 8, 12, 8),
                Child = row,
            });
        }
    }

    // ---------------------------------------------------------------- scan

    private void QuickScan_Click(object s, RoutedEventArgs e) => _ = RunScan(full: false);
    private void FullScan_Click(object s, RoutedEventArgs e) => _ = RunScan(full: true);
    private void StopScan_Click(object s, RoutedEventArgs e) => _scanCts?.Cancel();

    private async Task RunScan(bool full)
    {
        if (_scanCts is not null) return;
        _scanCts = new CancellationTokenSource();
        var cts = _scanCts;
        ScanOut.Visibility = Visibility.Visible;
        ScanOut.Text = "";
        QuickScanButton.IsEnabled = FullScanButton.IsEnabled = false;
        StopScanButton.IsEnabled = true;

        void Line(string line) => DispatcherQueue.TryEnqueue(() =>
        {
            ScanOut.Text += line + "\n";
            ScanOut.Select(ScanOut.Text.Length, 0);
        });

        try
        {
            await Defender.RunScanAsync(full, Line, cts.Token);
        }
        catch (Exception ex)
        {
            App.Log("Defender.Scan", ex);
            Line("error: " + ex.Message);
        }
        finally
        {
            cts.Dispose();
            _scanCts = null;
            if (_onPage)
            {
                QuickScanButton.IsEnabled = FullScanButton.IsEnabled = true;
                StopScanButton.IsEnabled = false;
                _ = LoadAsync();   // refresh "last scan" and threat history
            }
        }
    }

    // ---------------------------------------------------------------- hash check

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            nint hwnd = App.Window is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;
            string? path = Services.NativeFileDialog.PickFile(hwnd, "Choose a file to check");
            if (!string.IsNullOrEmpty(path)) PathBox.Text = path;
        }
        catch (Exception ex)
        {
            App.Log("Security.Browse", ex);
            Show(InfoBarSeverity.Warning, "Could not open the file picker", "Type or paste the file path instead.");
        }
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        string input = PathBox.Text.Trim().Trim('"');
        if (input.Length == 0) return;

        _hashCts?.Cancel();
        var cts = new CancellationTokenSource();
        _hashCts = cts;
        CheckButton.IsEnabled = false;
        HashText.Visibility = Visibility.Collapsed;
        HashResultBar.IsOpen = false;

        try
        {
            string sha256;
            if (input.Length == 64 && input.All(Uri.IsHexDigit))
            {
                sha256 = input.ToLowerInvariant();
            }
            else if (File.Exists(input))
            {
                HashText.Text = "Hashing the file…";
                HashText.Visibility = Visibility.Visible;
                sha256 = await HashLookup.Sha256FileAsync(input, cts.Token);
                HashText.Text = "SHA-256  " + sha256;
            }
            else
            {
                Show(InfoBarSeverity.Warning, "Not found", "That is not a file on disk or a SHA-256 hash.");
                return;
            }

            var r = await HashLookup.CheckAsync(sha256, cts.Token);
            if (cts.IsCancellationRequested) return;

            if (r.Error is { } err && !r.Found)
                Show(InfoBarSeverity.Warning, "Lookup problem", $"{r.Summary} ({err})");
            else if (r.KnownMalicious)
                Show(InfoBarSeverity.Error, "Known malicious", r.Summary);
            else if (r.Found && r.Trust is >= 50)
                Show(InfoBarSeverity.Success, "Known good file", r.Summary);
            else if (r.Found)
                Show(InfoBarSeverity.Informational, "Known file", r.Summary);
            else
                Show(InfoBarSeverity.Informational, "Not catalogued", r.Summary);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.Log("Security.HashCheck", ex);
            Show(InfoBarSeverity.Warning, "Could not check the file", ex.Message);
        }
        finally
        {
            CheckButton.IsEnabled = true;
            if (_hashCts == cts) _hashCts = null;
            cts.Dispose();
        }
    }

    private void Show(InfoBarSeverity severity, string title, string message)
    {
        HashResultBar.Severity = severity;
        HashResultBar.Title = title;
        HashResultBar.Message = message;
        HashResultBar.IsOpen = true;
    }

    // ---------------------------------------------------------------- helpers

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
