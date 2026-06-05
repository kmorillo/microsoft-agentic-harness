namespace Domain.AI.SkillTraining;

/// <summary>
/// Strongly-typed knobs for one training run. Held as a config record (not on the command
/// itself) so it can be loaded from <c>appsettings</c>, serialized into a checkpoint, and
/// passed verbatim into resume operations.
/// </summary>
public sealed record TrainSkillConfig
{
    /// <summary>Number of epochs to run (≥ 1).</summary>
    public int Epochs { get; init; } = 3;

    /// <summary>Steps per epoch (≥ 1).</summary>
    public int StepsPerEpoch { get; init; } = 5;

    /// <summary>LR at step 0 (≥ 1). Edits applied per step is bounded by the scheduler.</summary>
    public int LrStart { get; init; } = 8;

    /// <summary>Floor LR (≥ 1 and ≤ <see cref="LrStart"/>).</summary>
    public int LrMin { get; init; } = 1;

    /// <summary>Scheduler key: <c>"cosine"</c> | <c>"linear"</c> | <c>"constant"</c>.</summary>
    public string LrScheduler { get; init; } = "cosine";

    /// <summary>Train batch size per rollout step.</summary>
    public int TrainBatchSize { get; init; } = 8;

    /// <summary>Val batch size for the gate.</summary>
    public int ValBatchSize { get; init; } = 16;

    /// <summary>Which metric the gate compares on.</summary>
    public GateMetric GateMetric { get; init; } = GateMetric.Hard;

    /// <summary>Soft weight when <see cref="GateMetric"/> is Mixed; [0, 1].</summary>
    public double MixedWeight { get; init; } = 0.5;

    /// <summary>
    /// Early stop after this many consecutive <see cref="GateAction.Reject"/>s (≥ 1). The
    /// SkillOpt paper's default is 2–4 epochs worth of steps; 6 is a safe baseline.
    /// </summary>
    public int Patience { get; init; } = 6;

    /// <summary>Whether to run the SlowUpdate longitudinal pass at each epoch boundary.</summary>
    public bool UseSlowUpdate { get; init; } = true;

    /// <summary>Whether to maintain a cross-epoch meta-skill memory via <c>IKnowledgeMemory</c>.</summary>
    public bool UseMetaSkill { get; init; } = true;

    /// <summary>Seed for sampling the train/val batches; 0 = nondeterministic.</summary>
    public int Seed { get; init; }
}
