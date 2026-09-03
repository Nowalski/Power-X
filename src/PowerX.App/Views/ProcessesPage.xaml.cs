using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Processes;
using Windows.ApplicationModel.DataTransfer;

namespace PowerX.App.Views;

public sealed partial class ProcessesPage : Page
{
    private const int MaxRows = 300;
    private const int ReorderEveryTicks = 3;

    private readonly ObservableCollection<ProcessRow> _rows = [];
    private readonly Dictionary<int, ProcessRow> _byPid = [];
    private IDisposable? _subscription;
    private string _filter = "";
    private string _sortKey = "cpu";
    private bool _sortDesc = true;
    private int _tick;
    private bool _loaded;
    private ulong _totalRam;

    public ProcessesPage()
    {
        InitializeComponent();
        List.ItemsSource = _rows;
        _totalRam = TelemetryHub.Instance.LastMemory?.Value?.TotalPhysical ?? 0;
        _loaded = true;
        UpdateHeaderGlyphs();
        InitColumns();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    protected override void OnNavigatedFrom(NavigationEventArgs e) => _subscription?.Dispose();

    // ---------------------------------------------------------------- refresh

    private bool _menuOpen;

    private void Menu_Opening(object sender, object e) => _menuOpen = true;

    private void Menu_Closed(object sender, object e)
    {
        _menuOpen = false;
        if (FreezeButton.IsChecked != true) OnTick(null, EventArgs.Empty); // catch up (unless frozen on purpose)
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_loaded) return;
        if (_menuOpen) return;                 // hold the list still while a right-click menu is open
        // Freeze stops the *automatic* refresh (sender = TelemetryHub); explicit actions
        // (filter, or the unfrozen sort path) pass sender = null and still go through.
        if (sender is not null && FreezeButton.IsChecked == true) return;
        if (TelemetryHub.Instance.LastProcesses is not { } snap) return;
        if (_totalRam == 0) _totalRam = TelemetryHub.Instance.LastMemory?.Value?.TotalPhysical ?? 0;

        bool force = sender is null;
        bool reorder = force || _tick++ % ReorderEveryTicks == 0;

        IEnumerable<ProcessInfo> live = snap.Processes.Where(p => p.Pid > 0);
        if (_filter.Length > 0)
            live = live.Where(p => p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        var byPid = live.ToDictionary(p => p.Pid);

        Summary.Text = $"{snap.TotalProcesses} processes · {snap.TotalThreads} threads" +
                       (FreezeButton.IsChecked == true ? " · frozen" : "");

        for (int i = _rows.Count - 1; i >= 0; i--)
        {
            if (byPid.TryGetValue(_rows[i].Pid, out var info)) _rows[i].Update(info, _totalRam);
            else { _byPid.Remove(_rows[i].Pid); _rows.RemoveAt(i); }
        }

        if (!reorder) return;

        var target = Sort(live).Take(MaxRows).ToList();
        for (int i = 0; i < target.Count; i++)
        {
            var info = target[i];
            if (i < _rows.Count && _rows[i].Pid == info.Pid) continue;

            if (_byPid.TryGetValue(info.Pid, out var existing))
            {
                int j = _rows.IndexOf(existing);
                if (j > i) _rows.Move(j, i);
            }
            else
            {
                var row = new ProcessRow(info.Pid);
                row.Update(info, _totalRam);
                _byPid[info.Pid] = row;
                _rows.Insert(Math.Min(i, _rows.Count), row);
            }
        }
        while (_rows.Count > target.Count)
        {
            _byPid.Remove(_rows[^1].Pid);
            _rows.RemoveAt(_rows.Count - 1);
        }
    }

    private IEnumerable<ProcessInfo> Sort(IEnumerable<ProcessInfo> src)
    {
        Func<ProcessInfo, object> key = _sortKey switch
        {
            "name" => p => p.Name,
            "pid" => p => p.Pid,
            "mem" => p => p.WorkingSetBytes,
            "io" => p => p.IoBytesPerSec,
            "thr" => p => p.ThreadCount,
            _ => p => p.CpuPercent,
        };
        var ordered = _sortDesc
            ? src.OrderByDescending(key, Comparer<object>.Create(Compare))
            : src.OrderBy(key, Comparer<object>.Create(Compare));
        return _sortKey == "cpu" ? ordered.ThenByDescending(p => p.WorkingSetBytes) : ordered;

        static int Compare(object a, object b) => a is string sa ? string.Compare(sa, (string)b, StringComparison.OrdinalIgnoreCase) : Comparer<object>.Default.Compare(a, b);
    }

    // ---------------------------------------------------------------- toolbar / headers

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _filter = Filter.Text.Trim();
        OnTick(null, EventArgs.Empty);
    }

    private void Freeze_Click(object sender, RoutedEventArgs e)
    {
        if (FreezeButton.IsChecked != true) OnTick(null, EventArgs.Empty);  // resume right away
    }

    // ---------------------------------------------------------------- resizable columns
    //
    // {Binding} on ColumnDefinition.Width does not propagate in WinUI 3, so widths are applied
    // imperatively to the header grid and to each realized row grid. Column boundaries are
    // draggable: pointer events are handled on the (never-moving) HeaderGrid with
    // handledEventsToo — stable coordinate frame, and a header-button press still sorts
    // everywhere except within a few px of a boundary. Dragging a boundary transfers width
    // between its two adjacent columns (Excel-style), so the line follows the cursor.

    private const double GripTolerance = 8;

    // Each boundary: left column (null = the flexible Name column) and right column. Dragging
    // right grows Left and shrinks Right by the same amount.
    private static readonly (string? Left, string Right)[] Boundaries =
    [
        (null, "Pid"),
        ("Pid", "Cpu"),
        ("Cpu", "Mem"),
        ("Mem", "Io"),
        ("Io", "Thr"),
    ];

    private readonly ProcessColumns _cols = new();
    private int _dragIndex = -1;
    private double _dragStartX;
    private double _dragStartLeft;   // start width of the boundary's left column (0 for Name)
    private double _dragStartRight;  // start width of the boundary's right column

    private void InitColumns()
    {
        ApplyColumns(HeaderGrid);
        _cols.PropertyChanged += (_, _) =>
        {
            ApplyColumns(HeaderGrid);
            for (int i = 0; i < _rows.Count; i++)
                if (List.ContainerFromIndex(i) is ListViewItem { ContentTemplateRoot: Grid g })
                    ApplyColumns(g);
        };

        HeaderGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Header_PointerMoved), true);
        HeaderGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Header_PointerPressed), true);
        HeaderGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Header_PointerReleased), true);
        HeaderGrid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Header_PointerReleased), true);
        HeaderGrid.PointerExited += (_, _) =>
        {
            if (_dragIndex < 0) { HeaderGrid.ShowResizeCursor(false); ResizeGuide.Opacity = 0; }
        };
    }

    private void ApplyColumns(Grid grid)
    {
        if (grid.ColumnDefinitions.Count < 6) return;
        grid.ColumnDefinitions[1].Width = _cols.Pid;
        grid.ColumnDefinitions[2].Width = _cols.Cpu;
        grid.ColumnDefinitions[3].Width = _cols.Mem;
        grid.ColumnDefinitions[4].Width = _cols.Io;
        grid.ColumnDefinitions[5].Width = _cols.Thr;
    }

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer?.ContentTemplateRoot is Grid g) ApplyColumns(g);
    }

    /// <summary>X (in HeaderGrid space) of boundary <paramref name="index"/>, measured from the
    /// right edge so the flexible Name column doesn't distort it.</summary>
    private double BoundaryX(int index)
    {
        double w = HeaderGrid.ActualWidth;
        double pid = _cols.Pid.Value, cpu = _cols.Cpu.Value, mem = _cols.Mem.Value, io = _cols.Io.Value, thr = _cols.Thr.Value;
        return index switch
        {
            0 => w - pid - cpu - mem - io - thr,   // Name | Pid
            1 => w - cpu - mem - io - thr,          // Pid  | Cpu
            2 => w - mem - io - thr,                // Cpu  | Mem
            3 => w - io - thr,                      // Mem  | Io
            4 => w - thr,                           // Io   | Thr
            _ => 0,
        };
    }

    private int BoundaryNear(double x)
    {
        if (HeaderGrid.ActualWidth <= 0) return -1;
        for (int i = 0; i < Boundaries.Length; i++)
            if (Math.Abs(x - BoundaryX(i)) <= GripTolerance) return i;
        return -1;
    }

    private void PositionGuide(double headerLocalX) =>
        ResizeGuide.Margin = new Thickness(HeaderGrid.Margin.Left + headerLocalX - 1, 0, 0, 0);

    private void Header_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(HeaderGrid).Position.X;

        if (_dragIndex >= 0)
        {
            ResizeTo(x - _dragStartX);
            PositionGuide(BoundaryX(_dragIndex));
            e.Handled = true;
            return;
        }

        int near = BoundaryNear(x);
        HeaderGrid.ShowResizeCursor(near >= 0);
        if (near >= 0) { PositionGuide(BoundaryX(near)); ResizeGuide.Opacity = 0.55; }
        else ResizeGuide.Opacity = 0;
    }

    private void ResizeTo(double delta)
    {
        var (left, right) = Boundaries[_dragIndex];

        // Clamp the applied delta so both adjacent columns stay in range.
        double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
        if (left is not null)
        {
            lo = Math.Max(lo, _cols.Min(left) - _dragStartLeft);
            hi = Math.Min(hi, _cols.Max(left) - _dragStartLeft);
        }
        lo = Math.Max(lo, _dragStartRight - _cols.Max(right));
        hi = Math.Min(hi, _dragStartRight - _cols.Min(right));
        delta = Math.Clamp(delta, lo, hi);

        if (left is not null) _cols.SetPixels(left, _dragStartLeft + delta);
        _cols.SetPixels(right, _dragStartRight - delta);
    }

    private void Header_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(HeaderGrid).Position.X;
        int near = BoundaryNear(x);
        if (near < 0) return;   // not on a boundary — let the header button sort

        var (left, right) = Boundaries[near];
        _dragIndex = near;
        _dragStartX = x;
        _dragStartLeft = left is null ? 0 : _cols.Get(left);
        _dragStartRight = _cols.Get(right);
        HeaderGrid.CapturePointer(e.Pointer);
        PositionGuide(BoundaryX(near));
        ResizeGuide.Opacity = 0.9;
        e.Handled = true;
    }

    private void Header_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragIndex < 0) return;
        _dragIndex = -1;
        ResizeGuide.Opacity = 0;
        HeaderGrid.ShowResizeCursor(false);
        try { HeaderGrid.ReleasePointerCapture(e.Pointer); } catch { }
        _cols.Persist();
        e.Handled = true;
    }

    private void ResetColumns_Click(object sender, RoutedEventArgs e) => _cols.Reset();

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;
        if (_sortKey == key) _sortDesc = !_sortDesc;
        else { _sortKey = key; _sortDesc = key is not "name"; }
        UpdateHeaderGlyphs();

        // Sorting is an explicit action — it works even while the list is frozen. When frozen we
        // reorder the rows already on screen (keeping their frozen values); otherwise a normal
        // refresh re-sorts against live data.
        if (FreezeButton.IsChecked == true) SortRowsInPlace();
        else OnTick(null, EventArgs.Empty);
    }

    private void SortRowsInPlace()
    {
        Comparison<ProcessRow> cmp = _sortKey switch
        {
            "name" => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            "pid" => (a, b) => a.Pid.CompareTo(b.Pid),
            "mem" => (a, b) => a.WorkingSetBytes.CompareTo(b.WorkingSetBytes),
            "io" => (a, b) => a.IoBytesPerSec.CompareTo(b.IoBytesPerSec),
            "thr" => (a, b) => a.Threads.CompareTo(b.Threads),
            _ => (a, b) => a.Cpu.CompareTo(b.Cpu),
        };

        var sorted = _rows.ToList();
        sorted.Sort(cmp);
        if (_sortDesc) sorted.Reverse();

        for (int i = 0; i < sorted.Count; i++)
        {
            int cur = _rows.IndexOf(sorted[i]);
            if (cur != i) _rows.Move(cur, i);
        }
    }

    private void UpdateHeaderGlyphs()
    {
        (string key, Button btn, string label)[] cols =
        [
            ("name", HName, "Name"), ("pid", HPid, "PID"), ("cpu", HCpu, "CPU"),
            ("mem", HMem, "Memory"), ("io", HIo, "Disk I/O"), ("thr", HThr, "Threads"),
        ];
        foreach (var (key, btn, label) in cols)
            btn.Content = key == _sortKey ? $"{label} {(_sortDesc ? "▾" : "▴")}" : label;
    }

    // ---------------------------------------------------------------- context menu

    private static ProcessRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as ProcessRow;

    private async void End_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (!await Confirm($"End “{row.Name}”?", "Unsaved work in this program will be lost.", "End task")) return;
        Report(ProcessActions.EndTask(row.Pid), "End task");
    }

    private async void EndTree_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (TelemetryHub.Instance.LastProcesses is not { } snap) return;
        if (!await Confirm($"End “{row.Name}” and its child processes?", "Unsaved work will be lost.", "End tree")) return;
        Report(ProcessActions.EndTaskTree(row.Pid, snap), "End process tree");
    }

    private void Suspend_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) Report(ProcessActions.Suspend(r.Pid), "Suspend"); }

    private void Resume_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) Report(ProcessActions.Resume(r.Pid), "Resume"); }

    private void Eco_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) Report(ProcessActions.SetEfficiencyMode(r.Pid, true), "Efficiency mode"); }

    private void Priority_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r || sender is not FrameworkElement { Tag: string tag }) return;
        if (Enum.TryParse<ProcessPriority>(tag, out var pri))
            Report(ProcessActions.SetPriority(r.Pid, pri), "Set priority");
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r) return;
        Report(ProcessActions.OpenFileLocation(ProcessDetailsProvider.ImagePath(r.Pid)), "Open file location");
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r) return;
        Copy(ProcessDetailsProvider.ImagePath(r.Pid) ?? r.Name);
    }

    private void CopyPid_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) Copy(r.Pid.ToString()); }

    private void Search_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) ProcessActions.SearchOnline(r.Name); }

    private async void Props_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) await ShowDetails(r); }

    private async void List_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    { if (List.SelectedItem is ProcessRow r) await ShowDetails(r); }

    // ---------------------------------------------------------------- helpers

    private static void Copy(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
    }

    private async Task<bool> Confirm(string title, string body, string primary)
    {
        if (!Services.AppSettings.Current.ConfirmProcessActions) return true;
        var d = new ContentDialog
        {
            Title = title, Content = body,
            PrimaryButtonText = primary, CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
        };
        return await d.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void Report(PowerX.Core.Processes.ActionResult result, string what)
    {
        if (result.Success) return;
        await new ContentDialog
        {
            Title = $"Could not {what.ToLowerInvariant()}",
            Content = result.Message ?? "Unknown error.",
            CloseButtonText = "Close", XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private async Task ShowDetails(ProcessRow row)
    {
        var inspector = new Controls.ProcessInspector(row.Pid, row.Name);
        var dialog = new ContentDialog
        {
            Content = inspector,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        try { await dialog.ShowAsync(); }
        finally { inspector.Dispose(); }
    }
}
