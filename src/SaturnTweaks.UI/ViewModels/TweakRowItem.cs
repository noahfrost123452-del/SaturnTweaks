using CommunityToolkit.Mvvm.ComponentModel;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;
using SaturnTweaks.Engine.Tweaks;

namespace SaturnTweaks.UI.ViewModels;

/// <summary>One detected tweak plus a read-only preview of the change it would make.</summary>
public partial class TweakRowItem : ObservableObject
{
    public string TweakId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ShortDescription { get; set; } = "";
    public string DetailedExplanation { get; set; } = "";
    public string ExpectedBenefit { get; set; } = "";
    public string PotentialDisadvantages { get; set; } = "";
    public string Category { get; set; } = "";
    public TweakRisk Risk { get; set; }
    public bool IsApplicable { get; set; }
    public bool IsAlreadyConfigured { get; set; }
    public bool RequiresAdmin { get; set; }
    public bool RequiresRestart { get; set; }
    public string CurrentValue { get; set; } = "";
    public string ChangeLocation { get; set; } = "";
    public string ProposedChange { get; set; } = "";
    public string PreviewDisclaimer => "Preview only — no system setting has been changed.";

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isPreviewed;
    [ObservableProperty] private TweakResult? _lastResult;

    partial void OnLastResultChanged(TweakResult? value) => OnPropertyChanged(nameof(StatusText));

    public string RiskColor => Risk switch
    {
        TweakRisk.Safe => "#22C55E",
        TweakRisk.Low => "#84CC16",
        TweakRisk.Moderate => "#F59E0B",
        TweakRisk.High => "#F97316",
        TweakRisk.Critical => "#EF4444",
        _ => "#6B698A"
    };

    public string StatusText
    {
        get
        {
            if (!IsApplicable) return "Not compatible";
            if (IsAlreadyConfigured) return "Already set";
            if (LastResult == TweakResult.AppliedAndVerified) return "Applied ✓";
            if (LastResult == TweakResult.Failed) return "Failed ✗";
            if (LastResult == TweakResult.VerificationInconclusive) return "Verify manually";
            if (LastResult == TweakResult.RequiresRestart) return "Restart required";
            if (LastResult == TweakResult.DryRun) return "Previewed";
            return "Ready";
        }
    }

    public static async Task<TweakRowItem> CreateAsync(
        ITweakExecutor executor,
        SystemSnapshot snapshot,
        bool selected = false,
        CancellationToken cancellationToken = default)
    {
        var definition = executor.Definition;
        var applicable = await executor.IsApplicableAsync(snapshot, cancellationToken);
        var alreadyConfigured = applicable && await executor.IsAlreadyConfiguredAsync(snapshot, cancellationToken);
        var current = await executor.InspectCurrentStateAsync(snapshot, cancellationToken);

        string location;
        string proposed;
        if (executor is RegistryTweakBase registryTweak)
        {
            location = registryTweak.PreviewLocation;
            proposed = registryTweak.PreviewTarget;
        }
        else if (executor is PowerPlanTweak powerPlanTweak)
        {
            location = powerPlanTweak.PreviewLocation;
            proposed = powerPlanTweak.PreviewTarget;
        }
        else
        {
            location = "Windows configuration";
            proposed = "See the explanation above; this executor does not expose a raw target value.";
        }

        return new TweakRowItem
        {
            TweakId = definition.Id,
            DisplayName = definition.DisplayName,
            ShortDescription = definition.ShortDescription,
            DetailedExplanation = definition.DetailedExplanation,
            ExpectedBenefit = definition.ExpectedBenefit,
            PotentialDisadvantages = definition.PotentialDisadvantages,
            Category = definition.Category.ToString(),
            Risk = definition.RiskLevel,
            IsApplicable = applicable,
            IsAlreadyConfigured = alreadyConfigured,
            RequiresAdmin = definition.RequiresAdminPrivileges,
            RequiresRestart = definition.RequiresRestart,
            CurrentValue = current ?? "(not set / unavailable)",
            ChangeLocation = location,
            ProposedChange = proposed,
            IsSelected = selected && applicable && !alreadyConfigured
        };
    }
}
