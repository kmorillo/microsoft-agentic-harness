using Domain.AI.SkillTraining;

namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// Persists <see cref="SkillTrainingCheckpoint"/>s for a training run so the orchestrator can
/// resume after a crash, audit the trajectory of accepted skills, and roll back to a prior step.
/// </summary>
/// <remarks>
/// Mirrors the plan-state-store pattern: short-lived store calls, all operations addressable
/// by (RunId, Step). Implementations may be in-memory (default for tests / dev) or backed
/// by a durable store (Phase 4+ follow-up: EF Core SQLite migration).
/// </remarks>
public interface ISkillTrainingCheckpointStore
{
    /// <summary>Persists a checkpoint. Replaces any existing checkpoint at the same (RunId, Step).</summary>
    Task SaveAsync(SkillTrainingCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Loads a specific checkpoint by (RunId, Step), or null when none exists.</summary>
    Task<SkillTrainingCheckpoint?> GetAsync(string runId, int step, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the highest-scoring checkpoint for the run, or null when the run has none yet.
    /// Ties broken by latest <see cref="SkillTrainingCheckpoint.Step"/>.
    /// </summary>
    Task<SkillTrainingCheckpoint?> GetBestAsync(string runId, CancellationToken cancellationToken);

    /// <summary>Returns every checkpoint for the run, ordered by step ascending.</summary>
    Task<IReadOnlyList<SkillTrainingCheckpoint>> ListAsync(string runId, CancellationToken cancellationToken);
}
