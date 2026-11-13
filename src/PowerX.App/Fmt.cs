namespace PowerX.App;

internal static class Fmt
{
    public static string Bytes(ulong b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        // One decimal from MB up, none below. "0.#" used to drop the decimal whenever the value
        // happened to be whole, so a right-aligned column read "377.3 GB / 5.2 GB / 3 GB / 1.1 GB"
        // with one row visibly out of step. Bytes and kilobytes keep no decimal: a tenth of a
        // kilobyte is noise, not information.
        return i <= 1 ? $"{v:0} {u[i]}" : $"{v:0.0} {u[i]}";
    }

    public static string Bytes(double b) => Bytes((ulong)Math.Max(0, b));

    /// <summary>Plural suffix for a count, so messages read "1 rule" / "3 rules" rather than
    /// "rule(s)". Only regular plurals; irregular ones are written out at the call site.</summary>
    public static string S(int n) => n == 1 ? "" : "s";

    public static string Rate(double bytesPerSec) => bytesPerSec < 1 ? "0/s" : $"{Bytes(bytesPerSec)}/s";

    /// <summary>How long ago something happened, for a result shown again later: "just now",
    /// "5 minutes ago", "2 hours ago", "3 days ago".</summary>
    public static string Ago(TimeSpan t)
    {
        int n;
        if (t.TotalMinutes < 1) return "just now";
        if (t.TotalHours < 1) { n = (int)t.TotalMinutes; return $"{n} minute{S(n)} ago"; }
        if (t.TotalDays < 1) { n = (int)t.TotalHours; return $"{n} hour{S(n)} ago"; }
        n = (int)t.TotalDays;
        return $"{n} day{S(n)} ago";
    }

    /// <summary>Elapsed time as a clock, "0:42" or "12:05", for something still running.</summary>
    public static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        $"{t.Minutes}m {t.Seconds}s";
}
