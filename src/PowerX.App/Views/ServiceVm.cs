using System.ComponentModel;
using System.ServiceProcess;
using Microsoft.UI.Xaml;
using PowerX.Core.Services;

namespace PowerX.App.Views;

public sealed class ServiceVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required ServiceEntry Entry { get; set; }

    public string DisplayName => Entry.DisplayName;
    public string Name => Entry.Name;
    public string Description => string.IsNullOrWhiteSpace(Entry.Description)
        ? Entry.ImagePath : Entry.Description;
    public string StatusText => Entry.StatusText;
    public string StartModeText => Entry.StartModeText;

    /// <summary>The metadata line under the description. Plenty of services have a short name
    /// identical to their display name, and repeating it directly under the heading reads like a
    /// rendering fault, so it is only shown when it actually adds something.</summary>
    public string NameAndStartMode =>
        string.Equals(Entry.Name, Entry.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? $"start: {Entry.StartModeText}"
            : $"{Entry.Name}  ·  start: {Entry.StartModeText}";
    public bool IsRunning => Entry.Status == ServiceControllerStatus.Running;
    public bool IsCritical => Entry.IsCritical;
    public Visibility CriticalVisibility => Entry.IsCritical ? Visibility.Visible : Visibility.Collapsed;
    public string StartStopLabel => IsRunning ? "Stop" : "Start";

    public bool Busy { get; private set; }

    public void Refresh(ServiceEntry fresh)
    {
        Entry = fresh;
        RaiseAll();
    }

    public void SetBusy(bool b)
    {
        Busy = b;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Busy)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowEnabled)));
    }

    public bool RowEnabled => !Busy;

    private void RaiseAll()
    {
        foreach (var p in new[] { nameof(StatusText), nameof(StartModeText), nameof(NameAndStartMode), nameof(IsRunning), nameof(StartStopLabel), nameof(Description) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
