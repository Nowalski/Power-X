using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;

namespace PowerX.App.Views;

public sealed partial class TweaksPage : Page
{
    private readonly TweakEngine _engine = new(TweakCatalog.Default);
    private string _filter = "";
    private bool _recOnly;

    public TweaksPage()
    {
        InitializeComponent();
        PageLayout.CenterCap(this, Root, 1320);
        BuildProfileStrip();
        Rebuild();
    }

    private void BuildProfileStrip()
    {
        ProfileStrip.Children.Clear();
        foreach (var profile in Profiles.All)
            ProfileStrip.Children.Add(BuildProfileCard(profile));
    }

    private static (string Tone, string Glyph, Windows.UI.Color Color) ProfileLook(ProfileTone tone) => tone switch
    {
        ProfileTone.Conservative => ("Conservative", "", Windows.UI.Color.FromArgb(0xFF, 0x3A, 0xA0, 0x55)), // shield
        ProfileTone.Balanced => ("Balanced", "", Windows.UI.Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5)),         // tiles
        ProfileTone.Aggressive => ("Aggressive", "", Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x7B, 0x2A)),     // lightning
        _ => ("Restore", "", Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A)),                              // undo
    };

    private Border BuildProfileCard(OptimizationProfile profile)
    {
        var (toneText, glyph, toneColor) = ProfileLook(profile.Tone);
        var toneBrush = new SolidColorBrush(toneColor);

        // circular tinted icon badge
        var badge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x24, toneColor.R, toneColor.G, toneColor.B)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = toneBrush,
            },
        };

        var name = new TextBlock
        {
            Text = profile.Name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            TextWrapping = TextWrapping.Wrap,
        };
        var tone = new TextBlock
        {
            Text = toneText.ToUpperInvariant(),
            FontSize = 10,
            CharacterSpacing = 60,
            Foreground = toneBrush,
        };
        var titleText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        titleText.Children.Add(name);
        titleText.Children.Add(tone);

        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(badge);
        head.Children.Add(titleText);

        var count = profile.Tone == ProfileTone.Restore
            ? "Undoes every applied tweak"
            : $"{profile.TweakIds.Count} tweaks · all reversible";

        var desc = Muted(profile.Description, 12);
        // Kept as a backstop so an over-long future description degrades to an ellipsis instead of
        // spilling out of the fixed-height card; the card is sized to fit today's longest one.
        desc.MaxLines = 8;
        desc.TextTrimming = TextTrimming.CharacterEllipsis;
        desc.VerticalAlignment = VerticalAlignment.Top;

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(head);
        body.Children.Add(desc);

        var footer = new StackPanel { Spacing = 8 };
        footer.Children.Add(Muted(count, 11, tertiary: true));
        var apply = new Button
        {
            Content = profile.Tone == ProfileTone.Restore ? "Preview & restore" : "Preview & apply",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Tag = profile.Id,
        };
        if (profile.Tone == ProfileTone.Aggressive)
            apply.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        apply.Click += ProfileApply_Click;
        footer.Children.Add(apply);

        // description stretches, footer pinned to the bottom so buttons line up across cards
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(body, 0);
        Grid.SetRow(footer, 1);
        layout.Children.Add(body);
        layout.Children.Add(footer);

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Padding = new Thickness(16),
            Child = layout,
        };
    }

    private async void ProfileApply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string profileId }) return;
        var profile = Profiles.Get(profileId);
        if (profile is null) return;

        bool restore = profile.Tone == ProfileTone.Restore;

        // Compute the concrete diff against the live machine state.
        var willChange = new List<string>();
        var already = 0;
        var unavailable = 0;
        var action = restore ? ChangeAction.Revert : ChangeAction.Apply;
        IReadOnlyList<string> ids;

        if (restore)
        {
            var applied = _engine.GetAllStatus().Where(s => s.State == TweakState.Applied).ToList();
            ids = applied.Select(s => s.Definition.Id).ToList();
            willChange.AddRange(applied.Select(s => s.Definition.Name));
        }
        else
        {
            ids = profile.TweakIds;
            var ctx = TweakContext.Detect();
            foreach (var id in profile.TweakIds)
            {
                var st = _engine.GetStatus(id, ctx);
                switch (st.State)
                {
                    case TweakState.Applied: already++; break;
                    case TweakState.Default or TweakState.Custom: willChange.Add(st.Definition.Name); break;
                    default: unavailable++; break;
                }
            }
        }

        if (willChange.Count == 0)
        {
            await new ContentDialog
            {
                Title = profile.Name,
                Content = restore
                    ? "PowerX hasn't applied any tweaks on this PC, so there is nothing to restore."
                    : "Everything in this profile is already configured on this PC. No changes needed.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            }.ShowAsync();
            return;
        }

        var list = new StackPanel { Spacing = 4 };
        list.Children.Add(new TextBlock
        {
            Text = restore
                ? $"{willChange.Count} tweak{Fmt.S(willChange.Count)} will be reverted to Windows defaults:"
                : $"{willChange.Count} tweak{Fmt.S(willChange.Count)} will be turned on:",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var name in willChange)
            list.Children.Add(Muted("•  " + name, 13));
        if (already > 0) list.Children.Add(Muted($"{already} already configured. These stay as they are.", 12, tertiary: true));
        if (unavailable > 0) list.Children.Add(Muted($"{unavailable} not applicable to this Windows build, so they are skipped.", 12, tertiary: true));

        var restorePoint = new CheckBox
        {
            Content = "Create a system restore point first (recommended)",
            IsChecked = !restore,
            Margin = new Thickness(0, 10, 0, 0),
        };
        list.Children.Add(restorePoint);

        var scroll = new ScrollViewer { Content = list, MaxHeight = 360 };
        var confirm = await new ContentDialog
        {
            Title = restore ? "Restore Windows defaults" : $"Apply {profile.Name}",
            Content = scroll,
            PrimaryButtonText = restore ? "Restore" : "Apply",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        }.ShowAsync();
        if (confirm != ContentDialogResult.Primary) return;

        string? rpNote = null;
        if (restorePoint.IsChecked == true)
        {
            var rp = await Task.Run(() => SystemRestore.Create($"PowerX before {profile.Name}"));
            rpNote = rp.Success ? "Restore point created. " : $"Restore point skipped ({rp.Message}). ";
        }

        var result = await Task.Run(() => _engine.ApplyMany(ids, action));
        BuildProfileStrip();
        Rebuild();

        var parts = new List<string>();
        if (result.Succeeded > 0) parts.Add($"{result.Succeeded} changed");
        if (result.AlreadyConfigured > 0) parts.Add($"{result.AlreadyConfigured} already set");
        if (result.Failed > 0) parts.Add($"{result.Failed} failed");
        var summary = parts.Count > 0 ? string.Join(", ", parts) + "." : "No changes.";

        var restartMsg = "";
        if (result.Restart.Any)
        {
            var scopes = new List<string>();
            if (result.Restart.Explorer) scopes.Add("Windows Explorer restart");
            if (result.Restart.SignOut) scopes.Add("sign out");
            if (result.Restart.Reboot) scopes.Add("reboot");
            if (result.Restart.Application) scopes.Add("PowerX restart");
            restartMsg = "\n\nTo take full effect: " + string.Join(", ", scopes) + ".";
        }

        await new ContentDialog
        {
            Title = restore ? "Restore complete" : $"{profile.Name} applied",
            Content = (rpNote ?? "") + summary + restartMsg
                      + (result.Failed > 0 ? "\n\nSee Change history for details on what failed." : ""),
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        _filter = Filter.Text.Trim();
        Rebuild();
    }

    private void RecOnly_Click(object sender, RoutedEventArgs e)
    {
        _recOnly = RecOnly.IsChecked == true;
        Rebuild();
    }

    private void Rebuild()
    {
        var all = _engine.GetAllStatus();
        int applied = all.Count(s => s.State == TweakState.Applied);
        Summary.Text = $"{all.Count} tweaks · {applied} applied";

        IEnumerable<TweakStatus> shown = all;
        if (_recOnly) shown = shown.Where(s => s.Definition.Recommended);
        if (_filter.Length > 0)
            shown = shown.Where(s =>
                s.Definition.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.Definition.Id.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.Definition.Category.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.Definition.Tags.Any(t => t.Contains(_filter, StringComparison.OrdinalIgnoreCase)));

        Sections.Children.Clear();
        foreach (var group in shown
                     .GroupBy(s => s.Definition.Category)
                     .OrderBy(g => g.Key))
        {
            Sections.Children.Add(new TextBlock
            {
                Text = group.Key,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(2, 14, 0, 4),
            });
            foreach (var status in group.OrderByDescending(s => s.Definition.Recommended).ThenBy(s => s.Definition.Name))
                Sections.Children.Add(BuildCard(status));
        }

        if (Sections.Children.Count == 0)
            Sections.Children.Add(new TextBlock
            {
                Text = "No tweaks match.",
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                Margin = new Thickness(2, 8, 0, 0),
            });
    }

    private Border BuildCard(TweakStatus status)
    {
        var d = status.Definition;

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(Chip(RiskLabel(d.Risk), Design.RiskBrush(d.Risk)));
        if (d.Recommended)
            chips.Children.Add(Chip("Recommended", (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]));
        if (d.Restart != RestartScope.None)
            chips.Children.Add(Chip($"needs {d.Restart}", (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        title.Children.Add(new TextBlock { Text = d.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(chips);

        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(title);
        text.Children.Add(Muted(d.WhatItDoes, 13));
        text.Children.Add(Muted($"Downside: {d.Downside}", 12, tertiary: true));
        if (!string.IsNullOrEmpty(status.Note))
            text.Children.Add(Muted(status.Note!, 12, tertiary: true));

        // Custom = some of a multi-value tweak's keys are set: treat as "off", let the user apply all.
        bool togglable = status.State is TweakState.Applied or TweakState.Default or TweakState.Custom;
        var toggle = new ToggleSwitch
        {
            IsOn = status.State == TweakState.Applied,
            IsEnabled = togglable,
            OnContent = "On",
            OffContent = "Off",
            VerticalAlignment = VerticalAlignment.Center,
            Tag = d.Id,
        };
        toggle.Toggled += Toggle_Toggled;

        if (status.State == TweakState.Custom)
            text.Children.Add(Muted("Partially configured. Turn it on to apply the rest, or off to restore defaults.", 12, tertiary: true));
        else if (!togglable)
            text.Children.Add(Muted(status.Note ?? "Not available on this edition or build of Windows.", 12, tertiary: true));

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(text);
        grid.Children.Add(toggle);

        return new Border
        {
            Style = (Style)Application.Current.Resources["CardStyle"],
            Margin = new Thickness(0, 3, 0, 3),
            Child = grid,
        };
    }

    private async void Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: string id } sw) return;

        var current = _engine.GetStatus(id).State;
        bool currentlyOn = current == TweakState.Applied;
        if (sw.IsOn == currentlyOn) return; // spurious / programmatic

        var def = _engine.Find(id)!;
        var action = sw.IsOn ? ChangeAction.Apply : ChangeAction.Revert;

        if (action == ChangeAction.Apply && NeedsConfirm(def.Risk))
        {
            var proceed = await new ContentDialog
            {
                Title = def.Name,
                Content = $"{def.WhatItDoes}\n\nWhy: {def.WhyYouMightWant}\n\nDownside: {def.Downside}\n\nRisk: {RiskLabel(def.Risk)}",
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            }.ShowAsync();
            if (proceed != ContentDialogResult.Primary)
            {
                sw.Toggled -= Toggle_Toggled; sw.IsOn = currentlyOn; sw.Toggled += Toggle_Toggled;
                return;
            }
        }

        var rec = _engine.Execute(id, action);
        if (!rec.Success)
        {
            sw.Toggled -= Toggle_Toggled; sw.IsOn = currentlyOn; sw.Toggled += Toggle_Toggled;
            await new ContentDialog
            {
                Title = "Could not apply this change",
                Content = rec.Message ?? "Unknown error.",
                CloseButtonText = "Close",
                XamlRoot = XamlRoot,
            }.ShowAsync();
            return;
        }

        Summary.Text = $"{_engine.Catalog.Count} tweaks · {_engine.GetAllStatus().Count(s => s.State == TweakState.Applied)} applied";

        if (rec.PreviousState != rec.ResultingState && def.Restart != RestartScope.None)
        {
            await new ContentDialog
            {
                Title = "Restart required",
                Content = def.Restart.HasFlag(RestartScope.Explorer)
                    ? "This change takes effect after Windows Explorer restarts. You can do that from Home > Quick actions."
                    : $"This change needs: {def.Restart}.",
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            }.ShowAsync();
        }
    }

    private static bool NeedsConfirm(TweakRisk r) => r is TweakRisk.Advanced or TweakRisk.SecurityTradeoff or TweakRisk.Destructive;

    private static string RiskLabel(TweakRisk r) => r switch
    {
        TweakRisk.SecurityTradeoff => "Security trade-off",
        _ => r.ToString(),
    };

    private TextBlock Muted(string text, double size, bool tertiary = false) => new()
    {
        Text = text,
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources[tertiary ? "TextFillColorTertiaryBrush" : "TextFillColorSecondaryBrush"],
    };

    private Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
