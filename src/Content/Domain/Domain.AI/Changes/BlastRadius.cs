namespace Domain.AI.Changes;

/// <summary>
/// The estimated impact band of a <see cref="ChangeProposal"/>. Drives gate-pipeline
/// decisions: lower bands may auto-approve under autonomous tiers; higher bands always
/// require an <c>ApprovalGate</c> human nod even when policy and validation pass.
/// </summary>
/// <remarks>
/// <para>
/// The band is set by the submitter (the agent or the gate resolver) based on the target
/// type and the diff content. It is advisory at submission time and confirmed (or revised)
/// by the gate pipeline; the audit trail records both values.
/// </para>
/// <para>
/// The ordering is meaningful: every member's integer value is a strict upper bound of the
/// previous member, so range checks like <c>radius &gt;= BlastRadius.High</c> are valid.
/// </para>
/// </remarks>
public enum BlastRadius
{
    /// <summary>
    /// Cosmetic or comment-only change — no behavior alteration possible. Example:
    /// fixing a typo in a markdown file, reformatting whitespace.
    /// </summary>
    Trivial = 0,

    /// <summary>
    /// Touches non-production assets or scoped to a single non-critical module. Example:
    /// adding a unit test, updating an internal helper used by one caller.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Touches production code in a bounded module with regression test coverage. Example:
    /// changing an internal service method's body without touching its signature.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Touches a cross-cutting concern, a public contract, or production data. Example:
    /// modifying a database migration, changing an API DTO, editing IaC for production.
    /// </summary>
    High = 3,

    /// <summary>
    /// Affects production availability, security boundaries, or compliance controls.
    /// Example: changes to authentication, secret rotation flows, prod IAM policies,
    /// or anything touching the merge-pipeline itself. Always requires human approval.
    /// </summary>
    Critical = 4
}
