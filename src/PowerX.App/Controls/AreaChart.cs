using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using WShapes = Microsoft.UI.Xaml.Shapes;

namespace PowerX.App.Controls;

/// <summary>
/// Modern history chart: smooth (Catmull-Rom) line over a gradient area fill, a soft dot on the
/// latest sample, faint gridlines and an optional axis-max label. Shapes live in an inner
/// <see cref="Canvas"/> so their bounds never feed layout. Caller owns the sample cadence.
/// </summary>
public sealed class AreaChart : Grid
{
    private readonly Canvas _canvas = new();
    private readonly WShapes.Path _grid = new() { StrokeThickness = 1 };
    private readonly WShapes.Path _fill = new() { IsHitTestVisible = false };
    private readonly WShapes.Path _line = new() { StrokeThickness = 1.8, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
    private readonly Ellipse _dot = new() { Width = 7, Height = 7, IsHitTestVisible = false };
    private readonly TextBlock _maxLabel = new() { FontSize = 10, IsHitTestVisible = false };

    private Color _accent = Colors.SteelBlue;
    private double[] _data = [];
    private double _max = 100;

    // Geometry is built once and then mutated in place each frame — the sample count is stable
    // (300) once history is seeded, so a redraw allocates nothing and just moves points.
    private readonly PathGeometry _lineGeo = new();
    private readonly PathGeometry _fillGeo = new();
    private readonly PathGeometry _gridGeo = new();
    private readonly PathFigure _lineFig = new() { IsClosed = false };
    private readonly PathFigure _fillFig = new() { IsClosed = true, IsFilled = true };
    private readonly LineSegment _fillLead = new();   // (0,h) → p[0]
    private readonly LineSegment _fillTail = new();   // p[^1] → (w,h)
    private readonly List<BezierSegment> _lineBez = [];
    private readonly List<BezierSegment> _fillBez = [];

    public AreaChart()
    {
        MinHeight = 28;
        Background = new SolidColorBrush(Color.FromArgb(0x12, 0x80, 0x80, 0x80));
        CornerRadius = new CornerRadius(6);
        _grid.Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0x88, 0x88, 0x88));
        _maxLabel.Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x88, 0x88, 0x88));

        _lineGeo.Figures.Add(_lineFig);
        _fillGeo.Figures.Add(_fillFig);
        for (int i = 0; i < 3; i++)
            _gridGeo.Figures.Add(new PathFigure { Segments = { new LineSegment() } });
        _line.Data = _lineGeo;
        _fill.Data = _fillGeo;
        _grid.Data = _gridGeo;

        _canvas.Children.Add(_grid);
        _canvas.Children.Add(_fill);
        _canvas.Children.Add(_line);
        _canvas.Children.Add(_dot);
        Children.Add(_canvas);
        Children.Add(_maxLabel);
        ApplyAccent();
        SizeChanged += (_, _) => ScheduleRedraw();
    }

    // Coalesce the (relatively expensive) bezier rebuild during a window resize.
    private DispatcherQueueTimer? _resizeTimer;

    private void ScheduleRedraw()
    {
        // Redraw immediately so the clip and geometry track the new size with no lag (this is
        // what caused content to look "cut off" at some sizes), then coalesce a second pass to
        // catch the end of a drag-resize.
        Redraw();
        _resizeTimer ??= DispatcherQueue?.CreateTimer();
        if (_resizeTimer is null) return;
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(60);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick -= OnResizeTick;
        _resizeTimer.Tick += OnResizeTick;
        _resizeTimer.Start();
    }

    private void OnResizeTick(DispatcherQueueTimer s, object e) => Redraw();

    public string? MaxLabel { get; set; }

    public Color Accent
    {
        get => _accent;
        set { _accent = value; ApplyAccent(); Redraw(); }
    }

    public void SetData(double[] data, double max)
    {
        _data = data;
        _max = max <= 0 ? 1 : max;
        Redraw();
    }

    private void ApplyAccent()
    {
        _line.Stroke = new SolidColorBrush(_accent);
        _dot.Fill = new SolidColorBrush(_accent);
        _dot.Stroke = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF));
        _dot.StrokeThickness = 1;
        _fill.Fill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(0x7A, _accent.R, _accent.G, _accent.B), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0x08, _accent.R, _accent.G, _accent.B), Offset = 1 },
            },
        };
    }

    private Point[] _pts = [];
    private readonly RectangleGeometry _clip = new();

    private void Redraw()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Keep the clip exactly on the current bounds every frame — never stale.
        _clip.Rect = new Rect(0, 0, w, h);
        if (!ReferenceEquals(Clip, _clip)) Clip = _clip;

        // gridlines at 25/50/75%
        double[] fracs = [0.25, 0.5, 0.75];
        for (int k = 0; k < 3; k++)
        {
            double y = h - fracs[k] * h;
            var fig = _gridGeo.Figures[k];
            fig.StartPoint = new Point(0, y);
            ((LineSegment)fig.Segments[0]).Point = new Point(w, y);
        }

        _maxLabel.Text = MaxLabel ?? "";
        Canvas.SetLeft(_maxLabel, 6);
        Canvas.SetTop(_maxLabel, 4);

        int n = _data.Length;
        if (n < 2) { _dot.Visibility = Visibility.Collapsed; _lineFig.Segments.Clear(); _fillFig.Segments.Clear(); return; }

        if (_pts.Length != n) _pts = new Point[n];
        double stepX = w / (n - 1);
        for (int i = 0; i < n; i++)
        {
            double y = h - Math.Clamp(_data[i] / _max, 0, 1) * (h - 6) - 3;
            _pts[i] = new Point(i * stepX, y);
        }

        int curves = n - 1;
        SyncBezier(_lineBez, curves);
        SyncBezier(_fillBez, curves);

        // line figure: [bez × curves]
        if (_lineFig.Segments.Count != curves)
        {
            _lineFig.Segments.Clear();
            for (int i = 0; i < curves; i++) _lineFig.Segments.Add(_lineBez[i]);
        }
        _lineFig.StartPoint = _pts[0];

        // fill figure: [lead line, bez × curves, tail line]
        if (_fillFig.Segments.Count != curves + 2)
        {
            _fillFig.Segments.Clear();
            _fillFig.Segments.Add(_fillLead);
            for (int i = 0; i < curves; i++) _fillFig.Segments.Add(_fillBez[i]);
            _fillFig.Segments.Add(_fillTail);
        }
        _fillFig.StartPoint = new Point(0, h);
        _fillLead.Point = _pts[0];
        _fillTail.Point = new Point(w, h);

        const double t = 1.0 / 6.0;
        for (int i = 0; i < curves; i++)
        {
            Point p0 = _pts[Math.Max(0, i - 1)], p1 = _pts[i], p2 = _pts[i + 1], p3 = _pts[Math.Min(n - 1, i + 2)];
            var c1 = new Point(p1.X + (p2.X - p0.X) * t, p1.Y + (p2.Y - p0.Y) * t);
            var c2 = new Point(p2.X - (p3.X - p1.X) * t, p2.Y - (p3.Y - p1.Y) * t);
            _lineBez[i].Point1 = c1; _lineBez[i].Point2 = c2; _lineBez[i].Point3 = p2;
            _fillBez[i].Point1 = c1; _fillBez[i].Point2 = c2; _fillBez[i].Point3 = p2;
        }

        _dot.Visibility = Visibility.Visible;
        Canvas.SetLeft(_dot, Math.Clamp(_pts[^1].X - _dot.Width / 2, 0, Math.Max(0, w - _dot.Width)));
        Canvas.SetTop(_dot, Math.Clamp(_pts[^1].Y - _dot.Height / 2, 0, Math.Max(0, h - _dot.Height)));
    }

    private static void SyncBezier(List<BezierSegment> pool, int need)
    {
        while (pool.Count < need) pool.Add(new BezierSegment());
        // pool only grows; extra segments simply aren't referenced by a figure
    }
}
