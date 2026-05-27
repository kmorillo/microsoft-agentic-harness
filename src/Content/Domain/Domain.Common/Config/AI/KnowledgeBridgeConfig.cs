namespace Domain.Common.Config.AI;

/// <summary>
/// Configuration for the Conversation-to-Knowledge Bridge.
/// Controls whether fact extraction runs, confidence thresholds, and timeout behavior.
/// Bound to <c>AppConfig:AI:KnowledgeBridge</c>.
/// </summary>
public sealed class KnowledgeBridgeConfig
{
    /// <summary>
    /// Master toggle. When false, <c>KnowledgeExtractionBehavior</c> passes through
    /// with zero overhead — no LLM calls, no background tasks.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum LLM confidence (0.0–1.0) required to persist an extracted fact.
    /// Facts below this threshold are silently discarded.
    /// </summary>
    public double MinConfidence { get; set; } = 0.7;

    /// <summary>
    /// Hard timeout in seconds for the background extraction LLM call.
    /// Prevents runaway requests from consuming resources indefinitely.
    /// </summary>
    public int ExtractionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Operation name passed to <c>IModelRouter.RouteOperationAsync</c> to resolve
    /// the extraction LLM client. Should map to the economy tier in
    /// <c>ModelRoutingConfig.OperationOverrides</c>.
    /// </summary>
    public string RoutingOperationName { get; set; } = "fact_extraction";
}
