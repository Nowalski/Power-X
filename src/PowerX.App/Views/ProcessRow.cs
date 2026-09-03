using System.ComponentModel;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Processes;
using FontWeight = Windows.UI.Text.FontWeight;

namespace PowerX.App.Views;

/// <summary>
/// Observable process row — PID-keyed, updated in place so selection and scroll position stay put.
/// Hand-rolled INPC to keep it WinRT-marshalling friendly (no toolkit source-gen).
/// </summary>
public sealed class ProcessRow(int pid) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Pid { get; } = pid;

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

    public void Update(ProcessInfo p, ulong totalRam)
    {
        Name = p.Name;
        Cpu = Math.Round(p.CpuPercent, 1);
        CpuText = p.CpuPercent < 0.05 ? "—" : $"{p.CpuPercent:0.0}%";
        WorkingSetBytes = p.WorkingSetBytes;
        WorkingSet = Fmt.Bytes(p.WorkingSetBytes);
        PrivateBytes = Fmt.Bytes(p.PrivateBytes);
        IoBytesPerSec = p.IoBytesPerSec;
        Io = Fmt.Rate(p.IoBytesPerSec);
        Threads = p.ThreadCount;

        CpuHeat = Design.CpuHeat(p.CpuPercent);
        CpuWeight = p.CpuPercent >= 20 ? FontWeights.SemiBold : FontWeights.Normal;

        double memPct = totalRam == 0 ? 0 : 100.0 * p.WorkingSetBytes / totalRam;
        double memScaled = memPct * 3.5; // working sets are small vs total RAM; exaggerate for the wash
        MemHeat = Design.MemHeat(memScaled);
        MemWeight = memScaled >= 40 ? FontWeights.SemiBold : FontWeights.Normal;

        // The left bar tracks the greater of CPU load and a scaled memory share, so a process
        // that's hogging either resource stands out while scrolling.
        CpuBar = Design.CpuBar(Math.Max(p.CpuPercent, memPct * 2.2));
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
