using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace PowerX.App.Controls;

/// <summary>
/// A <see cref="Grid"/> that lets the owner set the pointer cursor (e.g. the ↔ resize cursor when
/// hovering a column boundary). <c>ProtectedCursor</c> is only reachable from a subclass.
/// </summary>
public sealed partial class ResizeGrid : Grid
{
    private bool _resizeCursor;

    public void ShowResizeCursor(bool on)
    {
        if (on == _resizeCursor) return;
        _resizeCursor = on;
        try
        {
            ProtectedCursor = on
                ? InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast)
                : InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        }
        catch { /* best-effort */ }
    }
}
