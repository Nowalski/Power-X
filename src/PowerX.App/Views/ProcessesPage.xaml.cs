using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Processes;

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
        {
            // Digits also match a PID by its start, so "12" finds 1204 as you type it. A name
            // that happens to contain the digits ("obs64.exe") still matches as before.
            string filter = _filter;
            bool digits = filter.All(char.IsAsciiDigit);
            live = live.Where(p =>
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (digits && p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture).StartsWith(filter, StringComparison.Ordinal)));
        }
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
    //
    // The saved widths are a preference. When the table is too narrow for all of them plus the
    // Name column's minimum, FitColumns narrows the five fixed columns (each no further than its
    // own minimum) so the table fits; otherwise the last column was simply cut off ("Thre").
    // What is applied to the header and the rows is _applied, never the raw preference, and the
    // boundary maths below works on _applied too.

    private const double GripTolerance = 8;
    private const double NameMinWidth = 120;   // the Name column's MinWidth in the XAML

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
    private readonly double[] _applied = new double[ProcessColumns.Keys.Length];   // widths in use, see FitColumns
    private int _dragIndex = -1;
    private double _dragStartX;
    private double _dragStartLeft;   // start width of the boundary's left column (0 for Name)
    private double _dragStartRight;  // start width of the boundary's right column
    private double _dragStartName;   // start width of the Name column

    private void InitColumns()
    {
        FitColumns();
        _cols.PropertyChanged += (_, _) => FitColumns();
        // The Border, not HeaderGrid: when the columns overflow, a Grid is laid out at its full
        // desired width and clipped, so HeaderGrid.ActualWidth would report the overflowing width.
        HeaderBorder.SizeChanged += (_, _) => FitColumns();

        HeaderGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(Header_PointerMoved), true);
        HeaderGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Header_PointerPressed), true);
        HeaderGrid.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Header_PointerReleased), true);
        HeaderGrid.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(Header_PointerReleased), true);
        HeaderGrid.PointerExited += (_, _) =>
        {
            if (_dragIndex < 0) { HeaderGrid.ShowResizeCursor(false); ResizeGuide.Opacity = 0; }
        };
    }

    /// <summary>Width the table's columns share: the header row minus HeaderGrid's margins, which
    /// is also a list row minus its right padding.</summary>
    private double TableWidth() =>
        HeaderBorder.ActualWidth - HeaderGrid.Margin.Left - HeaderGrid.Margin.Right;

    /// <summary>Work out the widths actually used (see the comment block above) and apply them to
    /// the header and every realized row.</summary>
    private void FitColumns()
    {
        var keys = ProcessColumns.Keys;
        double room = TableWidth() - NameMinWidth;
        double wanted = 0, slack = 0;
        foreach (var k in keys)
        {
            wanted += _cols.Get(k);
            slack += Math.Max(0, _cols.Get(k) - ProcessColumns.Min(k));
        }

        // Share the shortfall out in proportion to how far each column is above its minimum.
        double share = TableWidth() > 0 && wanted > room && slack > 0
            ? Math.Min(1, (wanted - room) / slack)
            : 0;
        for (int i = 0; i < keys.Length; i++)
        {
            double want = _cols.Get(keys[i]);
            double over = Math.Max(0, want - ProcessColumns.Min(keys[i]));
            _applied[i] = Math.Floor(want - over * share);
        }

        ApplyColumns(HeaderGrid);
        for (int i = 0; i < _rows.Count; i++)
            if (List.ContainerFromIndex(i) is ListViewItem { ContentTemplateRoot: Grid g })
                ApplyColumns(g);
    }

    private void ApplyColumns(Grid grid)
    {
        if (grid.ColumnDefinitions.Count < 6) return;
        for (int i = 0; i < _applied.Length; i++)
            if (grid.ColumnDefinitions[i + 1].Width.Value != _applied[i])
                grid.ColumnDefinitions[i + 1].Width = new GridLength(_applied[i]);
    }

    private void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer?.ContentTemplateRoot is Grid g) ApplyColumns(g);
    }

    private double Applied(string key) => _applied[Array.IndexOf(ProcessColumns.Keys, key)];

    /// <summary>X (in HeaderGrid space) of boundary <paramref name="index"/>, measured from the
    /// right edge so the flexible Name column doesn't distort it.</summary>
    private double BoundaryX(int index)
    {
        double x = TableWidth();
        for (int i = _applied.Length - 1; i >= index; i--) x -= _applied[i];
        return index is >= 0 and < 5 ? x : 0;
    }

    private int BoundaryNear(double x)
    {
        if (TableWidth() <= 0) return -1;
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
            lo = Math.Max(lo, ProcessColumns.Min(left) - _dragStartLeft);
            hi = Math.Min(hi, ProcessColumns.Max(left) - _dragStartLeft);
        }
        else
        {
            // The Name column is the left one: PID can only take what Name has above its
            // minimum, or the table would overflow again and FitColumns would squeeze the rest.
            lo = Math.Max(lo, Math.Min(0, NameMinWidth - _dragStartName));
        }
        lo = Math.Max(lo, _dragStartRight - ProcessColumns.Max(right));
        hi = Math.Min(hi, _dragStartRight - ProcessColumns.Min(right));
        if (lo > hi) return;
        delta = Math.Clamp(delta, lo, hi);

        if (left is not null) _cols.SetPixels(left, _dragStartLeft + delta);
        _cols.SetPixels(right, _dragStartRight - delta);
    }

    private void Header_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        double x = e.GetCurrentPoint(HeaderGrid).Position.X;
        int near = BoundaryNear(x);
        if (near < 0) return;   // not on a boundary — let the header button sort

        // Start from what is on screen. If FitColumns had narrowed the saved widths to fit, adopt
        // the narrowed ones as the preference first; a drag then moves only the two columns
        // either side of the boundary, so the line stays under the cursor.
        for (int i = 0; i < ProcessColumns.Keys.Length; i++)
            if (_cols.Get(ProcessColumns.Keys[i]) != _applied[i])
                _cols.SetPixels(ProcessColumns.Keys[i], _applied[i]);

        var (left, right) = Boundaries[near];
        _dragIndex = near;
        _dragStartX = x;
        _dragStartLeft = left is null ? 0 : Applied(left);
        _dragStartRight = Applied(right);
        _dragStartName = BoundaryX(0);
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
        {
            string arrow = _sortDesc ? "▾" : "▴";
            // The arrow goes on the outer side of the label: after Name, but before the
            // right-aligned numeric labels, so their text still ends in line with the values.
            btn.Content = key != _sortKey ? label : key == "name" ? $"{label} {arrow}" : $"{arrow} {label}";
        }
    }

    // ---------------------------------------------------------------- context menu

    private static ProcessRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as ProcessRow;

    private async void End_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || await DemoBlocked()) return;
        // A critical process takes Windows down with it, so that warning is shown whatever the
        // "Confirm before ending or changing processes" setting says.
        bool ok = ProcessActions.IsCritical(row.Pid)
            ? await ConfirmCritical($"{row.Name} is a critical Windows process",
                $"Ending {row.Name} makes Windows stop or restart immediately. "
                + "Unsaved work in every open program is lost.", "End task")
            : await Confirm($"End {row.Name}?", "Unsaved work in this program will be lost.", "End task");
        if (!ok) return;
        Report(ProcessActions.EndTask(row.Pid), "End task");
    }

    private async void EndTree_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || await DemoBlocked()) return;
        if (TelemetryHub.Instance.LastProcesses is not { } snap) return;
        bool ok = ProcessActions.TreeHasCritical(row.Pid, snap)
            ? await ConfirmCritical("This tree includes a critical Windows process",
                $"{row.Name} or one of its child processes is critical to Windows. Ending the tree "
                + "makes Windows stop or restart immediately. Unsaved work in every open program is lost.", "End tree")
            : await Confirm($"End {row.Name} and its child processes?", "Unsaved work will be lost.", "End tree");
        if (!ok) return;
        Report(ProcessActions.EndTaskTree(row.Pid, snap), "End process tree");
    }

    private async void Suspend_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r && !await DemoBlocked()) Report(ProcessActions.Suspend(r.Pid), "Suspend"); }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r && !await DemoBlocked()) Report(ProcessActions.Resume(r.Pid), "Resume"); }

    private async void Eco_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r && !await DemoBlocked()) Report(ProcessActions.SetEfficiencyMode(r.Pid, true), "Efficiency mode"); }

    private async void Priority_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r || sender is not FrameworkElement { Tag: string tag }) return;
        if (await DemoBlocked()) return;
        if (Enum.TryParse<ProcessPriority>(tag, out var pri))
            Report(ProcessActions.SetPriority(r.Pid, pri), "Set priority");
    }

    /// <summary>
    /// In demo mode the rows are made up, but their PIDs are ordinary numbers that may belong to
    /// a real process on this PC, so an action would land on whatever that happens to be.
    /// </summary>
    private async Task<bool> DemoBlocked()
    {
        if (!Services.DemoData.Active) return false;
        try
        {
            await new ContentDialog
            {
                Title = "Demo mode",
                Content = "These processes are sample data, so nothing was changed.",
                CloseButtonText = "OK", XamlRoot = XamlRoot,
            }.ShowAsync();
        }
        catch (Exception ex) { App.Log("Processes.Demo", ex); }
        return true;
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
    { if (RowOf(sender) is { } r) Report(ProcessActions.SearchOnline(r.Name), "Search the web"); }

    private async void Props_Click(object sender, RoutedEventArgs e)
    { if (RowOf(sender) is { } r) await ShowDetails(r); }

    private async void List_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    { if (List.SelectedItem is ProcessRow r) await ShowDetails(r); }

    // ---------------------------------------------------------------- helpers

    private static void Copy(string text) => Services.Clip.SetText(text);

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

    /// <summary>The warning before ending a critical process. Never skipped by the Settings
    /// switch, and Cancel is the default so Enter does not take the PC down.</summary>
    private async Task<bool> ConfirmCritical(string title, string body, string primary)
    {
        var d = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary, CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
        };
        try { return await d.ShowAsync() == ContentDialogResult.Primary; }
        catch (Exception ex)
        {
            // Another dialog already open: treat it as Cancel rather than as a yes.
            App.Log("Processes.ConfirmCritical", ex);
            return false;
        }
    }

    private async void Report(PowerX.Core.Processes.ActionResult result, string what)
    {
        if (result.Success) return;
        try
        {
            await new ContentDialog
            {
                Title = $"Could not {what.ToLowerInvariant()}",
                Content = result.Message ?? "Unknown error.",
                CloseButtonText = "Close", XamlRoot = XamlRoot,
            }.ShowAsync();
        }
        catch (Exception ex)
        {
            // e.g. another ContentDialog is already open — don't let a fire-and-forget helper crash the app.
            App.Log("Processes.Report", ex);
        }
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
        // A ContentDialog clamps its content to ContentDialogMaxWidth, which is 548 by default.
        // The inspector used to declare a fixed 700 width, so every value column was laid out at
        // 700 and then clipped at the dialog edge, cutting long text ("About", the image path)
        // mid-word with no scrollbar to reveal it. The inspector now stretches instead of forcing
        // a width, and the dialog is allowed to be wide enough to be worth reading.
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;
        try { await dialog.ShowAsync(); }
        finally { inspector.Dispose(); }
    }
}
