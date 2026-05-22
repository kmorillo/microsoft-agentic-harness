using Domain.Common.Config.AI;

namespace Domain.AI.Routing.Models;

/// <summary>
/// Defines a model deployment tier with provider, cost, and rate limit metadata.
/// Tiers are ordered by <see cref="EstimatedCostPer1KTokens"/> ascending for escalation.
/// </summary>
public sealed record ModelTier
{
    /// <summary>Tier identifier (e.g., "economy", "standard", "premium").</summary>
    public required string Name { get; init; }

    /// <summary>Which AI provider hosts this tier's deployment.</summary>
    public required AIAgentFrameworkClientType ClientType { get; init; }

    /// <summary>Deployment name or model identifier for the provider.</summary>
    public required string DeploymentName { get; init; }

    /// <summary>Optional reference to a named fallback chain in ResilienceConfig.</summary>
    public string? FallbackChainName { get; init; }

    /// <summary>Rate limit for this tier (tokens per minute).</summary>
    public int MaxTokensPerMinute { get; init; }

    /// <summary>Estimated cost per 1K tokens for budget tracking and tier ordering.</summary>
    public decimal EstimatedCostPer1KTokens { get; init; }
}
