using System.ComponentModel;
using Microsoft.UI.Xaml;
using PowerX.App.Services;

namespace PowerX.App.Views;

/// <summary>
/// Shared, resizable column widths for the Processes table. One instance is bound by both the
/// header row and every list row (as a keyed resource), so a drag updates all of them at once.
/// Widths persist to <see cref="AppSettings"/>. The Name column is always the flexible remainder.
/// </summary>
public sealed class ProcessColumns : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // (key, min, max, default)
    private static readonly (string Key, double Min, double Max, double Default)[] Spec =
    [
        ("Pid", 48, 180, 66),
        ("Cpu", 56, 260, 92),
        ("Mem", 76, 340, 112),
        ("Io", 68, 340, 104),
        ("Thr", 52, 220, 74),
    ];

    private GridLength _pid, _cpu, _mem, _io, _thr;

    public ProcessColumns()
    {
        var saved = Parse(AppSettings.Current.ProcessColumnWidths);
        _pid = new GridLength(saved.TryGetValue("Pid", out var p) ? p : 66);
        _cpu = new GridLength(saved.TryGetValue("Cpu", out var c) ? c : 92);
        _mem = new GridLength(saved.TryGetValue("Mem", out var m) ? m : 112);
        _io = new GridLength(saved.TryGetValue("Io", out var i) ? i : 104);
        _thr = new GridLength(saved.TryGetValue("Thr", out var t) ? t : 74);
    }

    public GridLength Pid { get => _pid; set => Set(ref _pid, value, nameof(Pid)); }
    public GridLength Cpu { get => _cpu; set => Set(ref _cpu, value, nameof(Cpu)); }
    public GridLength Mem { get => _mem; set => Set(ref _mem, value, nameof(Mem)); }
    public GridLength Io { get => _io; set => Set(ref _io, value, nameof(Io)); }
    public GridLength Thr { get => _thr; set => Set(ref _thr, value, nameof(Thr)); }

    /// <summary>Current pixel width of one column.</summary>
    public double Get(string key) => key switch
    {
        "Pid" => _pid.Value, "Cpu" => _cpu.Value, "Mem" => _mem.Value,
        "Io" => _io.Value, "Thr" => _thr.Value, _ => 0,
    };

    public double Min(string key) => Array.Find(Spec, x => x.Key == key).Min;
    public double Max(string key) => Array.Find(Spec, x => x.Key == key).Max;

    /// <summary>Set one column to an absolute pixel width, clamped to its allowed range.</summary>
    public void SetPixels(string key, double value)
    {
        var s = Array.Find(Spec, x => x.Key == key);
        if (s.Key is null) return;
        var gl = new GridLength(Math.Round(Math.Clamp(value, s.Min, s.Max)));
        switch (key)
        {
            case "Pid": Pid = gl; break;
            case "Cpu": Cpu = gl; break;
            case "Mem": Mem = gl; break;
            case "Io": Io = gl; break;
            case "Thr": Thr = gl; break;
        }
    }

    public void Persist()
    {
        AppSettings.Current.ProcessColumnWidths =
            $"Pid={_pid.Value:0};Cpu={_cpu.Value:0};Mem={_mem.Value:0};Io={_io.Value:0};Thr={_thr.Value:0}";
        AppSettings.Current.Save();
    }

    public void Reset()
    {
        Pid = new GridLength(66);
        Cpu = new GridLength(92);
        Mem = new GridLength(112);
        Io = new GridLength(104);
        Thr = new GridLength(74);
        Persist();
    }

    private static Dictionary<string, double> Parse(string s)
    {
        var d = new Dictionary<string, double>();
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && double.TryParse(kv[1], out var v)) d[kv[0]] = v;
        }
        return d;
    }

    private void Set(ref GridLength field, GridLength value, string name)
    {
        if (field.Value == value.Value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
