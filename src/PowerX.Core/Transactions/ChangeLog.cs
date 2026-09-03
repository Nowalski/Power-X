using System.Text.Json;

namespace PowerX.Core.Transactions;

/// <summary>
/// Append-only JSON-lines change history at
/// <c>%LOCALAPPDATA%\PowerX\change-history.jsonl</c>. Powers the Change History timeline
/// and "undo compatible changes".
/// </summary>
public sealed class ChangeLog
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private readonly string _path;
    private readonly Lock _gate = new();

    public ChangeLog(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerX", "change-history.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public void Append(ChangeRecord record)
    {
        lock (_gate)
        {
            File.AppendAllText(_path, JsonSerializer.Serialize(record, Json) + Environment.NewLine);
            RotateIfLarge();
        }
    }

    // Keep the history file bounded. Once it passes ~2 MB, trim to the most recent ~2000 entries
    // (~0.5 MB) so it stays well under the trigger and doesn't rotate on every subsequent append.
    // Write the trimmed copy to a temp file and swap it in, so a crash mid-rotate can't lose the log.
    private void RotateIfLarge()
    {
        try
        {
            var info = new FileInfo(_path);
            if (!info.Exists || info.Length < 2_097_152) return;
            var keep = File.ReadLines(_path).TakeLast(2000).ToArray();
            string tmp = _path + ".tmp";
            File.WriteAllLines(tmp, keep);
            File.Replace(tmp, _path, null, ignoreMetadataErrors: true);
        }
        catch (IOException) { /* another writer has it — try again next append */ }
    }

    public IReadOnlyList<ChangeRecord> ReadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return [];
            var result = new List<ChangeRecord>();
            foreach (var line in File.ReadLines(_path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<ChangeRecord>(line, Json) is { } r) result.Add(r);
                }
                catch (JsonException) { /* skip corrupt line, keep history readable */ }
            }
            return result;
        }
    }

    /// <summary>
    /// Per tweak, the most recent successful change — kept only when it was an Apply that
    /// actually left the tweak in the Applied state. A later successful Revert, a failed
    /// operation, or an Apply that ended up Custom/Default therefore does not count.
    /// </summary>
    public IReadOnlyList<ChangeRecord> RevertableChanges()
    {
        var latest = new Dictionary<string, ChangeRecord>();
        foreach (var r in ReadAll().Where(r => r.Success))
        {
            latest[r.TweakId] = r;
        }
        return latest.Values
            .Where(r => r.Action == ChangeAction.Apply
                     && r.ResultingState == nameof(Tweaks.TweakState.Applied))
            .ToList();
    }
}
