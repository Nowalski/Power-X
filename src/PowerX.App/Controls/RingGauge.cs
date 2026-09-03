using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace PowerX.App.Controls;

/// <summary>
/// A 270° ring gauge with the value in the centre. The arc colour shifts with the value.
/// Used for the CPU / GPU / memory hero numbers. Geometry is built once and mutated in place.
/// </summary>
public sealed class RingGauge : Grid
{
    private const double StartAngle = 135;   // degrees, clockwise from +x
    private const double SweepAngle = 270;

    private readonly Microsoft.UI.Xaml.Shapes.Path _track = new() { StrokeThickness = 9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly Microsoft.UI.Xaml.Shapes.Path _arc = new() { StrokeThickness = 9, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    private readonly TextBlock _value = new() { FontSize = 26, FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace") };
    private readonly TextBlock _caption = new() { FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center };

    private readonly PathGeometry _trackGeo = new();
    private readonly PathGeometry _arcGeo = new();
    private readonly PathFigure _trackFig = new();
    private readonly PathFigure _arcFig = new();
    private readonly ArcSegment _trackSeg = new() { SweepDirection = SweepDirection.Clockwise };
    private readonly ArcSegment _arcSeg = new() { SweepDirection = SweepDirection.Clockwise };
    private readonly SolidColorBrush _arcBrush = new(Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5));

    private double _pct;

    public RingGauge()
    {
        Width = 132;
        Height = 132;
        _track.Stroke = new SolidColorBrush(Color.FromArgb(0x28, 0x88, 0x88, 0x88));
        _arc.Stroke = _arcBrush;

        _trackFig.Segments.Add(_trackSeg);
        _arcFig.Segments.Add(_arcSeg);
        _trackGeo.Figures.Add(_trackFig);
        _arcGeo.Figures.Add(_arcFig);
        _track.Data = _trackGeo;
        _arc.Data = _arcGeo;

        Children.Add(_track);
        Children.Add(_arc);
        var center = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
        _value.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(_value);
        center.Children.Add(_caption);
        Children.Add(center);
        SizeChanged += (_, _) => Render();
    }

    public string Caption { get => _caption.Text; set => _caption.Text = value; }

    // Animated sweep: Value sets the target; an internal DP eases toward it and redraws.
    private static readonly DependencyProperty DisplayValueProperty =
        DependencyProperty.Register("DisplayValue", typeof(double), typeof(RingGauge),
            new PropertyMetadata(0.0, (d, e) => { ((RingGauge)d)._display = (double)e.NewValue; ((RingGauge)d).Render(); }));

    private double _display;

    /// <summary>0..100.</summary>
    public double Value
    {
        get => _pct;
        set
        {
            _pct = Math.Clamp(value, 0, 100);
            if (!IsLoaded) { _display = _pct; Render(); return; }
            var anim = new DoubleAnimation
            {
                To = _pct,
                Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(anim, this);
            Storyboard.SetTargetProperty(anim, "DisplayValue");
            new Storyboard { Children = { anim } }.Begin();
        }
    }

    public string ValueText { get => _value.Text; set => _value.Text = value; }

    private void Render()
    {
        double s = Math.Min(ActualWidth, ActualHeight);
        if (s <= 0) return;
        double r = s / 2 - _track.StrokeThickness;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double pct = Math.Clamp(_display, 0, 100);

        SetArc(_trackFig, _trackSeg, c, r, StartAngle, SweepAngle);

        double sweep = SweepAngle * pct / 100.0;
        _arc.Visibility = sweep < 0.5 ? Visibility.Collapsed : Visibility.Visible;
        if (_arc.Visibility == Visibility.Visible)
            SetArc(_arcFig, _arcSeg, c, r, StartAngle, sweep);

        _arcBrush.Color = pct switch
        {
            >= 90 => Color.FromArgb(0xFF, 0xE0, 0x4F, 0x4F),
            >= 70 => Color.FromArgb(0xFF, 0xE0, 0x8A, 0x3A),
            >= 40 => Color.FromArgb(0xFF, 0xD9, 0xC0, 0x40),
            _ => Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5),
        };
    }

    private static void SetArc(PathFigure fig, ArcSegment seg, Point center, double radius, double startDeg, double sweepDeg)
    {
        static Point P(Point c, double r, double deg)
        {
            double rad = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        fig.StartPoint = P(center, radius, startDeg);
        seg.Point = P(center, radius, startDeg + sweepDeg);
        seg.Size = new Size(radius, radius);
        seg.IsLargeArc = sweepDeg > 180;
    }
}
