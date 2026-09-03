using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace PowerX.App.Controls;

/// <summary>
/// A thin rounded load meter. The fill is a full-width rectangle scaled on the X axis (GPU
/// render transform) so value changes animate smoothly. Colour shifts blue → amber → red.
/// </summary>
public sealed class LoadBar : Grid
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(LoadBar),
            new PropertyMetadata(0.0, (d, _) => ((LoadBar)d).Render()));

    private readonly Rectangle _track = new() { RadiusX = 3, RadiusY = 3 };
    private readonly Rectangle _fill = new() { RadiusX = 3, RadiusY = 3 };
    private readonly ScaleTransform _scale = new() { ScaleX = 0 };

    public LoadBar()
    {
        Height = 6;
        _track.Fill = new SolidColorBrush(Color.FromArgb(0x24, 0x88, 0x88, 0x88));
        _fill.RenderTransform = _scale;
        _fill.RenderTransformOrigin = new Windows.Foundation.Point(0, 0.5);
        Children.Add(_track);
        Children.Add(_fill);
        SizeChanged += (_, _) => { _fill.Width = ActualWidth; Render(); };
    }

    /// <summary>0..100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Animate value changes (off for high-frequency per-core bars).</summary>
    public bool Animated { get; set; } = true;

    private void Render()
    {
        double target = Math.Clamp(Value, 0, 100) / 100.0;
        _fill.Fill = new SolidColorBrush(Value switch
        {
            >= 90 => Color.FromArgb(0xFF, 0xE0, 0x4F, 0x4F),
            >= 70 => Color.FromArgb(0xFF, 0xE0, 0x8A, 0x3A),
            >= 40 => Color.FromArgb(0xFF, 0xD9, 0xC0, 0x40),
            _ => Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5),
        });

        if (!Animated || !IsLoaded)
        {
            _scale.ScaleX = target;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = new Duration(TimeSpan.FromMilliseconds(280)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(anim, _scale);
        Storyboard.SetTargetProperty(anim, "ScaleX");
        new Storyboard { Children = { anim } }.Begin();
    }
}
