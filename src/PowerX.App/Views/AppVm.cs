using System.ComponentModel;
using PowerX.Core.Debloat;

namespace PowerX.App.Views;

public sealed class AppVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public required InstalledApp App { get; init; }

    public string DisplayName => App.DisplayName;
    public string Category => App.Category;
    public string Publisher => Shorten(App.Publisher);
    public string Description => App.Catalog?.Description ?? "Installed package, not in the curated catalog. Remove only if you know what it is.";
    public string ClassLabel => App.Class switch
    {
        RemovalClass.RecommendedRemovable => "Safe to remove",
        RemovalClass.Optional => "Optional",
        RemovalClass.Advanced => "Advanced",
        _ => "Keep",
    };
    public string RestoreLabel => App.Catalog?.Restore == RestoreDifficulty.Difficult
        ? "Hard to reinstall"
        : "Reinstallable from the Store";

    // Curated non-"Keep" entries are removable (we run elevated and remove for all users +
    // deprovision). Un-catalogued packages fall back to the system-signature check.
    public bool CanRemove => App.Catalog is not null
        ? App.Catalog.Class != RemovalClass.KeepSystem
        : !App.NonRemovable;

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Raise(nameof(Selected)); }
    }

    private bool _removing;
    public bool Removing
    {
        get => _removing;
        set { if (_removing == value) return; _removing = value; Raise(nameof(Removing)); Raise(nameof(RowEnabled)); }
    }

    public bool RowEnabled => !_removing;

    private static string Shorten(string publisher)
    {
        // "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" -> "Microsoft Corporation"
        int cn = publisher.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (cn < 0) return publisher;
        string rest = publisher[(cn + 3)..];
        int comma = rest.IndexOf(',');
        return comma > 0 ? rest[..comma].Trim() : rest.Trim();
    }

    private void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
