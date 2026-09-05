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

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        $"{t.Minutes}m {t.Seconds}s";
}
