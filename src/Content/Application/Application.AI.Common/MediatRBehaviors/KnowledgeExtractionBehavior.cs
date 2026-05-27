using Application.AI.Common.Interfaces.KnowledgeGraph;
using Application.AI.Common.Interfaces.MediatR;
using Domain.Common.Config.AI;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.AI.Common.MediatRBehaviors;

/// <summary>
/// Post-turn pipeline behavior that extracts notable facts from agent conversations
/// and persists them to the knowledge graph via <see cref="IKnowledgeMemory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Only activates for requests implementing <see cref="IAgentTurnRequest"/> that produce
/// a successful <see cref="IAgentTurnResult"/> with a non-empty response.
/// All other request types pass through untouched.
/// </para>
/// <para>
/// Extraction runs as fire-and-forget on a background thread. The agent's response
/// is returned immediately — extraction failures are logged but never propagate.
/// </para>
/// </remarks>
public sealed class KnowledgeExtractionBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly IConversationFactExtractor _extractor;
    private readonly IKnowledgeMemory _knowledgeMemory;
    private readonly KnowledgeBridgeConfig _config;
    private readonly ILogger<KnowledgeExtractionBehavior<TRequest, TResponse>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgeExtractionBehavior{TRequest, TResponse}"/> class.
    /// </summary>
    public KnowledgeExtractionBehavior(
        IConversationFactExtractor extractor,
        IKnowledgeMemory knowledgeMemory,
        IOptions<KnowledgeBridgeConfig> config,
        ILogger<KnowledgeExtractionBehavior<TRequest, TResponse>> logger)
    {
        _extractor = extractor;
        _knowledgeMemory = knowledgeMemory;
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next();

        if (!_config.Enabled)
            return response;

        if (request is not IAgentTurnRequest agentRequest ||
            response is not IAgentTurnResult { Success: true, Response.Length: > 0 } turnResult)
            return response;

        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_config.ExtractionTimeoutSeconds));

                var facts = await _extractor.ExtractAsync(
                    agentRequest.UserMessage,
                    turnResult.Response,
                    agentRequest.ConversationId,
                    agentRequest.TurnNumber,
                    cts.Token);

                foreach (var fact in facts)
                {
                    try
                    {
                        await _knowledgeMemory.RememberAsync(
                            fact.Key, fact.Content, fact.EntityType, cts.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex,
                            "Failed to persist fact {Key} for conversation {ConversationId}",
                            fact.Key, agentRequest.ConversationId);
                    }
                }

                if (facts.Count > 0)
                {
                    _logger.LogInformation(
                        "Persisted {Count} facts from conversation {ConversationId} turn {Turn}",
                        facts.Count, agentRequest.ConversationId, agentRequest.TurnNumber);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Knowledge extraction failed for conversation {ConversationId} turn {Turn}",
                    agentRequest.ConversationId, agentRequest.TurnNumber);
            }
        }, cancellationToken);

        return response;
    }
}
