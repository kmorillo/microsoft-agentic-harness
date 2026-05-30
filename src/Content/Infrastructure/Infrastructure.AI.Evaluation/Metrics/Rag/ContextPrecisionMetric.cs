using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Evaluation.Metrics.Rag;

/// <summary>
/// Scores what fraction of the retrieved context was actually relevant to the user's
/// question. Requires <see cref="EvalCase.Input"/> and <see cref="EvalCase.RetrievedContext"/>.
/// </summary>
public sealed class ContextPrecisionMetric : RagJudgeMetricBase
{
    /// <inheritdoc />
    public ContextPrecisionMetric(
        ILlmJudge judge,
        IPromptTemplateLoader templateLoader,
        ILogger<ContextPrecisionMetric> logger)
        : base(judge, templateLoader, logger) { }

    /// <inheritdoc />
    public override string Key => "context_precision";

    /// <inheritdoc />
    protected override string TemplateName => "context-precision";

    /// <inheritdoc />
    protected override RagInputs RequiredFields => RagInputs.Input | RagInputs.RetrievedContext;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output)
        => new Dictionary<string, string?>
        {
            ["input"] = @case.Input,
            ["retrieved_context"] = @case.RetrievedContext,
        };
}
