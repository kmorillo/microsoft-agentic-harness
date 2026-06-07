using Application.AI.Common.Interfaces.Changes;
using Domain.AI.Changes;

namespace Infrastructure.AI.Changes.Gates;

/// <summary>
/// Fail-loud default <see cref="IChangeProposalValidator"/> wired into DI when
/// no real validator is registered for a target kind. Throws on first use so a
/// consumer that turns on <c>ChangesConfig.Enabled</c> without wiring real
/// validators sees the misconfiguration at the first proposal — not silently
/// passing every validation gate.
/// </summary>
/// <remarks>
/// Same pattern as <c>NotConfiguredPatchProposer</c> and
/// <c>NotConfiguredRolloutRunner</c> in the SkillTraining subsystem: ship a
/// type-safe placeholder that throws with a directive message rather than
/// quietly returning a Pass / Fail / no-op. The exception message names the
/// keyed-DI registration the consumer must add.
/// </remarks>
public sealed class NotConfiguredValidator : IChangeProposalValidator
{
    /// <summary>Initializes a <see cref="NotConfiguredValidator"/>.</summary>
    /// <param name="targetKind">The target kind this default placeholder was registered under (surfaces in the exception message).</param>
    public NotConfiguredValidator(ChangeTargetKind targetKind)
    {
        TargetKind = targetKind;
    }

    /// <summary>The target kind this placeholder covers.</summary>
    public ChangeTargetKind TargetKind { get; }

    /// <inheritdoc />
    public string Key => $"not_configured.{TargetKind}";

    /// <inheritdoc />
    public Task<GateResult> ValidateAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            $"No IChangeProposalValidator is registered for target kind '{TargetKind}'. " +
            $"Register one via services.AddKeyedSingleton<IChangeProposalValidator>(ChangeTargetKind.{TargetKind}, ...) " +
            "before enabling AppConfig.AI.Changes.Enabled. " +
            "PR-8 (Workspace skill), PR-9 (GitOps skill), and PR-10 (IaC skill) ship real validators; " +
            "until then, register an in-process fake or disable the Changes pipeline.");
    }
}
