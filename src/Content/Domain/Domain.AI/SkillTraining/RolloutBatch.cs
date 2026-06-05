namespace Domain.AI.SkillTraining;

/// <summary>
/// The split-aware request the orchestrator hands to an <c>IRolloutRunner</c> at each
/// rollout call.
/// </summary>
/// <remarks>
/// Mirrors SkillOpt's <c>BatchSpec</c> with .NET-idiomatic naming. The runner uses
/// <see cref="Split"/> to pick the right item pool ("train" for rollout steps, "val" for
/// gate evaluation), <see cref="ItemIds"/> when the orchestrator wants to pin an exact
/// set (e.g. paired longitudinal comparison), and <see cref="BatchSize"/>/<see cref="Seed"/>
/// when the runner is free to sample.
/// </remarks>
public sealed record RolloutBatch
{
    /// <summary>Dataset split this batch targets. Free-form string; conventional values are
    /// <c>"train"</c>, <c>"val"</c>, <c>"test"</c>.</summary>
    public required string Split { get; init; }

    /// <summary>Random seed for sampling, when applicable.</summary>
    public int Seed { get; init; }

    /// <summary>Maximum batch size when the runner is sampling. Ignored when
    /// <see cref="ItemIds"/> is non-empty.</summary>
    public int BatchSize { get; init; } = 8;

    /// <summary>
    /// Specific item ids to roll out. When non-empty, the runner must produce exactly these
    /// items in order; when empty, the runner samples up to <see cref="BatchSize"/> items
    /// from the split.
    /// </summary>
    public IReadOnlyList<string> ItemIds { get; init; } = [];
}
