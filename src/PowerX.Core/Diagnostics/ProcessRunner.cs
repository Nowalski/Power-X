using System.Diagnostics;
using System.Text;

namespace PowerX.Core.Diagnostics;

/// <summary>
/// Runs a short-lived console tool and captures its combined output — correctly:
/// the pipes are drained asynchronously (no deadlock when the child fills stderr while
/// we read stdout), there is a hard timeout, the child process tree is killed on timeout,
/// and <see cref="Process.ExitCode"/> is never read before the process has exited.
/// For long-running, line-streamed jobs use <see cref="CommandRunner"/> instead.
/// </summary>
public static class ProcessRunner
{
    public readonly record struct Result(bool Exited, int ExitCode, string Output)
    {
        /// <summary>The process ran to completion and returned 0.</summary>
        public bool Ok => Exited && ExitCode == 0;
    }

    public static Result Run(string file, string arguments, int timeoutMs = 15_000)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        var sb = new StringBuilder();
        void Sink(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (sb) sb.AppendLine(e.Data);
        }
        p.OutputDataReceived += Sink;
        p.ErrorDataReceived += Sink;

        try
        {
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch (Exception) { /* already exiting */ }
                string sofar = Text(sb);
                return new Result(false, -1,
                    (sofar.Length > 0 ? sofar + "\n" : "") + $"(timed out after {timeoutMs / 1000}s)");
            }

            p.WaitForExit();   // parameterless: lets the async output handlers finish flushing
            return new Result(true, p.ExitCode, Text(sb));
        }
        catch (Exception ex)
        {
            return new Result(false, -1, ex.Message);
        }

        static string Text(StringBuilder b) { lock (b) return b.ToString().Trim(); }
    }
}
