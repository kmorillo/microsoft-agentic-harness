using Domain.AI.SkillTraining;

namespace Application.AI.Common.Interfaces.SkillTraining;

/// <summary>
/// Ranks edits in an aggregated patch and keeps the top <c>k</c> — gradient-clipping for
/// the skill-training loop.
/// </summary>
/// <remarks>
/// <para>
/// Port of SkillOpt's <c>optimizer/clip.py rank_and_select</c>: even after aggregation, an
/// optimizer may emit more edits per step than the LR budget allows. This component decides
/// which edits make the cut.
/// </para>
/// <para>
/// Implementations are pure. The default <see cref="Application.AI.Common.Services.SkillTraining.TopKEditSelector"/>
/// ranks by <see cref="Edit.SupportCount"/> descending, with <see cref="Edit.MergeLevel"/> as
/// a tie-breaker. Future implementations may use embedding-similarity to skill keywords or
/// learned ranking; the interface stays stable.
/// </para>
/// </remarks>
public interface IEditSelector
{
    /// <summary>
    /// Ranks <paramref name="patch"/>'s edits and returns a new patch keeping at most
    /// <paramref name="k"/> of them.
    /// </summary>
    /// <param name="patch">The aggregated patch to clip.</param>
    /// <param name="k">Maximum number of edits to keep (must be ≥ 0).</param>
    /// <returns>A patch with <c>min(patch.Edits.Count, k)</c> edits, ranked highest-first.</returns>
    Patch SelectTopK(Patch patch, int k);
}
