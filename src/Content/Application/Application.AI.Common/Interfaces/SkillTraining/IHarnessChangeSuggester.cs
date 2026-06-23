using Domain.AI.SkillTraining;

namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// The optional "harness-change advisor" role of the skill-training loop: reflects on a run's rollout
/// signal and proposes bounded, never-applied <see cref="HarnessChangeSuggestion"/>s for non-prose
/// harness settings (Self-Harness Phase 2 Step 2).
/// </summary>
/// <remarks>
/// <para>
/// This seam exists because some harness knobs are scalar configuration, not prose — the retry dial
/// (<c>RetryConfig.MaxAttempts</c>) being the canonical case. A scalar change cannot flow through the
/// rollout-scoring accept-gate, and Phase 2 deliberately refuses to auto-mutate live resilience config,
/// so the loop's only sanctioned move is to <em>suggest</em>. The loop bounds-checks every returned
/// suggestion against a code-owned <c>ConfigSurfaceConstraint</c>, audits it, and surfaces the survivors
/// on the run result. Nothing a suggester returns ever mutates running configuration.
/// </para>
/// <para>
/// Unlike <see cref="IPatchProposer"/> and <see cref="IRolloutRunner"/> — which are <em>required</em>
/// for the loop to function and whose defaults throw — this seam is <em>optional and advisory</em>. Its
/// default implementation (<c>NoHarnessChangeSuggester</c>) returns no suggestions, and the loop only
/// calls it at all when a run opts in via <c>TrainSkillConfig.EmitHarnessChangeSuggestions</c>. A real
/// implementation (typically agent-backed, reading current config from its own injected options) is a
/// template consumer's concern.
/// </para>
/// </remarks>
public interface IHarnessChangeSuggester
{
    /// <summary>
    /// Reflects on the run's rollout signal and returns zero or more bounded harness-change suggestions.
    /// </summary>
    /// <param name="input">The run's rollout outcomes and provenance context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Candidate suggestions — possibly empty. The loop validates each against the code-owned constraint
    /// before auditing or surfacing it; an implementation need not pre-filter, though it should only
    /// propose changes it believes are warranted.
    /// </returns>
    Task<IReadOnlyList<HarnessChangeSuggestion>> SuggestAsync(
        HarnessSuggestionInput input,
        CancellationToken cancellationToken);
}
