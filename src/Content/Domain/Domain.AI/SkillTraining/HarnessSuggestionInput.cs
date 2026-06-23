namespace Domain.AI.SkillTraining;

/// <summary>
/// The signal a <c>IHarnessChangeSuggester</c> reflects on when deciding whether to propose a bounded
/// harness configuration change at the end of a training run.
/// </summary>
/// <remarks>
/// The loop hands the suggester the run's rollout outcomes — the evidence a config-dial suggestion is
/// grounded in (e.g. a high proportion of rollouts that failed on transient tool errors motivates
/// suggesting a higher retry count). The suggester reads any current configuration values it needs
/// directly from its own injected options; the loop does not pass live config through, keeping this
/// input free of any dependency on the concrete config POCOs.
/// </remarks>
public sealed record HarnessSuggestionInput
{
    /// <summary>Identifier of the skill whose run produced these rollouts. Provenance / audit context.</summary>
    public required string SkillId { get; init; }

    /// <summary>
    /// The run's rollout outcomes — the failure/success signal a suggestion is reasoned from. The
    /// orchestrator passes the last evaluated (val) rollouts; may be empty if the run produced none.
    /// </summary>
    public required IReadOnlyList<RolloutResult> Rollouts { get; init; }
}
