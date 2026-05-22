using System.Text.RegularExpressions;
using Application.AI.Common.Interfaces.Routing;
using Domain.AI.Routing.Enums;
using Domain.AI.Routing.Models;
using Domain.Common.Config.AI.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Routing;

/// <summary>
/// Fast, zero-cost heuristic classifier for task complexity.
/// Evaluates message length, keywords, tool count, conversation depth, and code block presence.
/// Returns <see langword="null"/> when no tier exceeds the confidence threshold, triggering LLM fallback.
/// </summary>
public sealed class TaskComplexityHeuristic : ITaskComplexityHeuristic
{
    private static readonly Regex CodeBlockPattern = new(@"```", RegexOptions.Compiled);

    private readonly ModelRoutingConfig _config;
    private readonly ILogger<TaskComplexityHeuristic> _logger;

    /// <summary>Initializes a new instance of <see cref="TaskComplexityHeuristic"/>.</summary>
    public TaskComplexityHeuristic(
        IOptions<ModelRoutingConfig> config,
        ILogger<TaskComplexityHeuristic> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public TaskComplexityAssessment? Classify(AgentTurnContext context)
    {
        var scores = new Dictionary<TaskComplexity, double>
        {
            [TaskComplexity.Trivial] = 0.0,
            [TaskComplexity.Simple] = 0.0,
            [TaskComplexity.Moderate] = 0.0,
            [TaskComplexity.Complex] = 0.0
        };

        var thresholds = _config.HeuristicThresholds;
        var message = context.UserMessage;
        var messageLength = message.Length;

        // Signal: Message length — tier anchor. Short messages are only Trivial candidates
        // when paired with trivial keyword signals; otherwise they may still be Simple.
        if (messageLength <= thresholds.TrivialMaxLength)
            scores[TaskComplexity.Trivial] += 0.25;
        else if (messageLength <= thresholds.SimpleMaxLength)
            scores[TaskComplexity.Simple] += 0.4;
        else if (messageLength <= thresholds.ModerateMaxLength)
            scores[TaskComplexity.Moderate] += 0.4;
        else
            scores[TaskComplexity.Complex] += 0.45;

        // Signal: Trivial keywords (greetings, acknowledgments) — strong indicator
        var lowerMessage = message.ToLowerInvariant();
        if (thresholds.TrivialKeywords.Any(kw =>
                lowerMessage == kw ||
                lowerMessage.StartsWith(kw + " ", StringComparison.Ordinal) ||
                lowerMessage.StartsWith(kw + "!", StringComparison.Ordinal)))
        {
            scores[TaskComplexity.Trivial] += 0.55;
        }

        // Signal: Complex keywords — strong indicator
        if (thresholds.ComplexKeywords.Any(kw => lowerMessage.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            scores[TaskComplexity.Complex] += 0.4;

        // Signal: Tool count. Tool availability is a strong complexity signal.
        // Extreme ends (0 or many tools) are decisive — sufficient alone to reach threshold.
        if (context.AvailableToolCount == 0)
            scores[TaskComplexity.Trivial] += 0.2;
        else if (context.AvailableToolCount <= 3)
            scores[TaskComplexity.Simple] += 0.85;   // decisive: few tools = Simple
        else if (context.AvailableToolCount <= thresholds.ComplexMinToolCount)
            scores[TaskComplexity.Moderate] += 0.35;
        else
            scores[TaskComplexity.Complex] += 0.85;  // decisive: many tools = Complex

        // Signal: Code blocks present — corroborates Moderate or higher
        if (CodeBlockPattern.IsMatch(message))
            scores[TaskComplexity.Moderate] += 0.35;

        // Signal: Conversation depth + tool chains
        if (context.TurnNumber == 1 && scores[TaskComplexity.Trivial] > 0.5)
            scores[TaskComplexity.Trivial] += 0.1;
        else if (context.TurnNumber >= 8 && (context.RecentToolNames?.Count ?? 0) > 4)
            scores[TaskComplexity.Complex] += 0.2;
        else if (context.TurnNumber >= 4)
            scores[TaskComplexity.Moderate] += 0.15;

        // Select the tier with the highest score
        var best = scores.MaxBy(kv => kv.Value);
        var confidence = Math.Min(best.Value, 1.0);

        if (confidence < _config.HeuristicConfidenceThreshold)
        {
            _logger.LogDebug(
                "Heuristic confidence {Confidence:F2} below threshold {Threshold:F2} for conversation {ConversationId}, deferring to LLM",
                confidence, _config.HeuristicConfidenceThreshold, context.ConversationId);
            return null;
        }

        _logger.LogDebug(
            "Heuristic classified turn as {Complexity} with confidence {Confidence:F2} for conversation {ConversationId}",
            best.Key, confidence, context.ConversationId);

        return new TaskComplexityAssessment
        {
            Complexity = best.Key,
            Confidence = confidence,
            Source = ClassificationSource.Heuristic,
            Reasoning = $"Heuristic signals: length={messageLength}, tools={context.AvailableToolCount}, turn={context.TurnNumber}"
        };
    }
}
