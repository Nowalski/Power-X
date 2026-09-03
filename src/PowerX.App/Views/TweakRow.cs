using Microsoft.UI.Xaml;
using PowerX.Core.Tweaks;

namespace PowerX.App.Views;

public sealed class TweakRow(TweakStatus status)
{
    public TweakDefinition Def { get; } = status.Definition;
    public string Id => Def.Id;
    public string Name => Def.Name;
    public string What => Def.WhatItDoes;
    public string Downside => $"Downside: {Def.Downside}" +
        (Def.Restart == RestartScope.None ? "" : $"  ·  Restart: {Def.Restart}");
    public string RiskLabel => Def.Risk switch
    {
        TweakRisk.SecurityTradeoff => "Security trade-off",
        _ => Def.Risk.ToString(),
    };
    public string Note => status.Note ?? "";
    public Visibility NoteVisibility => string.IsNullOrEmpty(status.Note) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility RecommendedVisibility => Def.Recommended ? Visibility.Visible : Visibility.Collapsed;

    public bool CanToggle => status.State is TweakState.Applied or TweakState.Default;
    public bool IsOn { get; set; } = status.State == TweakState.Applied;
}
