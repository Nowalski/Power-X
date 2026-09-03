using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace PowerX.App.Controls;

/// <summary>
/// The WrapPanel WinUI 3 doesn't ship. Lays children left-to-right, wrapping to a new row when
/// the current one is full. Optional <see cref="ItemWidth"/> / <see cref="ItemHeight"/> force a
/// uniform cell size (so cards in a grid line up); otherwise each child takes its desired size.
/// </summary>
public sealed partial class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, OnLayoutChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, OnLayoutChanged));

    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(double.NaN, OnLayoutChanged));

    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(double.NaN, OnLayoutChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty);
        set => SetValue(ItemWidthProperty, value);
    }

    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WrapPanel)d).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        double fixedW = ItemWidth, fixedH = ItemHeight;
        bool hasFixedW = !double.IsNaN(fixedW), hasFixedH = !double.IsNaN(fixedH);

        double lineWidth = 0, lineHeight = 0, totalWidth = 0, totalHeight = 0;
        var childConstraint = new Size(
            hasFixedW ? fixedW : availableSize.Width,
            hasFixedH ? fixedH : availableSize.Height);

        foreach (var child in Children)
        {
            child.Measure(childConstraint);
            double w = hasFixedW ? fixedW : child.DesiredSize.Width;
            double h = hasFixedH ? fixedH : child.DesiredSize.Height;

            if (lineWidth > 0 && lineWidth + HorizontalSpacing + w > availableSize.Width + 0.5)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += (totalHeight > 0 ? VerticalSpacing : 0) + lineHeight;
                lineWidth = w;
                lineHeight = h;
            }
            else
            {
                lineWidth += (lineWidth > 0 ? HorizontalSpacing : 0) + w;
                lineHeight = Math.Max(lineHeight, h);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += (totalHeight > 0 ? VerticalSpacing : 0) + lineHeight;
        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double fixedW = ItemWidth, fixedH = ItemHeight;
        bool hasFixedW = !double.IsNaN(fixedW), hasFixedH = !double.IsNaN(fixedH);

        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            double w = hasFixedW ? fixedW : child.DesiredSize.Width;
            double h = hasFixedH ? fixedH : child.DesiredSize.Height;

            if (x > 0 && x + w > finalSize.Width + 0.5)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, w, h));
            x += w + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, h);
        }

        return new Size(finalSize.Width, Math.Max(finalSize.Height, y + lineHeight));
    }
}
