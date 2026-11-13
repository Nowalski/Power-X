using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Processes;
using FontWeight = Windows.UI.Text.FontWeight;

namespace PowerX.App.Views;

/// <summary>
/// One visible position ("slot") in the process list, not one process. Each refresh writes the
/// process that now sorts into this position into it, so the list's items never move, appear or
/// disappear unless the number of processes changes; only the values in a row change, as in Task
/// Manager. That is also why <see cref="Pid"/> is settable and change-notifying: code that acts on
/// a process must copy the PID it means at the moment the user picked it, not hold on to the slot.
/// Hand-rolled INPC to keep it WinRT-marshalling friendly (no toolkit source-gen).
/// </summary>
public sealed class ProcessRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _pid;
    private string _name = "";
    private double _cpu;
    private string _cpuText = "";
    private string _workingSet = "";
    private string _privateBytes = "";
    private string _io = "";
    private int _threads;
    private Brush? _cpuHeat;
    private Brush? _memHeat;
    private Brush? _cpuBar;
    private FontWeight _cpuWeight = FontWeights.Normal;
    private FontWeight _memWeight = FontWeights.Normal;

    // The raw figures the texts above were made from. A slot usually holds the same process with
    // the same memory and I/O as last time, so the texts are only formatted again when these move.
    private double _cpuRaw;
    private ulong _privateRaw;
    private ulong _totalRam;
    private bool _formatted;

    public int Pid { get => _pid; private set => Set(ref _pid, value); }
    public string Name { get => _name; private set => Set(ref _name, value); }
    public double Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }
    public string WorkingSet { get => _workingSet; private set => Set(ref _workingSet, value); }
    public string PrivateBytes { get => _privateBytes; private set => Set(ref _privateBytes, value); }
    public string Io { get => _io; private set => Set(ref _io, value); }
    public int Threads { get => _threads; private set => Set(ref _threads, value); }
    public Brush? CpuHeat { get => _cpuHeat; private set => Set(ref _cpuHeat, value); }
    public Brush? MemHeat { get => _memHeat; private set => Set(ref _memHeat, value); }
    public Brush? CpuBar { get => _cpuBar; private set => Set(ref _cpuBar, value); }
    public FontWeight CpuWeight { get => _cpuWeight; private set => Set(ref _cpuWeight, value); }
    public FontWeight MemWeight { get => _memWeight; private set => Set(ref _memWeight, value); }

    public ulong WorkingSetBytes { get; private set; }
    public double IoBytesPerSec { get; private set; }

    /// <summary>1 while this slot holds the process the user selected, else 0. The page keeps the
    /// selection by PID and sets this on whichever slot holds it (see ProcessesPage).</summary>
    private double _selectionOpacity;
    public double SelectionOpacity { get => _selectionOpacity; set => Set(ref _selectionOpacity, value); }

    /// <summary>Show <paramref name="p"/> in this slot. Cheap when nothing changed: no strings are
    /// formatted and no brushes are made (Design hands out cached ones), and properties whose value
    /// is unchanged raise no change notification.</summary>
    public void Update(ProcessInfo p, ulong totalRam)
    {
        Pid = p.Pid;
        Name = p.Name;

        bool ramChanged = totalRam != _totalRam;
        _totalRam = totalRam;

        if (!_formatted || p.CpuPercent != _cpuRaw)
        {
            _cpuRaw = p.CpuPercent;
            Cpu = Math.Round(p.CpuPercent, 1);
            CpuText = p.CpuPercent < 0.05 ? "—" : $"{p.CpuPercent:0.0}%";
            CpuHeat = Design.CpuHeat(p.CpuPercent);
            CpuWeight = p.CpuPercent >= 20 ? FontWeights.SemiBold : FontWeights.Normal;
        }

        bool memChanged = !_formatted || ramChanged || p.WorkingSetBytes != WorkingSetBytes;
        if (memChanged)
        {
            WorkingSetBytes = p.WorkingSetBytes;
            WorkingSet = Fmt.Bytes(p.WorkingSetBytes);

            double memScaled = MemPercent(p.WorkingSetBytes, totalRam) * 3.5; // working sets are small vs total RAM; exaggerate for the wash
            MemHeat = Design.MemHeat(memScaled);
            MemWeight = memScaled >= 40 ? FontWeights.SemiBold : FontWeights.Normal;
        }

        if (!_formatted || p.PrivateBytes != _privateRaw)
        {
            _privateRaw = p.PrivateBytes;
            PrivateBytes = Fmt.Bytes(p.PrivateBytes);
        }

        if (!_formatted || p.IoBytesPerSec != IoBytesPerSec)
        {
            IoBytesPerSec = p.IoBytesPerSec;
            Io = Fmt.Rate(p.IoBytesPerSec);
        }

        Threads = p.ThreadCount;

        // The left bar tracks the greater of CPU load and a scaled memory share, so a process
        // that's hogging either resource stands out while scrolling.
        CpuBar = Design.CpuBar(Math.Max(p.CpuPercent, MemPercent(p.WorkingSetBytes, totalRam) * 2.2));
        _formatted = true;
    }

    private static double MemPercent(ulong workingSet, ulong totalRam) =>
        totalRam == 0 ? 0 : 100.0 * workingSet / totalRam;

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
