using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PowerX.App.Services;
using PowerX.Core.Diagnostics;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;
using Windows.System;
using QuickActionsCore = PowerX.Core.Diagnostics.QuickActions;

namespace PowerX.App;

public sealed record PaletteCommand(string Title, string Category, Func<Task> Run);

public sealed partial class MainWindow
{
    private List<PaletteCommand>? _commands;

    private List<PaletteCommand> Commands => _commands ??= BuildCommands();

    private List<PaletteCommand> BuildCommands()
    {
        var list = new List<PaletteCommand>();

        void Nav(string title, string tag) => list.Add(new($"Go to {title}", "Page", () => { Navigate(tag); return Task.CompletedTask; }));
        Nav("Home", "home"); Nav("Health check", "health"); Nav("Processes", "processes"); Nav("CPU", "cpu"); Nav("Memory", "memory");
        Nav("GPU", "gpu"); Nav("Network", "network"); Nav("Firewall", "firewall");
        Nav("Programs", "programs"); Nav("Startup", "startup"); Nav("Scheduled tasks", "tasks");
        Nav("Services", "services"); Nav("Drivers", "drivers");
        Nav("Tweaks", "tweaks"); Nav("Debloat", "debloat"); Nav("Repair", "repair"); Nav("Tools", "tools");
        Nav("Storage explorer", "storage"); Nav("What changed", "changes"); Nav("Event log", "events");
        Nav("Crash insights", "crashes"); Nav("Change history", "history");

        void Act(string title, Func<PowerX.Core.Processes.ActionResult> run) =>
            list.Add(new(title, "Action", async () =>
            {
                var r = await Task.Run(run);
                if (!r.Success) await Toast($"{title} failed", r.Message);
            }));

        Act("Restart Explorer", QuickActionsCore.RestartExplorer);
        Act("Flush DNS cache", QuickActionsCore.FlushDns);
        Act("Empty Recycle Bin", QuickActionsCore.EmptyRecycleBin);
        list.Add(new("Open Windows Update", "Action", () => { QuickActionsCore.OpenSettings("ms-settings:windowsupdate"); return Task.CompletedTask; }));
        list.Add(new("Open Startup apps settings", "Action", () => { QuickActionsCore.OpenSettings("ms-settings:startupapps"); return Task.CompletedTask; }));

        // toggle recommended tweaks straight from the palette
        var engine = new TweakEngine(TweakCatalog.Default);
        foreach (var s in engine.GetAllStatus().Where(s => s.Definition.Recommended))
        {
            var def = s.Definition;
            bool on = s.State == TweakState.Applied;
            list.Add(new($"{(on ? "Revert" : "Apply")} tweak: {def.Name}", "Tweak", async () =>
            {
                var rec = await Task.Run(() => new TweakEngine(TweakCatalog.Default)
                    .Execute(def.Id, on ? ChangeAction.Revert : ChangeAction.Apply));
                if (!rec.Success) await Toast("Tweak failed", rec.Message);
                _commands = null; // rebuild so the label flips
            }));
        }

        return list;
    }

    private void Palette_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        TogglePalette();
    }

    private void Palette_Click(object sender, RoutedEventArgs e) => TogglePalette();

    private void PaletteEntry_GotFocus(object sender, RoutedEventArgs e)
    {
        if (PaletteOverlay.Visibility == Visibility.Collapsed) TogglePalette();
    }

    private void TogglePalette()
    {
        bool show = PaletteOverlay.Visibility == Visibility.Collapsed;
        PaletteOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            PaletteBox.Text = "";
            FilterPalette("");
            PaletteBox.Focus(FocusState.Programmatic);
        }
    }

    private void PaletteOverlay_Tapped(object sender, TappedRoutedEventArgs e) => PaletteOverlay.Visibility = Visibility.Collapsed;

    private void PaletteCard_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true; // don't close when clicking inside

    private void PaletteBox_TextChanged(object sender, TextChangedEventArgs e) => FilterPalette(PaletteBox.Text);

    private void FilterPalette(string term)
    {
        term = term.Trim();
        IEnumerable<PaletteCommand> src = Commands;
        if (term.Length > 0)
            src = Commands
                .Where(c => c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || c.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase));
        PaletteList.ItemsSource = src.Take(40).ToList();
        if (PaletteList.Items.Count > 0) PaletteList.SelectedIndex = 0;
    }

    private void PaletteBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                PaletteOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                if (PaletteList.SelectedItem is PaletteCommand c) _ = Execute(c);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                PaletteList.SelectedIndex = Math.Min(PaletteList.SelectedIndex + 1, PaletteList.Items.Count - 1);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                PaletteList.SelectedIndex = Math.Max(PaletteList.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
        }
    }

    private void PaletteList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteCommand c) _ = Execute(c);
    }

    private async Task Execute(PaletteCommand c)
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        try { await c.Run(); }
        catch (Exception ex) { App.Log("Palette", ex); }
    }

    private async Task Toast(string title, string? body) => await new ContentDialog
    {
        Title = title, Content = body ?? "", CloseButtonText = "OK", XamlRoot = Content.XamlRoot,
    }.ShowAsync();
}
