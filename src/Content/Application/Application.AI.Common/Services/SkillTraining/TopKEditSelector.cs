using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Default <see cref="IEditSelector"/> — ranks by <see cref="Edit.SupportCount"/> descending,
/// then by <see cref="Edit.MergeLevel"/> descending as a tie-breaker.
/// </summary>
/// <remarks>
/// Ties beyond both keys are broken by original insertion order (stable sort), so callers
/// receive deterministic output for identical input across runs and across machines.
/// </remarks>
public sealed class TopKEditSelector : IEditSelector
{
    /// <inheritdoc />
    public Patch SelectTopK(Patch patch, int k)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), k, "k must be ≥ 0");

        if (k == 0)
        {
            return patch with { Edits = [] };
        }

        // Single path for all sizes — keeps tie-break semantics identical regardless of
        // whether k clips the list or not, so cross-size snapshot tests remain stable.
        var ranked = patch.Edits
            .Select((edit, index) => (edit, index))
            .OrderByDescending(t => SortKey(t.edit))
            .ThenBy(t => t.index)
            .Take(k)
            .Select(t => t.edit)
            .ToList();

        return patch with { Edits = ranked };
    }

    private static (int Support, int Merge) SortKey(Edit e) =>
        (e.SupportCount ?? 0, e.MergeLevel ?? 0);
}
