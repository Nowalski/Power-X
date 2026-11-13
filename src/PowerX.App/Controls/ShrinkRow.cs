using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PowerX.App.Controls;

/// <summary>
/// A single horizontal line of children that gives up width in a chosen order when it does not
/// fit, instead of clipping whatever happens to come last.
/// <para>
/// A horizontal StackPanel measures every child at infinite width, so a long first item pushes
/// the rest past the edge and they are cut off mid-word; a Grid with Auto columns does the same.
/// Here every child first gets the width it wants. If the total is too wide, children with a
/// <c>ShrinkOrder</c> above 0 are narrowed, lowest order first, each no further than its
/// <c>ShrinkMin</c> (a TextBlock trims with an ellipsis if it has TextTrimming). A child with no
/// ShrinkMin that would end up too narrow to show anything useful is left out instead. Children
/// with order 0 always keep their full width.
/// </para>
/// </summary>
public sealed partial class ShrinkRow : Panel
{
    /// <summary>Narrower than this, a child that may disappear shows little more than "…".</summary>
    private const double DropBelow = 32;

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(ShrinkRow),
            new PropertyMetadata(0.0, (d, _) => ((ShrinkRow)d).InvalidateMeasure()));

    public static readonly DependencyProperty ShrinkOrderProperty =
        DependencyProperty.RegisterAttached("ShrinkOrder", typeof(int), typeof(ShrinkRow),
            new PropertyMetadata(0, OnChildSettingChanged));

    public static readonly DependencyProperty ShrinkMinProperty =
        DependencyProperty.RegisterAttached("ShrinkMin", typeof(double), typeof(ShrinkRow),
            new PropertyMetadata(0.0, OnChildSettingChanged));

    private static void OnChildSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((d as FrameworkElement)?.Parent as ShrinkRow)?.InvalidateMeasure();

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>0 = never shrinks. 1 gives up width first, then 2, and so on.</summary>
    public static int GetShrinkOrder(UIElement element) => (int)element.GetValue(ShrinkOrderProperty);
    public static void SetShrinkOrder(UIElement element, int value) => element.SetValue(ShrinkOrderProperty, value);

    /// <summary>The narrowest a shrinking child may get. 0 (the default) means it may be left out.</summary>
    public static double GetShrinkMin(UIElement element) => (double)element.GetValue(ShrinkMinProperty);
    public static void SetShrinkMin(UIElement element, double value) => element.SetValue(ShrinkMinProperty, value);

    // Widths decided in Measure and used as they are by Arrange. 0 means the child is not shown.
    private double[] _widths = [];

    private double Total()
    {
        double sum = 0;
        int count = 0;
        foreach (double w in _widths)
            if (w > 0) { sum += w; count++; }
        return sum + Spacing * Math.Max(0, count - 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int n = Children.Count;
        _widths = new double[n];
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        for (int i = 0; i < n; i++)
        {
            Children[i].Measure(unbounded);
            if (Children[i].Visibility == Visibility.Visible) _widths[i] = Children[i].DesiredSize.Width;
        }

        double available = availableSize.Width;
        if (!double.IsInfinity(available) && Total() > available)
        {
            var orders = Enumerable.Range(0, n)
                .Select(i => GetShrinkOrder(Children[i]))
                .Where(o => o > 0)
                .Distinct()
                .Order();

            foreach (int order in orders)
            {
                for (int i = 0; i < n; i++)
                {
                    double excess = Total() - available;
                    if (excess <= 0) break;
                    if (_widths[i] <= 0 || GetShrinkOrder(Children[i]) != order) continue;

                    double floor = GetShrinkMin(Children[i]);
                    double target = Math.Max(floor, _widths[i] - excess);
                    if (floor <= 0 && target < DropBelow) target = 0;
                    _widths[i] = Math.Min(_widths[i], target);
                }
                if (Total() <= available) break;
            }

            // Measure the narrowed children again so their text trims to the width they now have.
            for (int i = 0; i < n; i++)
                if (Children[i].Visibility == Visibility.Visible && _widths[i] < Children[i].DesiredSize.Width)
                    Children[i].Measure(new Size(_widths[i], availableSize.Height));
        }

        double height = 0;
        for (int i = 0; i < n; i++)
            if (_widths[i] > 0) height = Math.Max(height, Children[i].DesiredSize.Height);
        double width = Total();
        return new Size(double.IsInfinity(available) ? width : Math.Min(width, available), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            double w = i < _widths.Length ? _widths[i] : 0;
            if (w <= 0)
            {
                // Given no room rather than collapsed: changing Visibility here would re-enter layout.
                Children[i].Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            // The full row height, so each child's VerticalAlignment decides where it sits.
            Children[i].Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + Spacing;
        }
        return finalSize;
    }
}
