using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

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
    private readonly string _mutexName;
    private readonly Lock _gate = new();

    public ChangeLog(string? path = null)
    {
        _path = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerX", "change-history.jsonl"));
        string pathHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_path.ToUpperInvariant())));
        _mutexName = $"Local\\PowerX.ChangeLog.{pathHash}";
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public void Append(ChangeRecord record)
    {
        lock (_gate)
        {
            WithCrossProcessLock(() =>
            {
                File.AppendAllText(_path, JsonSerializer.Serialize(record, Json) + Environment.NewLine);
                RotateIfLarge();
            });
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
            string tmp = _path + ".tmp";
            using var source = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            var keep = new Queue<string>(2000);
            while (reader.ReadLine() is { } line)
            {
                if (keep.Count == 2000) keep.Dequeue();
                keep.Enqueue(line);
            }
            File.WriteAllLines(tmp, keep);
            File.Replace(tmp, _path, null, ignoreMetadataErrors: true);
        }
        catch (IOException) { /* append succeeded; retry rotation on a later append */ }
        catch (UnauthorizedAccessException) { /* append succeeded; retry rotation on a later append */ }
    }

    public IReadOnlyList<ChangeRecord> ReadAll()
    {
        lock (_gate)
        {
            return WithCrossProcessLock(() =>
            {
                if (!File.Exists(_path)) return (IReadOnlyList<ChangeRecord>)[];
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
                return (IReadOnlyList<ChangeRecord>)result;
            });
        }
    }

    private void WithCrossProcessLock(Action action) => WithCrossProcessLock(() =>
    {
        action();
        return true;
    });

    private T WithCrossProcessLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(initiallyOwned: false, _mutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
        catch (AbandonedMutexException) { acquired = true; }

        if (!acquired)
            throw new IOException("Timed out waiting for the shared PowerX change history.");

        try { return action(); }
        finally { mutex.ReleaseMutex(); }
    }

    /// <summary>
    /// Per tweak, the most recent successful change — kept only when it was an Apply that
    /// actually left the tweak in the Applied state. A later successful Revert, a failed
    /// operation, or an Apply that ended up Custom/Default therefore does not count.
    /// </summary>
    public IReadOnlyList<ChangeRecord> RevertableChanges()
    {
        var latest = new Dictionary<string, ChangeRecord>();
        foreach (var r in ReadAll())
        {
            latest[r.TweakId] = r;
        }
        return latest.Values
            .Where(r => r.Success
                     && r.Action == ChangeAction.Apply
                     && r.ResultingState == nameof(Tweaks.TweakState.Applied))
            .ToList();
    }
}
