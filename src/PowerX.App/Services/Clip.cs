using Windows.ApplicationModel.DataTransfer;

namespace PowerX.App.Services;

/// <summary>
/// Clipboard writes that do not crash the app. The Windows clipboard is a shared, single-owner
/// resource; another app holding it open makes <c>Clipboard.SetContent</c> throw a transient
/// <c>COMException</c>. That should never take down PowerX, so it is retried briefly and then
/// given up on quietly.
/// </summary>
internal static class Clip
{
    public static bool SetText(string text)
    {
        var pkg = new DataPackage();
        pkg.SetText(text);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetContent(pkg);
                return true;
            }
            catch (Exception ex)
            {
                if (attempt == 2) { App.Log("Clipboard", ex); return false; }
                Thread.Sleep(40);
            }
        }
        return false;
    }
}
