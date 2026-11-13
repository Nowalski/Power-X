using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PowerX.App;

/// <summary>
/// Small layout helpers for page content inside a <c>PageScrollStyle</c> ScrollViewer.
/// <para>
/// WinUI quirk: a <c>Stretch</c> element with <c>MaxWidth</c> smaller than the available width does
/// NOT fill to its MaxWidth — it collapses to its content's desired width and drifts (content
/// looks "cut off" or shoved to one side as you enlarge the window). These helpers set an explicit
/// <c>Width</c> from the page's own size so the content is predictable: filled up to a cap.
/// </para>
/// </summary>
internal static class PageLayout
{
    /// <summary>
    /// The right padding of <c>PageScrollStyle</c> (App.xaml), which keeps content clear of the
    /// scrollbar. Pages with rows above their scroller stop those rows short by the same amount.
    /// </summary>
    public const double ScrollGutter = 16;

    /// <summary>
    /// Keep <paramref name="content"/> at the page's left edge, with its width at
    /// min(cap, page − gutter).
    /// <para>
    /// Left, not centred: every other page starts at the frame's left edge, and a centred page
    /// put its title somewhere else (12 px in at one size, 84 px at another), so the title jumped
    /// sideways when you moved between pages. The default gutter is the scroller's own right
    /// padding, so below the cap the content ends exactly where an uncapped page's content does.
    /// </para>
    /// </summary>
    public static void LeftCap(Page page, FrameworkElement content, double cap, double gutter = ScrollGutter)
    {
        content.HorizontalAlignment = HorizontalAlignment.Left;
        // Floor: a fractional width a hair wider than the viewport would clip the cards' right border.
        void Fit(double available) => content.Width = Math.Max(0, Math.Floor(Math.Min(cap, available - gutter)));
        page.SizeChanged += (_, e) => Fit(e.NewSize.Width);
        if (page.ActualWidth > 0) Fit(page.ActualWidth);
    }

    /// <summary>
    /// The cap for list pages (Processes, Services, Programs and the like) and Repair. Without one
    /// a maximized window on an ultrawide monitor stretched every row to about 3000 px, with the
    /// name at the far left and its buttons at the far right, and Repair's console to an empty
    /// box over 1700 px wide. The same as Home's cap, the widest of the dashboard pages.
    /// </summary>
    public const double ListCap = 1480;

    /// <summary>
    /// <see cref="LeftCap"/> for a page whose content is a Grid filling the page, with its list
    /// scrolling inside it rather than the whole page scrolling. No gutter: the Grid already
    /// reaches the page's right edge (its rows keep their own distance from the list's
    /// scrollbar), so below the cap nothing moves.
    /// </summary>
    public static void CapListPage(Page page, double cap = ListCap)
    {
        if (page.Content is FrameworkElement root) LeftCap(page, root, cap, gutter: 0);
    }
}
