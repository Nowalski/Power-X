namespace PowerX.App;

internal static class Fmt
{
    public static string Bytes(ulong b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }

    public static string Bytes(double b) => Bytes((ulong)Math.Max(0, b));

    public static string Rate(double bytesPerSec) => bytesPerSec < 1 ? "0/s" : $"{Bytes(bytesPerSec)}/s";

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        $"{t.Minutes}m {t.Seconds}s";
}
