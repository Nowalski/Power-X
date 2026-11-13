using System.Text;
using Microsoft.UI.Dispatching;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Services;

/// <summary>
/// The component store (WinSxS) work started from the Tools page, kept for the life of the app.
/// <para>
/// ToolsPage is a new page on every visit. With the analysis inside the page, the minutes DISM
/// takes were lost the moment you looked at another page: the result never arrived anywhere, DISM
/// kept running with nobody waiting for it, and coming back showed "Not analyzed yet" with
/// Analyze to press again. This owns the running analysis or cleanup, DISM's percentage, when it
/// started, and the last result with its time; the page only shows that state and re-attaches to
/// it when it comes back.
/// </para>
/// One DISM run at a time: an analysis and a cleanup never overlap. Everything here is used on
/// the UI thread, and <see cref="Changed"/> is raised there.
/// </summary>
internal static class ComponentStoreSession
{
    private const int MaxOutputChars = 20_000;

    /// <summary>Raised on the UI thread whenever anything below changes. A page that subscribes
    /// must unsubscribe when it is navigated away from, or this keeps it alive.</summary>
    public static event EventHandler? Changed;

    public static bool Analyzing { get; private set; }

    /// <summary>DISM's own percentage for the running analysis; null until it draws its first
    /// progress bar.</summary>
    public static double? Percent { get; private set; }

    public static DateTimeOffset AnalysisStartedAt { get; private set; }
    public static ComponentStoreInfo? LastResult { get; private set; }
    public static DateTimeOffset LastResultAt { get; private set; }

    /// <summary>The last analysis was stopped by the user; <see cref="LastResult"/>, if any, is
    /// from the one before it.</summary>
    public static bool AnalysisStopped { get; private set; }

    public static bool Cleaning { get; private set; }
    public static double? CleanupPercent { get; private set; }
    public static DateTimeOffset CleanupStartedAt { get; private set; }

    /// <summary>A cleanup ran after <see cref="LastResult"/> was measured, so its figures (and
    /// whether a cleanup is recommended) are out of date until the next analysis.</summary>
    public static bool CleanedSinceAnalysis { get; private set; }

    private static readonly StringBuilder CleanupLog = new();
    public static string CleanupOutput => CleanupLog.ToString();
    public static bool HasCleanupOutput => CleanupLog.Length > 0;

    private static CancellationTokenSource? _analysisCts;
    private static CancellationTokenSource? _cleanupCts;

    public static bool Busy => Analyzing || Cleaning;

    /// <summary>Start an analysis unless DISM is already running for one of these.</summary>
    public static async void Analyze()
    {
        if (Busy) return;
        Analyzing = true;
        AnalysisStopped = false;
        Percent = null;
        AnalysisStartedAt = DateTimeOffset.Now;
        var cts = _analysisCts = new CancellationTokenSource();
        Raise();

        try
        {
            var progress = new Progress<double>(p =>
            {
                // A report can be delivered after the run ended; it no longer means anything then.
                if (!Analyzing) return;
                Percent = p;
                Raise();
            });
            var info = DemoData.Active
                ? DemoData.ComponentStore()
                : await ComponentStore.AnalyzeAsync(progress, cts.Token);
            Finish(info);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Stopped by the user (DISM is ended). Keep whatever result there was before.
            AnalysisStopped = true;
        }
        catch (Exception ex)
        {
            App.Log("Tools.WinSxs", ex);
            Finish(new ComponentStoreInfo { Error = ex.Message });
        }
        finally
        {
            cts.Dispose();
            _analysisCts = null;
            Analyzing = false;
            Raise();
        }

        static void Finish(ComponentStoreInfo info)
        {
            LastResult = info;
            LastResultAt = DateTimeOffset.Now;
            CleanedSinceAnalysis = false;
        }
    }

    /// <summary>Stop whichever DISM run is going (an analysis or a cleanup).</summary>
    public static void Stop()
    {
        _analysisCts?.Cancel();
        _cleanupCts?.Cancel();
    }

    /// <summary>Start DISM /StartComponentCleanup unless DISM is already running for one of these.</summary>
    public static async void StartCleanup()
    {
        if (Busy) return;
        Cleaning = true;
        CleanupPercent = null;
        CleanupStartedAt = DateTimeOffset.Now;
        CleanupLog.Clear();
        var cts = _cleanupCts = new CancellationTokenSource();
        Raise();

        var ui = DispatcherQueue.GetForCurrentThread();
        void Line(string s)
        {
            // DISM's lines arrive on a reader thread; the log is only touched on the UI thread.
            if (ui.HasThreadAccess) Append(s);
            else ui.TryEnqueue(() => Append(s));
        }

        try
        {
            var progress = new Progress<double>(p =>
            {
                if (!Cleaning) return;
                CleanupPercent = p;
                Raise();
            });
            if (DemoData.Active) Line("Component cleanup finished. (demo)");
            else await ComponentStore.StartCleanupAsync(Line, progress, cts.Token);
        }
        catch (Exception ex)
        {
            App.Log("Tools.WinSxsClean", ex);
            Line("error: " + ex.Message);
        }
        finally
        {
            cts.Dispose();
            _cleanupCts = null;
            CleanedSinceAnalysis = true;
            Cleaning = false;
            Raise();
        }
    }

    private static void Append(string line)
    {
        CleanupLog.Append(line).Append('\n');
        if (CleanupLog.Length > MaxOutputChars) CleanupLog.Remove(0, CleanupLog.Length - MaxOutputChars);
        Raise();
    }

    private static void Raise()
    {
        // A page's handler failing must not take down the run (these are async void).
        try { Changed?.Invoke(null, EventArgs.Empty); }
        catch (Exception ex) { App.Log("ComponentStoreSession", ex); }
    }
}
