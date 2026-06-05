using Domain.AI.SkillTraining;

namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// Merges a batch of per-rollout patches into a single patch, deduplicating equivalent edits
/// and accumulating their <see cref="Edit.SupportCount"/>.
/// </summary>
/// <remarks>
/// <para>
/// Port of SkillOpt's <c>gradient/aggregate.py merge_patches</c>: when many rollouts produce
/// substantially the same edit, the trainer should not apply it many times; instead it
/// should apply it once with a higher support count, which is then used by selection to
/// prioritize high-support edits.
/// </para>
/// <para>
/// "Equivalent" means same <see cref="Edit.Op"/>, same <see cref="Edit.Target"/>, and same
/// <see cref="Edit.Content"/> by ordinal string equality. Future implementations may
/// loosen this with embedding-cosine matching; the interface is stable.
/// </para>
/// </remarks>
public interface IPatchAggregator
{
    /// <summary>
    /// Merges the given patches into a single aggregated patch.
    /// </summary>
    /// <param name="patches">The per-rollout patches.</param>
    /// <returns>A single patch whose <see cref="Patch.Edits"/> are the deduplicated union
    /// of the inputs' edits, with <see cref="Edit.SupportCount"/> summed across duplicates.</returns>
    Patch Aggregate(IReadOnlyList<Patch> patches);
}
