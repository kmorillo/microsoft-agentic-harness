namespace Domain.Common.Config.AI;

/// <summary>
/// Configuration for the Conversation-to-Knowledge Bridge.
/// Controls whether fact extraction runs, confidence thresholds, and timeout behavior.
/// Bound to <c>AppConfig:AI:KnowledgeBridge</c>.
/// </summary>
public sealed class KnowledgeBridgeConfig
{
    /// <summary>
    /// Master toggle for cross-session memory (both fact extraction via <c>KnowledgeExtractionBehavior</c>
    /// and recall via <c>KnowledgeMemoryContextProvider</c>). When false, both pass through with zero
    /// overhead — no LLM calls, no background tasks, no recalled context.
    /// </summary>
    /// <remarks>
    /// <strong>Opt-in by default.</strong> Memory is scoped per user/tenant via <c>IKnowledgeScope</c>,
    /// but that scope is only populated when the host calls <c>KnowledgeScopeAccessor.SetScope(...)</c>
    /// from its authenticated request context. Until that wiring exists, all conversations share a single
    /// default tenant, so enabling memory in a multi-user deployment would let one user's remembered facts
    /// surface for another. Leave this <see langword="false"/> until per-user scope is wired (or for a
    /// single-tenant deployment, set it <see langword="true"/> deliberately).
    /// </remarks>
    public bool Enabled { get; set; } = false;

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
