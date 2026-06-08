using Domain.AI.Iac;
using Domain.Common;

namespace Application.AI.Common.Interfaces.Iac;

/// <summary>
/// Backend-neutral infrastructure-as-code surface: scaffold a module, validate +
/// plan it, and security-scan it. The load-bearing abstraction behind the IaC
/// skill pack (PR-10) — Terraform and Bicep ship with parity, and consumers add
/// a third backend by implementing this interface and registering it under a new
/// keyed-DI key.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are registered as keyed singletons under the backend's
/// canonical key (<see cref="IacBackendKeys"/>: <c>"terraform"</c> / <c>"bicep"</c>),
/// so the validator and tools resolve the right backend by the
/// <c>IacDeploymentTarget.Backend</c> string.
/// </para>
/// <para>
/// <see cref="PlanAsync"/> and <see cref="ScanAsync"/> shell out to the backend
/// CLIs (terraform / bicep / checkov / tfsec / arm-ttk) <b>inside the PR-3
/// sandbox</b> with the egress allowlist scoped to the relevant registries — the
/// implementation never spawns processes on the host directly. The skill never
/// deploys: there is intentionally no <c>ApplyAsync</c> on this surface.
/// </para>
/// </remarks>
public interface IIacGenerator
{
    /// <summary>The backend this generator implements. Matches its keyed-DI registration key.</summary>
    IacBackend Backend { get; }

    /// <summary>
    /// Scaffolds a starter module for the requested resource. Deterministic
    /// templating — not an LLM call. The caller submits the result as a
    /// <c>ChangeProposal</c>.
    /// </summary>
    /// <param name="request">The typed scaffold request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="Result{T}.Success"/> with the generated files, or <see cref="Result{T}.Fail(string[])"/> with a stable <c>iac.*</c> code.</returns>
    Task<Result<IacGenerationResult>> GenerateAsync(IacGenerationRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Validates and plans the module at <paramref name="moduleDirectory"/> by
    /// running the backend's validate/plan CLI inside the sandbox.
    /// </summary>
    /// <param name="moduleDirectory">The sandbox-rooted directory containing the module.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="Result{T}.Success"/> with the plan outcome, or <see cref="Result{T}.Fail(string[])"/> with a stable <c>iac.*</c> code.</returns>
    Task<Result<IacPlanResult>> PlanAsync(string moduleDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Security-scans the module at <paramref name="moduleDirectory"/> by running
    /// the backend's scanners (Checkov + tfsec / ARM-TTK + Checkov) inside the
    /// sandbox and normalising their findings.
    /// </summary>
    /// <param name="moduleDirectory">The sandbox-rooted directory containing the module.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see cref="Result{T}.Success"/> with the scan outcome, or <see cref="Result{T}.Fail(string[])"/> with a stable <c>iac.*</c> code.</returns>
    Task<Result<IacScanResult>> ScanAsync(string moduleDirectory, CancellationToken cancellationToken);
}
