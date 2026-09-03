using System.ComponentModel;

namespace PowerX.App.Views;

/// <summary>One labelled bar (a logical processor or a GPU engine). Fixed set, updated in place.</summary>
public sealed class CoreVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _label;
    private double _usage;

    public CoreVm(int index) => _label = $"CPU {index}";

    public string Label
    {
        get => _label;
        private set { if (_label == value) return; _label = value; Raise(nameof(Label)); }
    }

    public void SetLabel(string label) => Label = label;

    public double Usage
    {
        get => _usage;
        set
        {
            if (Math.Abs(_usage - value) < 0.05) return;
            _usage = value;
            Raise(nameof(Usage));
            Raise(nameof(Text));
        }
    }

    public string Text => $"{_usage:0}%";

    private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
