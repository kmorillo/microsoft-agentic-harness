using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Domain.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Evaluation.Metrics.Rag;

/// <summary>
/// Scores what fraction of the reference answer's claims are supported by the retrieved
/// context — isolates retrieval quality from generation quality. Requires
/// <see cref="EvalCase.ExpectedOutput"/> and <see cref="EvalCase.RetrievedContext"/>.
/// </summary>
public sealed class ContextRecallMetric : RagJudgeMetricBase
{
    /// <inheritdoc />
    public ContextRecallMetric(
        ILlmJudge judge,
        IPromptTemplateLoader templateLoader,
        ILogger<ContextRecallMetric> logger)
        : base(judge, templateLoader, logger) { }

    /// <inheritdoc />
    public override string Key => "context_recall";

    /// <inheritdoc />
    protected override string TemplateName => "context-recall";

    /// <inheritdoc />
    protected override RagInputs RequiredFields => RagInputs.ExpectedOutput | RagInputs.RetrievedContext;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output)
        => new Dictionary<string, string?>
        {
            ["expected_output"] = @case.ExpectedOutput,
            ["retrieved_context"] = @case.RetrievedContext,
        };
}
