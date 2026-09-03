using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PowerX.App;

/// <summary>
/// Small layout helpers for page content inside a <c>PageScrollStyle</c> ScrollViewer.
/// <para>
/// WinUI quirk: a <c>Stretch</c> element with <c>MaxWidth</c> smaller than the available width does
/// NOT fill to its MaxWidth — it collapses to its content's desired width and drifts (content
/// looks "cut off" or shoved to one side as you enlarge the window). These helpers set an explicit
/// <c>Width</c> from the page's own size so the content is predictable: filled up to a cap and
/// centred.
/// </para>
/// </summary>
internal static class PageLayout
{
    /// <summary>Centre <paramref name="content"/> and keep its width at min(cap, page − gutter).</summary>
    public static void CenterCap(Page page, FrameworkElement content, double cap, double gutter = 40)
    {
        content.HorizontalAlignment = HorizontalAlignment.Center;
        void Fit(double available) => content.Width = Math.Max(0, Math.Min(cap, available - gutter));
        page.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        if (page.ActualWidth > 0) Fit(page.ActualWidth);
    }
}
