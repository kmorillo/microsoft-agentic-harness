namespace Domain.AI.SkillTraining;

/// <summary>
/// Acceptance policy the skill-training gate applies when deciding whether to promote a
/// candidate skill into the lineage.
/// </summary>
/// <remarks>
/// <para>
/// The two modes encode genuinely different optimization objectives, not a cosmetic toggle over
/// the same goal — which is why they are an explicit, configured choice rather than an inferred one:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <see cref="StrictImprovementHeldOut"/> maximizes the held-out (validation) score alone. A
/// candidate is accepted whenever it strictly beats the current held-out score, even if it
/// regresses on the held-in tasks the proposer reflected on. This is the original SkillOpt gate.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="TwoSplitNonRegression"/> additionally requires non-regression on the held-in split,
/// porting the acceptance rule from Self-Harness (arXiv 2606.09498). It rejects candidates that
/// trade one split off against the other, guarding against edits that win on validation by chance
/// while quietly breaking behavior that previously worked. This is the safer default.
/// </description>
/// </item>
/// </list>
/// </remarks>
public enum GateMode
{
    /// <summary>
    /// Accept iff the candidate strictly beats the current held-out score. Held-in scores are
    /// ignored. Original single-split SkillOpt behavior.
    /// </summary>
    StrictImprovementHeldOut = 0,

    /// <summary>
    /// Accept iff the candidate does not regress on either split and strictly improves at least
    /// one: <c>Δ_in ≥ 0 ∧ Δ_ho ≥ 0 ∧ max(Δ_in, Δ_ho) &gt; 0</c>. Safer default; rejects proposals
    /// that only trade one split against the other.
    /// </summary>
    TwoSplitNonRegression = 1
}
