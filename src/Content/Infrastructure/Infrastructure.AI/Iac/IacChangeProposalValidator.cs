using Application.AI.Common.Interfaces.Changes;
using Application.AI.Common.Interfaces.Iac;
using Domain.AI.Changes;
using Domain.AI.Iac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Iac;

/// <summary>
/// The IaC <see cref="IChangeProposalValidator"/> run by the <c>SelfValidationGate</c>
/// for <see cref="ChangeTargetKind.IacDeployment"/> proposals (PR-10). Validates +
/// plans the module and security-scans it via the backend's <see cref="IIacGenerator"/>,
/// failing the proposal when the plan errors, when the plan shows destructive
/// changes (the deploy guard refuses unexpected resource replacement/deletion), or
/// when the scan does not pass policy.
/// </summary>
/// <remarks>
/// <para>
/// Registered under the <see cref="ChangeTargetKind.IacDeployment"/> keyed-DI key,
/// replacing the fail-loud <c>NotConfiguredValidator</c> placeholder. A proposal
/// whose target is not an <see cref="IacDeploymentTarget"/> is not this validator's
/// concern — it returns <see cref="GateResult.Pass"/> so a mis-routed proposal is
/// neither blocked nor silently approved by IaC logic.
/// </para>
/// <para>
/// The backend is resolved by <see cref="IacDeploymentTarget.Backend"/> via
/// <see cref="ServiceProviderKeyedServiceExtensions.GetKeyedService{T}"/>; an
/// unknown or unregistered backend fails the proposal with <c>iac.backend_not_registered</c>
/// rather than throwing — a typo in a target's backend string must not crash the
/// gate pipeline.
/// </para>
/// </remarks>
public sealed class IacChangeProposalValidator : IChangeProposalValidator
{
    /// <summary>The keyed-DI key this validator registers under for the IaC target kind.</summary>
    public const string ValidatorKey = "iac_plan_scan";

    private readonly IServiceProvider _services;
    private readonly ILogger<IacChangeProposalValidator> _logger;

    /// <summary>Initialises a new <see cref="IacChangeProposalValidator"/>.</summary>
    /// <param name="services">The service provider used to resolve the backend <see cref="IIacGenerator"/> by key.</param>
    /// <param name="logger">Structured logger.</param>
    public IacChangeProposalValidator(IServiceProvider services, ILogger<IacChangeProposalValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(logger);
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Key => ValidatorKey;

    /// <inheritdoc />
    public async Task<GateResult> ValidateAsync(
        ChangeProposal proposal,
        GateContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        if (proposal.Target is not IacDeploymentTarget target)
        {
            return GateResult.Pass("Not an IaC deployment target; iac_plan_scan does not apply.");
        }

        var generator = _services.GetKeyedService<IIacGenerator>(target.Backend);
        if (generator is null)
        {
            _logger.LogWarning(
                "No IIacGenerator registered for backend '{Backend}' (proposal {Proposal}).",
                target.Backend, proposal.Id);
            return GateResult.Fail($"iac.backend_not_registered: no IIacGenerator for backend '{target.Backend}'.");
        }

        var moduleDirectory = ResolveModuleDirectory(target.ModulePath);

        var planResult = await generator.PlanAsync(moduleDirectory, cancellationToken).ConfigureAwait(false);
        if (!planResult.IsSuccess || planResult.Value is null)
        {
            return GateResult.Fail($"iac.plan_failed: {string.Join("; ", planResult.Errors)}");
        }

        var plan = planResult.Value;
        if (!plan.Succeeded)
        {
            return GateResult.Fail($"iac.plan_invalid: {plan.Summary}");
        }

        if (plan.HasDestructiveChanges)
        {
            return GateResult.Fail($"iac.plan_destructive: plan includes destructive changes ({plan.Summary}).");
        }

        var scanResult = await generator.ScanAsync(moduleDirectory, cancellationToken).ConfigureAwait(false);
        if (!scanResult.IsSuccess || scanResult.Value is null)
        {
            return GateResult.Fail($"iac.scan_failed: {string.Join("; ", scanResult.Errors)}");
        }

        var scan = scanResult.Value;
        if (!scan.Passed)
        {
            return GateResult.Fail(
                $"iac.scan_blocked: {scan.Findings.Count} finding(s) at or above the blocking severity " +
                $"from {string.Join(", ", scan.ScannersRun)}.");
        }

        return GateResult.Pass(
            $"iac plan clean ({plan.Summary}); scan passed ({scan.Findings.Count} sub-blocking finding(s)).");
    }

    private static string ResolveModuleDirectory(string modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath))
        {
            return ".";
        }

        var directory = Path.GetDirectoryName(modulePath);
        return string.IsNullOrEmpty(directory) ? "." : directory;
    }
}
