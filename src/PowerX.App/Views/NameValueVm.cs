using System.ComponentModel;

namespace PowerX.App.Views;

/// <summary>Two-column "label / value" row that updates in place (no ItemsSource churn).</summary>
public sealed class NameValueVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _name = "";
    private string _value = "";

    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; Raise(nameof(Name)); }
    }

    public string Value
    {
        get => _value;
        set { if (_value == value) return; _value = value; Raise(nameof(Value)); }
    }

    private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
