using Domain.Common.Config.AI;

namespace Domain.Common.Config.AI.Routing;

/// <summary>
/// Configuration for the unified model router.
/// Consolidates model tiering, complexity routing, escalation, and heuristic thresholds.
/// Bound to <c>AppConfig:AI:ModelRouting</c>.
/// </summary>
public sealed class ModelRoutingConfig
{
    /// <summary>Master toggle for complexity-aware routing. When false, all routing falls back to DefaultTier.</summary>
    public bool Enabled { get; set; }

    /// <summary>Tier name to use when routing is disabled or classification fails.</summary>
    public string DefaultTier { get; set; } = "standard";

    /// <summary>Minimum heuristic confidence to accept without LLM fallback (0.0–1.0).</summary>
    public double HeuristicConfidenceThreshold { get; set; } = 0.8;

    /// <summary>Available model tiers ordered by cost ascending.</summary>
    public ModelRoutingTierConfig[] Tiers { get; set; } = [];

    /// <summary>Per-operation tier overrides for RAG pipeline steps.</summary>
    public Dictionary<string, string> OperationOverrides { get; set; } = new();

    /// <summary>Auto-escalation settings.</summary>
    public EscalationConfig Escalation { get; set; } = new();

    /// <summary>Heuristic classification signal thresholds.</summary>
    public HeuristicThresholdsConfig HeuristicThresholds { get; set; } = new();

    /// <summary>Default retrieval parameters per complexity tier (for RetrievalDecisionGate).</summary>
    public RetrievalDefaultsConfig RetrievalDefaults { get; set; } = new();
}

/// <summary>Defines a single model tier: provider, deployment, cost, and rate limit.</summary>
public sealed class ModelRoutingTierConfig
{
    /// <summary>Tier identifier (e.g., "economy", "standard", "premium").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>AI provider for this tier.</summary>
    public AIAgentFrameworkClientType ClientType { get; set; } = AIAgentFrameworkClientType.AzureOpenAI;

    /// <summary>Deployment name or model identifier.</summary>
    public string DeploymentName { get; set; } = string.Empty;

    /// <summary>Optional reference to a named fallback chain in ResilienceConfig.</summary>
    public string? FallbackChainName { get; set; }

    /// <summary>Tokens-per-minute rate limit.</summary>
    public int MaxTokensPerMinute { get; set; }

    /// <summary>Estimated cost per 1K tokens (used for tier ordering and budget tracking).</summary>
    public decimal EstimatedCostPer1KTokens { get; set; }
}

/// <summary>Auto-escalation configuration.</summary>
public sealed class EscalationConfig
{
    /// <summary>Enable auto-escalation on quality signals.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Block escalation when session spend exceeds this % of budget.</summary>
    public int BudgetCeilingPercent { get; set; } = 80;

    /// <summary>Turns to stay at escalated tier before attempting downshift.</summary>
    public int CooldownTurns { get; set; } = 2;
}

/// <summary>Thresholds for the heuristic complexity classifier.</summary>
public sealed class HeuristicThresholdsConfig
{
    /// <summary>Messages shorter than this are Trivial candidates.</summary>
    public int TrivialMaxLength { get; set; } = 50;

    /// <summary>Messages shorter than this are Simple candidates.</summary>
    public int SimpleMaxLength { get; set; } = 200;

    /// <summary>Messages shorter than this are Moderate candidates.</summary>
    public int ModerateMaxLength { get; set; } = 1000;

    /// <summary>Available tool count above which the task is Complex.</summary>
    public int ComplexMinToolCount { get; set; } = 8;

    /// <summary>Keywords that signal Complex tasks.</summary>
    public string[] ComplexKeywords { get; set; } = ["refactor", "design", "plan", "architect", "migrate", "rewrite"];

    /// <summary>Keywords that signal Trivial tasks (greetings, acknowledgments).</summary>
    public string[] TrivialKeywords { get; set; } = ["hi", "hello", "thanks", "ok", "yes", "no"];
}

/// <summary>Default retrieval parameters per complexity tier (used by RetrievalDecisionGate).</summary>
public sealed class RetrievalDefaultsConfig
{
    /// <summary>Minimum confidence to accept a complexity classification for retrieval decisions.</summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>TopK for Simple queries.</summary>
    public int SimpleTopK { get; set; } = 5;

    /// <summary>TopK for Complex queries.</summary>
    public int ComplexTopK { get; set; } = 15;

    /// <summary>Skip reranking for Simple queries.</summary>
    public bool SkipRerankForSimple { get; set; } = true;

    /// <summary>Skip CRAG evaluation for Simple queries.</summary>
    public bool SkipCragForSimple { get; set; } = true;
}
