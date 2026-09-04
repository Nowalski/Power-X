using System.Text;

namespace PowerX.App.Services;

/// <summary>Writes a small CSV file from a header row and data rows, RFC-4180-ish quoting. Used by
/// the "Export" button on the list-heavy pages so a machine's data can leave PowerX as a file.</summary>
internal static class CsvExport
{
    public static void Write(string path, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Quote)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(',', row.Select(Quote)));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Quote(string s)
    {
        s ??= "";
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }
}
