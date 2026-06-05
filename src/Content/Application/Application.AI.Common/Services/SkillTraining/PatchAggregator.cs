using Application.AI.Common.Interfaces.SkillTraining;
using Domain.AI.SkillTraining;

namespace Application.AI.Common.Services.SkillTraining;

/// <summary>
/// Pure aggregator that deduplicates edits by ordinal (Op, Target, Content) equality
/// and sums their <see cref="Edit.SupportCount"/>.
/// </summary>
public sealed class PatchAggregator : IPatchAggregator
{
    /// <inheritdoc />
    public Patch Aggregate(IReadOnlyList<Patch> patches)
    {
        ArgumentNullException.ThrowIfNull(patches);

        // Preserve first-seen ordering so callers get deterministic output across runs.
        var ordered = new List<Edit>();
        var indexByKey = new Dictionary<(EditOp Op, string Target, string Content), int>();
        var reasonings = new List<string>();

        foreach (var patch in patches)
        {
            if (patch is null) continue;
            if (!string.IsNullOrWhiteSpace(patch.Reasoning)) reasonings.Add(patch.Reasoning);

            foreach (var edit in patch.Edits)
            {
                // Normalize trailing whitespace on both Target and Content so equivalent edits
                // that differ only by a stray "\n" or trailing space merge correctly. We don't
                // mutate the stored Edit — only the equivalence key — so PatchApplier still
                // sees the original Target verbatim.
                var key = (edit.Op, NormalizeKey(edit.Target), NormalizeKey(edit.Content));
                if (indexByKey.TryGetValue(key, out var idx))
                {
                    var existing = ordered[idx];
                    var supportSum = (existing.SupportCount ?? 1) + (edit.SupportCount ?? 1);
                    ordered[idx] = existing with
                    {
                        SupportCount = supportSum,
                        MergeLevel = Math.Max(existing.MergeLevel ?? 0, edit.MergeLevel ?? 0) + 1
                    };
                }
                else
                {
                    indexByKey[key] = ordered.Count;
                    ordered.Add(edit with
                    {
                        SupportCount = edit.SupportCount ?? 1,
                        MergeLevel = (edit.MergeLevel ?? 0)
                    });
                }
            }
        }

        return new Patch
        {
            Edits = ordered,
            Reasoning = string.Join("\n---\n", reasonings)
        };
    }

    /// <summary>
    /// Trim trailing whitespace for the equivalence key — leading whitespace can be
    /// semantically meaningful (e.g. markdown list indentation) and is preserved.
    /// </summary>
    private static string NormalizeKey(string s) => s.TrimEnd();
}
