using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using PowerX.App.Services;
using Windows.Graphics;

namespace PowerX.App;

/// <summary>
/// Remembers the window's size, position and maximized state between runs, and sets a minimum
/// window size.
/// </summary>
public sealed partial class MainWindow
{
    // Below this (effective pixels) the nav pane and a page's toolbar no longer fit side by side.
    private const int MinWidthEpx = 900;
    private const int MinHeightEpx = 600;

    /// <summary>Set when POWERX_WINDOW_SIZE asked for a size. That size wins over the saved one,
    /// and it is never saved, so a screenshot run cannot overwrite the user's window.</summary>
    private SizeInt32? _qaSize;

    private RectInt32 _restoredBounds;
    private bool _haveRestoredBounds;
    private bool _maximized;
    private bool _minSizeHooked;

    private void RestorePlacement()
    {
        var s = AppSettings.Current;
        if (s.WindowWidth <= 0 || s.WindowHeight <= 0) return;
        try
        {
            var saved = new RectInt32(s.WindowX, s.WindowY, s.WindowWidth, s.WindowHeight);
            // A position on a display that is no longer connected (or has moved) would open the
            // window somewhere nobody can see it, so then Windows' default placement is kept.
            if (VisibleOn(saved) is { } work)
            {
                // The display may have switched to a lower resolution since.
                AppWindow.MoveAndResize(new RectInt32(saved.X, saved.Y,
                    Math.Min(saved.Width, work.Width), Math.Min(saved.Height, work.Height)));
            }
            if (s.WindowMaximized && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
        }
        catch (Exception ex) { App.Log("RestorePlacement", ex); }
    }

    /// <summary>
    /// The work area of the display that shows <paramref name="r"/>'s title bar, or null when no
    /// connected display shows enough of it to grab the window by.
    /// </summary>
    private static RectInt32? VisibleOn(RectInt32 r)
    {
        const int titleHeight = 40, enoughToGrab = 120, slack = 16;   // slack: the invisible resize border
        var area = DisplayArea.GetFromRect(new RectInt32(r.X, r.Y, r.Width, titleHeight), DisplayAreaFallback.None);
        if (area is null) return null;
        var work = area.WorkArea;
        int left = Math.Max(r.X, work.X);
        int right = Math.Min(r.X + r.Width, work.X + work.Width);
        bool topInside = r.Y >= work.Y - slack && r.Y + titleHeight <= work.Y + work.Height;
        return topInside && right - left >= enoughToGrab ? work : null;
    }

    private void TrackPlacement()
    {
        RememberBounds();
        AppWindow.Changed += (_, e) =>
        {
            if (e.DidPositionChange || e.DidSizeChange || e.DidPresenterChange) RememberBounds();
        };
    }

    /// <summary>Keep the last restored (not maximized, not minimized) bounds, which is what has
    /// to be saved: AppWindow reports the maximized size while maximized.</summary>
    private void RememberBounds()
    {
        if (AppWindow.Presenter is not OverlappedPresenter p) return;
        switch (p.State)
        {
            case OverlappedPresenterState.Restored:
                var pos = AppWindow.Position;
                var size = AppWindow.Size;
                _restoredBounds = new RectInt32(pos.X, pos.Y, size.Width, size.Height);
                _haveRestoredBounds = true;
                _maximized = false;
                break;
            case OverlappedPresenterState.Maximized:
                _maximized = true;
                break;
            // Minimized: keep what it was before, so closing from the taskbar does not save that.
        }
    }

    private void SavePlacement()
    {
        if (_qaSize is not null) return;
        try
        {
            RememberBounds();
            var s = AppSettings.Current;
            if (_haveRestoredBounds && _restoredBounds.Width > 0 && _restoredBounds.Height > 0)
            {
                s.WindowX = _restoredBounds.X;
                s.WindowY = _restoredBounds.Y;
                s.WindowWidth = _restoredBounds.Width;
                s.WindowHeight = _restoredBounds.Height;
            }
            s.WindowMaximized = _maximized;
            s.Save();
        }
        catch (Exception ex) { App.Log("SavePlacement", ex); }
    }

    /// <summary>
    /// PreferredMinimumWidth/Height are taken to be physical pixels, like every other AppWindow
    /// size (the API reference gives no unit), so the effective-pixel minimum is multiplied by the
    /// display's scale factor, and applied again when the window moves to a display with a
    /// different one.
    /// </summary>
    private void ApplyMinimumSize()
    {
        try
        {
            if (Content?.XamlRoot is not { } root || AppWindow.Presenter is not OverlappedPresenter p) return;
            if (!_minSizeHooked)
            {
                _minSizeHooked = true;
                root.Changed += (_, _) => ApplyMinimumSize();
            }
            double scale = root.RasterizationScale > 0 ? root.RasterizationScale : 1;
            int w = (int)Math.Round(MinWidthEpx * scale);
            int h = (int)Math.Round(MinHeightEpx * scale);
            // A requested QA size smaller than the minimum still wins.
            if (_qaSize is { } qa) { w = Math.Min(w, qa.Width); h = Math.Min(h, qa.Height); }
            if (p.PreferredMinimumWidth != w) p.PreferredMinimumWidth = w;
            if (p.PreferredMinimumHeight != h) p.PreferredMinimumHeight = h;
        }
        catch (Exception ex) { App.Log("MinimumSize", ex); }
    }
}
