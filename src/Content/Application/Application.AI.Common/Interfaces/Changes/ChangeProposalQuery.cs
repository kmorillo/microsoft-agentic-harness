using Domain.AI.Changes;

namespace Application.AI.Common.Interfaces.Changes;

/// <summary>
/// Filter criteria for <see cref="IChangeProposalStore.ListAsync"/>. All filters
/// combine with AND; null filters match everything in that dimension.
/// </summary>
/// <remarks>
/// <para>
/// Designed as an immutable record so handlers can build queries declaratively
/// (<c>new ChangeProposalQuery { Status = ChangeProposalStatus.Validating }</c>)
/// and stores can pattern-match without copying mutable filter state.
/// </para>
/// </remarks>
public sealed record ChangeProposalQuery
{
    /// <summary>Filter to proposals in this status. Null matches all statuses.</summary>
    public ChangeProposalStatus? Status { get; init; }

    /// <summary>Filter to proposals submitted by this agent id. Null matches all agents.</summary>
    public string? SubmittedByAgentId { get; init; }

    /// <summary>Filter to proposals at or above this blast radius. Null matches all radii.</summary>
    public BlastRadius? MinimumBlastRadius { get; init; }

    /// <summary>Filter to proposals whose target has this kind. Null matches all kinds.</summary>
    public ChangeTargetKind? TargetKind { get; init; }

    /// <summary>Maximum number of results to return. Stores honor this as an upper bound.</summary>
    public int MaxResults { get; init; } = 100;
}
