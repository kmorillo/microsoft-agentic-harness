using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;

namespace Application.AI.Common.Evaluation.Interfaces;

/// <summary>
/// Invokes the harness for one evaluation case and returns the result.
/// Abstracts the "how do we run the harness?" detail so the runner stays
/// transport-agnostic and unit-testable.
/// </summary>
/// <remarks>
/// <para>
/// The production implementation wraps <c>ExecuteAgentTurnCommand</c> via
/// <c>IMediator</c>, so the full MediatR pipeline (content safety, tool boundary,
/// audit, etc.) runs exactly as it would in a real agent turn. This is intentional —
/// eval should exercise the production code path, not a stripped-down shadow of it.
/// </para>
/// <para>
/// Test implementations can return canned <see cref="AgentInvocationResult"/>s for
/// runner unit tests without engaging the real agent stack.
/// </para>
/// </remarks>
public interface IAgentInvoker
{
    /// <summary>
    /// Invokes the harness for the given case.
    /// </summary>
    /// <param name="case">The case being evaluated. Carries input and optional invocation overrides.</param>
    /// <param name="runLevelOverrides">Run-wide invocation overrides (merged under case-level overrides).</param>
    /// <param name="forceDeterministic">When true, temperature is forced to 0 regardless of overrides.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The invocation result. Never null; <see cref="AgentInvocationResult.Success"/> indicates outcome.</returns>
    Task<AgentInvocationResult> InvokeAsync(
        EvalCase @case,
        IReadOnlyDictionary<string, string>? runLevelOverrides,
        bool forceDeterministic,
        CancellationToken cancellationToken);
}
