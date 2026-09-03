using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace PowerX.App.Controls;

public sealed record Segment(string Label, double Value, Color Color);

/// <summary>
/// A horizontal stacked bar (Windows-Settings-style) — one coloured slice per category,
/// width proportional to its value. Zero-value segments are skipped. Rounded ends, hairline gaps.
/// </summary>
public sealed class SegmentBar : Grid
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private IReadOnlyList<Segment> _segments = [];

    public SegmentBar()
    {
        Height = 12;
        Background = new SolidColorBrush(Color.FromArgb(0x1C, 0x88, 0x88, 0x88));
        CornerRadius = new CornerRadius(6);
        _row.Children.Clear();
        Children.Add(_row);
        SizeChanged += (_, _) => Render();
    }

    public void SetSegments(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
        Render();
    }

    private void Render()
    {
        _row.Children.Clear();
        double total = 0;
        foreach (var s in _segments) total += Math.Max(0, s.Value);
        double w = ActualWidth;
        if (total <= 0 || w <= 0) return;

        var visible = _segments.Where(s => s.Value > 0).ToList();
        for (int i = 0; i < visible.Count; i++)
        {
            var s = visible[i];
            var scale = new ScaleTransform { ScaleX = 0, CenterX = 0 };
            var rect = new Rectangle
            {
                Width = Math.Max(2, w * s.Value / total - (i < visible.Count - 1 ? 1.5 : 0)),
                Height = ActualHeight,
                Fill = new SolidColorBrush(s.Color),
                Margin = new Thickness(0, 0, i < visible.Count - 1 ? 1.5 : 0, 0),
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0, 0.5),
            };
            ToolTipService.SetToolTip(rect, $"{s.Label}   {Fmt.Bytes((ulong)s.Value)}");
            _row.Children.Add(rect);

            var anim = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                BeginTime = TimeSpan.FromMilliseconds(40 * i),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(anim, scale);
            Storyboard.SetTargetProperty(anim, "ScaleX");
            new Storyboard { Children = { anim } }.Begin();
        }
    }
}
