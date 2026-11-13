using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Processes;

namespace PowerX.App.Views;

public sealed partial class ProcessesPage : Page
{
    // The list is a row of fixed slots, one per visible position (see ProcessRow). Each refresh
    // sorts the snapshot and writes process i into slot i. Items are only added or removed at the
    // end, and only when the number of processes shown changes, so the ListView never moves,
    // re-creates or animates a row: the rows stay put and the values in them change. It used to
    // keep one item per PID, re-sort it with Move/Insert every third tick and cut the list at 300
    // rows, so rows slid about, vanished at the cut-off and the order lagged and then jumped.
    private readonly ObservableCollection<ProcessRow> _rows = [];
    private readonly List<ProcessInfo> _shown = [];
    private ProcessSnapshot? _shownSnap;   // the snapshot on screen; a sort while frozen re-sorts it
    private IDisposable? _subscription;
    private string _filter = "";
    private string _sortKey = "cpu";
    private bool _sortDesc = true;
    private bool _loaded;
    private ulong _totalRam;

    // Selection belongs to a process, not to a slot: the PID is remembered when the user picks a
    // row, and each refresh highlights whichever slot now holds it (ProcessRow.SelectionOpacity).
    // It is not the ListView's own selection: moving that from slot to slot while the list has
    // keyboard focus (it does once a row has been clicked) also moves focus to the new slot and
    // scrolls it into view, so the list chased the selected process up and down as it re-ranked.
    private int _selectedPid;

    public ProcessesPage()
    {
        InitializeComponent();
        PageLayout.CapListPage(this);
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

    /// <summary>The process a right-click menu was opened on, copied when it opened. The slot
    /// under the cursor shows another process after the next refresh, and the menu's actions
    /// (and the confirmation dialogs they await) must still act on the one the user clicked.</summary>
    private ProcTarget? _menuTarget;

    private sealed record ProcTarget(int Pid, string Name);

    private void Menu_Opening(object sender, object e)
    {
        _menuOpen = true;
        _menuTarget = sender is FlyoutBase { Target: FrameworkElement { DataContext: ProcessRow r } } && r.Pid > 0
            ? new ProcTarget(r.Pid, r.Name)
            : null;
    }

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

        Summary.Text = $"{snap.TotalProcesses} processes · {snap.TotalThreads} threads" +
                       (FreezeButton.IsChecked == true ? " · frozen" : "");
        Render(snap);
    }

    /// <summary>Filter and sort <paramref name="snap"/> and write it into the slots. When as many
    /// processes are shown as before (no process started or exited, filter unchanged) this makes
    /// no change to the collection at all, only to the values inside the rows.</summary>
    private void Render(ProcessSnapshot snap)
    {
        _shownSnap = snap;
        _shown.Clear();
        string filter = _filter;
        bool digits = filter.Length > 0 && filter.All(char.IsAsciiDigit);
        bool selectedAlive = false;
        foreach (var p in snap.Processes)
        {
            if (p.Pid <= 0) continue;
            if (p.Pid == _selectedPid) selectedAlive = true;
            // Digits also match a PID by its start, so "12" finds 1204 as you type it. A name
            // that happens to contain the digits ("obs64.exe") still matches as before.
            if (filter.Length > 0 &&
                !p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !(digits && p.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture).StartsWith(filter, StringComparison.Ordinal)))
                continue;
            _shown.Add(p);
        }
        _shown.Sort(SortOrder());

        // A process that has exited is forgotten, so its PID being reused later selects nothing.
        // One that is only hidden by the filter stays selected for when the filter is cleared.
        if (!selectedAlive) _selectedPid = 0;

        while (_rows.Count > _shown.Count) _rows.RemoveAt(_rows.Count - 1);
        for (int i = 0; i < _shown.Count; i++)
        {
            if (i == _rows.Count) _rows.Add(new ProcessRow());
            _rows[i].Update(_shown[i], _totalRam);
            _rows[i].SelectionOpacity = _shown[i].Pid == _selectedPid ? 1 : 0;
        }
    }

    // ---------------------------------------------------------------- selection

    private void SelectRow(ProcessRow? row)
    {
        _selectedPid = row?.Pid ?? 0;
        foreach (var r in _rows) r.SelectionOpacity = r.Pid == _selectedPid && _selectedPid != 0 ? 1 : 0;
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e) => SelectRow(e.ClickedItem as ProcessRow);

    /// <summary>Arrow keys move keyboard focus from row to row; the row they land on is selected,
    /// as the ListView's own selection did. A click is handled by ItemClick.</summary>
    private void List_GotFocus(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is ListViewItem { FocusState: FocusState.Keyboard } item &&
            List.ItemFromContainer(item) is ProcessRow row)
            SelectRow(row);
    }

    /// <summary>
    /// The order for the current sort column. Always a total order (the PID breaks ties): List.Sort
    /// is not stable, and processes that compare equal would otherwise swap places between
    /// refreshes for no visible reason. CPU sorts on the value shown (one decimal, and anything
    /// under 0.05% shows as idle), so the hundreds of idle processes keep a steady order by memory
    /// instead of shuffling on noise in the third decimal.
    /// </summary>
    private Comparison<ProcessInfo> SortOrder()
    {
        int dir = _sortDesc ? -1 : 1;
        return _sortKey switch
        {
            "name" => (a, b) => Then(dir * string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase), a, b),
            "pid" => (a, b) => dir * a.Pid.CompareTo(b.Pid),
            "mem" => (a, b) => Then(dir * a.WorkingSetBytes.CompareTo(b.WorkingSetBytes), a, b),
            "io" => (a, b) => Then(dir * a.IoBytesPerSec.CompareTo(b.IoBytesPerSec), a, b),
            "thr" => (a, b) => Then(dir * a.ThreadCount.CompareTo(b.ThreadCount), a, b),
            _ => (a, b) =>
            {
                int c = dir * ShownCpu(a).CompareTo(ShownCpu(b));
                if (c == 0) c = b.WorkingSetBytes.CompareTo(a.WorkingSetBytes);   // then by memory, largest first
                return Then(c, a, b);
            },
        };

        static int Then(int c, ProcessInfo a, ProcessInfo b) => c != 0 ? c : a.Pid.CompareTo(b.Pid);
        static double ShownCpu(ProcessInfo p) => p.CpuPercent < 0.05 ? 0 : Math.Round(p.CpuPercent, 1);
    }

    // ---------------------------------------------------------------- toolbar / headers

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _filter = Filter.Text.Trim();
        // While frozen the filter works on the frozen snapshot, as sorting does, so the values
        // stay the ones that were frozen.
        if (FreezeButton.IsChecked == true && _shownSnap is { } frozen) Render(frozen);
        else OnTick(null, EventArgs.Empty);
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
        // re-sort the snapshot already on screen (keeping its frozen values); otherwise a normal
        // refresh re-sorts against live data.
        if (FreezeButton.IsChecked == true) { if (_shownSnap is { } frozen) Render(frozen); }
        else OnTick(null, EventArgs.Empty);
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
    //
    // Every action reads _menuTarget, the PID and name copied when the menu opened, never the
    // row's DataContext: that slot shows another process after the next refresh, which can come
    // between the click and the end of a confirmation dialog.

    private async void End_Click(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is not { } row || await DemoBlocked()) return;
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
        if (_menuTarget is not { } row || await DemoBlocked()) return;
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
    { if (_menuTarget is { } r && !await DemoBlocked()) Report(ProcessActions.Suspend(r.Pid), "Suspend"); }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    { if (_menuTarget is { } r && !await DemoBlocked()) Report(ProcessActions.Resume(r.Pid), "Resume"); }

    private async void Eco_Click(object sender, RoutedEventArgs e)
    { if (_menuTarget is { } r && !await DemoBlocked()) Report(ProcessActions.SetEfficiencyMode(r.Pid, true), "Efficiency mode"); }

    private async void Priority_Click(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is not { } r || sender is not FrameworkElement { Tag: string tag }) return;
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
        if (_menuTarget is not { } r) return;
        Report(ProcessActions.OpenFileLocation(ProcessDetailsProvider.ImagePath(r.Pid)), "Open file location");
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_menuTarget is not { } r) return;
        Copy(ProcessDetailsProvider.ImagePath(r.Pid) ?? r.Name);
    }

    private void CopyPid_Click(object sender, RoutedEventArgs e)
    { if (_menuTarget is { } r) Copy(r.Pid.ToString()); }

    private void Search_Click(object sender, RoutedEventArgs e)
    { if (_menuTarget is { } r) Report(ProcessActions.SearchOnline(r.Name), "Search the web"); }

    private async void Props_Click(object sender, RoutedEventArgs e)
    { if (_menuTarget is { } r) await ShowDetails(r); }

    private async void List_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        // The row that was double-clicked (its first click selected it). Copy the PID and name
        // now: the slot shows something else after the next refresh.
        if ((e.OriginalSource as FrameworkElement)?.DataContext is ProcessRow { Pid: > 0 } r)
            await ShowDetails(new ProcTarget(r.Pid, r.Name));
    }

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

    private async Task ShowDetails(ProcTarget row)
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
