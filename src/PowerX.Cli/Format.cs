namespace PowerX.Cli;

internal static class Format
{
    public static string Bytes(ulong b)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = b;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.#} {units[u]}";
    }

    public static string Bytes(double b) => Bytes((ulong)Math.Max(0, b));

    public static string Rate(double bytesPerSec) => bytesPerSec < 1 ? "-" : $"{Bytes(bytesPerSec)}/s";

    public static string Percent(double p) => $"{p,5:0.0}%";

    public static string Heat(double p)
    {
        // colour-plus-value: never colour alone (docs/DESIGN_SYSTEM.md §heat maps)
        var colour = p switch { >= 80 => "red", >= 50 => "orange1", >= 20 => "yellow", _ => "grey" };
        return $"[{colour}]{p,5:0.0}%[/]";
    }

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h" :
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" :
        $"{t.Minutes}m {t.Seconds}s";
}
