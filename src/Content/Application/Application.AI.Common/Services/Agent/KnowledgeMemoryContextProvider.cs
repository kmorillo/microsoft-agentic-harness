using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.KnowledgeGraph;
using Domain.AI.KnowledgeGraph.Models;
using Domain.Common.Config;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.AI.Common.Services.Agent;

/// <summary>
/// An <see cref="AIContextProvider"/> that recalls relevant cross-session memories for the current
/// user turn and injects them into the agent's instructions before the model is invoked. This is the
/// read half of the knowledge-memory loop; the write half (post-turn fact extraction) is handled by
/// <c>KnowledgeExtractionBehavior</c>.
/// </summary>
/// <remarks>
/// <para>
/// Agents are cached as singletons (see <c>IAgentConversationCache</c>), so this provider is
/// long-lived and shared across requests and tenants. It therefore <strong>must not capture</strong>
/// the scoped, tenant-aware <see cref="IKnowledgeMemory"/>. Instead it resolves it per invocation
/// from the current request scope exposed by <see cref="IAmbientRequestScope"/>, guaranteeing each
/// turn recalls against the correct user/tenant. When no request scope is established, recall is
/// skipped (the agent simply runs without recalled context).
/// </para>
/// <para>
/// Recall failures are swallowed: memory is an enhancement, never a hard dependency of a turn.
/// </para>
/// </remarks>
public sealed class KnowledgeMemoryContextProvider : AIContextProvider
{
    private const int MaxRecallResults = 5;

    private readonly IAmbientRequestScope _ambientScope;
    private readonly IOptionsMonitor<AppConfig> _appConfig;
    private readonly ILogger<KnowledgeMemoryContextProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeMemoryContextProvider"/> class.
    /// </summary>
    /// <param name="ambientScope">Bridge to the current request's service scope.</param>
    /// <param name="appConfig">Application configuration; recall is gated live on <c>AI.KnowledgeBridge.Enabled</c>
    /// so a hot config change takes effect without evicting cached agents.</param>
    /// <param name="logger">Logger for recall diagnostics.</param>
    public KnowledgeMemoryContextProvider(
        IAmbientRequestScope ambientScope,
        IOptionsMonitor<AppConfig> appConfig,
        ILogger<KnowledgeMemoryContextProvider> logger)
        : base(
            provideInputMessageFilter: messages => messages,
            storeInputRequestMessageFilter: messages => messages,
            storeInputResponseMessageFilter: messages => messages)
    {
        _ambientScope = ambientScope;
        _appConfig = appConfig;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override ValueTask<AIContext> ProvideAIContextAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
        => RecallAndInjectAsync(context.AIContext, cancellationToken);

    /// <summary>
    /// Core recall logic, decoupled from <see cref="InvokingContext"/> for testability. Resolves
    /// scoped memory from the current request scope, recalls facts relevant to the latest user
    /// message, and returns an <see cref="AIContext"/> with those facts appended to the instructions.
    /// Returns <paramref name="inputContext"/> unchanged when recall is disabled, unavailable, or empty.
    /// </summary>
    public async ValueTask<AIContext> RecallAndInjectAsync(
        AIContext inputContext,
        CancellationToken cancellationToken = default)
    {
        if (!_appConfig.CurrentValue.AI.KnowledgeBridge.Enabled)
            return inputContext;

        var query = ExtractQuery(inputContext);
        if (string.IsNullOrWhiteSpace(query))
            return inputContext;

        IReadOnlyList<GraphNode> recalled;
        try
        {
            // Resolve tenant-aware memory from the CURRENT request scope — never captured (see remarks).
            // Resolution is inside the try so a disposed/absent scope degrades to "no recall" rather
            // than crashing the turn (memory is an enhancement, never a hard dependency).
            var memory = _ambientScope.Current?.GetService<IKnowledgeMemory>();
            if (memory is null)
                return inputContext;

            recalled = await memory.RecallAsync(query, MaxRecallResults, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Knowledge recall failed; proceeding without recalled context");
            return inputContext;
        }

        if (recalled.Count == 0)
            return inputContext;

        var block = FormatRecalledFacts(recalled);
        var instructions = string.IsNullOrWhiteSpace(inputContext.Instructions)
            ? block
            : inputContext.Instructions + "\n\n" + block;

        _logger.LogDebug("Injected {Count} recalled fact(s) into agent context", recalled.Count);

        return new AIContext
        {
            Instructions = instructions,
            Messages = inputContext.Messages,
            Tools = inputContext.Tools
        };
    }

    private static string? ExtractQuery(AIContext aiContext)
        => aiContext.Messages?.LastOrDefault(m => m.Role == ChatRole.User)?.Text;

    private static string FormatRecalledFacts(IReadOnlyList<GraphNode> nodes)
    {
        var lines = nodes.Select(n =>
            n.Properties.TryGetValue("content", out var content) && !string.IsNullOrWhiteSpace(content)
                ? content
                : n.Name);

        return "## Relevant remembered context\n" +
            string.Join("\n", lines.Select(line => $"- {line}"));
    }
}
