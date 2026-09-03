using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerX.Core.Programs;

namespace PowerX.App.Views;

public sealed class ProgramVm
{
    public required InstalledProgram Program { get; init; }
    public string Name => Program.Name;
    public string Publisher => string.IsNullOrWhiteSpace(Program.Publisher) ? "Unknown publisher" : Program.Publisher;
    public string Size => Program.EstimatedSizeBytes > 0 ? Fmt.Bytes((ulong)Program.EstimatedSizeBytes) : "";
    public string Meta =>
        (string.IsNullOrWhiteSpace(Program.Version) ? "" : $"v{Program.Version}   ") +
        (Program.InstalledOn is { } d ? $"installed {d:yyyy-MM-dd}   " : "") +
        Program.Scope;
}

public sealed partial class ProgramsPage : Page
{
    private readonly ObservableCollection<ProgramVm> _view = [];
    private List<ProgramVm> _all = [];
    private string _filter = "";
    private int _sort;
    private bool _loaded;

    public ProgramsPage()
    {
        InitializeComponent();
        List.ItemsSource = _view;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loaded = false;
        RefreshButton.IsEnabled = false;
        IReadOnlyList<InstalledProgram> progs;
        try
        {
            progs = await Task.Run(() => InstalledPrograms.Enumerate());
        }
        catch (Exception ex)
        {
            App.Log("Programs.Enumerate", ex);
            Summary.Text = "Could not read installed programs: " + ex.Message;
            RefreshButton.IsEnabled = true;
            return;
        }
        _all = progs.Select(p => new ProgramVm { Program = p }).ToList();
        long totalSize = _all.Sum(p => p.Program.EstimatedSizeBytes);
        Summary.Text = $"{_all.Count} programs · {Fmt.Bytes((ulong)totalSize)} on disk (reported)";
        RefreshButton.IsEnabled = true;
        _loaded = true;
        Render();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _filter = Filter.Text.Trim();
        Render();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _sort = SortBox.SelectedIndex;
        Render();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Render()
    {
        IEnumerable<ProgramVm> shown = _all;
        if (_filter.Length > 0)
            shown = shown.Where(p =>
                p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                p.Publisher.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        shown = _sort switch
        {
            1 => shown.OrderByDescending(p => p.Program.EstimatedSizeBytes),
            2 => shown.OrderByDescending(p => p.Program.InstalledOn ?? DateTimeOffset.MinValue),
            _ => shown.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
        };

        _view.Clear();
        foreach (var p in shown.Take(1000)) _view.Add(p);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name) return;
        var vm = _all.FirstOrDefault(p => p.Name == name);
        if (vm is null) return;

        var ok = await new ContentDialog
        {
            Title = $"Uninstall {vm.Name}?",
            Content = "This launches the program's own uninstaller. Follow its prompts. PowerX doesn't delete anything itself.",
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        }.ShowAsync();
        if (ok != ContentDialogResult.Primary) return;

        var result = InstalledPrograms.Uninstall(vm.Program, quiet: false);
        if (!result.Success)
            await new ContentDialog
            {
                Title = "Could not start the uninstaller",
                Content = result.Message ?? "Unknown error.",
                CloseButtonText = "Close", XamlRoot = XamlRoot,
            }.ShowAsync();
    }
}
