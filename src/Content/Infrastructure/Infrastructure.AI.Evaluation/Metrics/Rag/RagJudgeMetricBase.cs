using System.Diagnostics;
using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Evaluation.Outcomes;
using Domain.AI.Evaluation;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Evaluation.Metrics.Rag;

/// <summary>
/// Shared base for the RAG metric pack (faithfulness, context precision/recall,
/// answer relevance/correctness). Centralizes template loading, required-field
/// validation, judge dispatch through the structured <see cref="ILlmJudge"/>,
/// threshold comparison, and soft-fail-to-Warn for all expected failure modes.
/// </summary>
/// <remarks>
/// The nonce + HtmlEncode injection mitigations live inside <see cref="ILlmJudge"/>
/// (the structured-request boundary) so subclasses cannot accidentally bypass them.
/// Subclasses supply only metadata: <see cref="Key"/>, <see cref="TemplateName"/>,
/// <see cref="RequiredFields"/>, and <see cref="BuildVariables"/>.
/// </remarks>
public abstract class RagJudgeMetricBase : IEvalMetric
{
    private readonly ILlmJudge _judge;
    private readonly IPromptTemplateLoader _templateLoader;
    private readonly ILogger _logger;

    private const string SystemPromptCore =
        "You are an evaluation judge. Score the supplied data per the rubric in the user prompt. " +
        "Respond ONLY with a single JSON object: {\"score\": <0.0-1.0>, \"reasoning\": \"<one or two sentences>\"}. " +
        "Do not include markdown fences, prose, or any text outside the JSON object.";

    /// <summary>Required fields a case must supply for this metric to score.</summary>
    [Flags]
    protected enum RagInputs
    {
        /// <summary>No special requirement.</summary>
        None = 0,
        /// <summary>The case's <c>Input</c> must be non-empty.</summary>
        Input = 1 << 0,
        /// <summary>The case's <c>ExpectedOutput</c> must be non-empty.</summary>
        ExpectedOutput = 1 << 1,
        /// <summary>The case's <c>RetrievedContext</c> must be non-empty.</summary>
        RetrievedContext = 1 << 2,
        /// <summary>The agent invocation's <c>Output</c> must be non-empty.</summary>
        Output = 1 << 3,
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="judge">Shared judge call service.</param>
    /// <param name="templateLoader">Loads the metric's prompt template.</param>
    /// <param name="logger">Logger for missing-input and rendering diagnostics.</param>
    protected RagJudgeMetricBase(
        ILlmJudge judge,
        IPromptTemplateLoader templateLoader,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(templateLoader);
        ArgumentNullException.ThrowIfNull(logger);

        _judge = judge;
        _templateLoader = templateLoader;
        _logger = logger;
    }

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <summary>Name of the embedded prompt template (e.g. <c>"faithfulness"</c>).</summary>
    protected abstract string TemplateName { get; }

    /// <summary>Fields the metric needs populated; missing fields → <see cref="Verdict.Warn"/> without invoking the judge.</summary>
    protected abstract RagInputs RequiredFields { get; }

    /// <summary>Builds the variable dictionary substituted into the template.</summary>
    protected abstract IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output);

    /// <inheritdoc />
    public async Task<MetricScore> ScoreAsync(
        EvalCase @case,
        AgentInvocationResult output,
        MetricSpec spec,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(spec);

        var sw = Stopwatch.StartNew();

        var missingField = FindMissingRequired(@case, output);
        if (missingField is not null)
        {
            return Warn(sw, $"{Key} requires {missingField} to be populated for the case.");
        }

        string template;
        try
        {
            template = _templateLoader.Load(TemplateName);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Prompt template '{Template}' missing for metric {Metric}.", TemplateName, Key);
            return Warn(sw, $"Prompt template '{TemplateName}' not found for metric {Key}.");
        }

        LlmJudgeResult result;
        try
        {
            result = await _judge.JudgeAsync(
                new LlmJudgeRequest
                {
                    SystemPromptCore = SystemPromptCore,
                    UserPromptTemplate = template,
                    Variables = BuildVariables(@case, output),
                },
                cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Belt-and-suspenders: the judge contract is soft-fail-to-Warn for expected
            // failures, but we catch any escaped exception (e.g. template bug, unexpected
            // judge impl issue) so a single bad case can't abort the entire eval suite.
            _logger.LogWarning(ex, "Judge invocation escaped contract for metric {Metric}.", Key);
            return Warn(sw, $"Judge invocation error: {ex.Message}");
        }

        sw.Stop();

        return result.Outcome switch
        {
            LlmJudgeOutcome.Parsed => new MetricScore
            {
                MetricKey = Key,
                Score = result.Score,
                Verdict = result.Score >= spec.Threshold ? Verdict.Pass : Verdict.Fail,
                Reasoning = result.Reasoning,
                RawOutput = result.RawOutput,
                CostUsd = result.CostUsd,
                Duration = sw.Elapsed
            },
            _ => new MetricScore
            {
                MetricKey = Key,
                Score = 0.0,
                Verdict = Verdict.Warn,
                Reasoning = result.Reasoning,
                RawOutput = result.RawOutput,
                CostUsd = result.CostUsd,
                Duration = sw.Elapsed
            }
        };
    }

    private string? FindMissingRequired(EvalCase @case, AgentInvocationResult output)
    {
        if (RequiredFields.HasFlag(RagInputs.Input) && string.IsNullOrWhiteSpace(@case.Input))
            return "case.Input";
        if (RequiredFields.HasFlag(RagInputs.ExpectedOutput) && string.IsNullOrWhiteSpace(@case.ExpectedOutput))
            return "case.ExpectedOutput";
        if (RequiredFields.HasFlag(RagInputs.RetrievedContext) && string.IsNullOrWhiteSpace(@case.RetrievedContext))
            return "case.RetrievedContext";
        if (RequiredFields.HasFlag(RagInputs.Output) && string.IsNullOrWhiteSpace(output.Output))
            return "output.Output";
        return null;
    }

    private MetricScore Warn(Stopwatch sw, string reasoning)
    {
        sw.Stop();
        return new MetricScore
        {
            MetricKey = Key,
            Score = 0.0,
            Verdict = Verdict.Warn,
            Reasoning = reasoning,
            RawOutput = null,
            CostUsd = 0m,
            Duration = sw.Elapsed
        };
    }
}
