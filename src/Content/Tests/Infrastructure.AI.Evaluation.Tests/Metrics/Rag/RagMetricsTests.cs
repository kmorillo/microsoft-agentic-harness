using Application.AI.Common.Evaluation.Interfaces;
using Application.AI.Common.Evaluation.Models;
using Application.AI.Common.Prompts.Exceptions;
using Application.AI.Common.Prompts.Interfaces;
using Application.AI.Common.Prompts.Models;
using Domain.AI.Evaluation;
using Domain.AI.Prompts;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Metrics.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Infrastructure.AI.Evaluation.Tests.Metrics.Rag.RagMetricTestHarness;

namespace Infrastructure.AI.Evaluation.Tests.Metrics.Rag;

/// <summary>
/// Behavior tests covering all 5 RAG metrics via the shared base class.
/// Each metric is exercised: key + required-field validation + pass/fail/warn paths +
/// reasoning propagation. Resolution/recorder semantics are exercised on FaithfulnessMetric
/// (representative) and on a sentinel subclass that exercises the base-class convention.
/// </summary>
public sealed class RagMetricsTests
{
    [Theory]
    [InlineData("faithfulness", typeof(FaithfulnessMetric))]
    [InlineData("context_precision", typeof(ContextPrecisionMetric))]
    [InlineData("context_recall", typeof(ContextRecallMetric))]
    [InlineData("answer_relevance", typeof(AnswerRelevanceMetric))]
    [InlineData("answer_correctness", typeof(AnswerCorrectnessMetric))]
    public void Each_metric_reports_its_registered_key(string expectedKey, Type metricType)
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var metric = (Application.AI.Common.Evaluation.Interfaces.IEvalMetric)
            Create(metricType, judge.Object, registry.Object, recorder.Object);

        metric.Key.Should().Be(expectedKey);
    }

    // ---------- FaithfulnessMetric ----------

    [Fact]
    public async Task Faithfulness_returns_warn_when_retrieved_context_missing()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(
            Case(retrieved: null),
            Output(),
            Spec(),
            CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("RetrievedContext");
    }

    [Fact]
    public async Task Faithfulness_passes_when_judge_score_meets_threshold()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.9, "grounded"));
        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Score.Should().Be(0.9);
        score.Reasoning.Should().Be("grounded");
        score.CostUsd.Should().Be(0.001m);
    }

    [Fact]
    public async Task Faithfulness_fails_when_below_threshold()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.4));
        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Fail);
        score.Score.Should().Be(0.4);
    }

    [Fact]
    public async Task Faithfulness_warns_on_judge_malformed()
    {
        var (judge, registry, recorder) = Plumbing(Malformed());
        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
    }

    // ---------- ContextPrecisionMetric ----------

    [Fact]
    public async Task ContextPrecision_warns_when_input_missing()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new ContextPrecisionMetric(judge.Object, registry.Object, recorder.Object, Log<ContextPrecisionMetric>());

        var score = await sut.ScoreAsync(
            Case(input: "  "),
            Output(),
            Spec(),
            CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("Input");
    }

    [Fact]
    public async Task ContextPrecision_warns_when_retrieved_context_missing()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new ContextPrecisionMetric(judge.Object, registry.Object, recorder.Object, Log<ContextPrecisionMetric>());

        var score = await sut.ScoreAsync(
            Case(retrieved: null),
            Output(),
            Spec(),
            CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
    }

    [Fact]
    public async Task ContextPrecision_passes_at_threshold()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.7));
        var sut = new ContextPrecisionMetric(judge.Object, registry.Object, recorder.Object, Log<ContextPrecisionMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
    }

    // ---------- ContextRecallMetric ----------

    [Fact]
    public async Task ContextRecall_warns_when_expected_output_missing()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new ContextRecallMetric(judge.Object, registry.Object, recorder.Object, Log<ContextRecallMetric>());

        var score = await sut.ScoreAsync(
            Case(expected: null),
            Output(),
            Spec(),
            CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("ExpectedOutput");
    }

    [Fact]
    public async Task ContextRecall_passes_when_judge_scores_high()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.95));
        var sut = new ContextRecallMetric(judge.Object, registry.Object, recorder.Object, Log<ContextRecallMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.8), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Score.Should().Be(0.95);
    }

    // ---------- AnswerRelevanceMetric ----------

    [Fact]
    public async Task AnswerRelevance_warns_when_output_missing()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new AnswerRelevanceMetric(judge.Object, registry.Object, recorder.Object, Log<AnswerRelevanceMetric>());

        var score = await sut.ScoreAsync(
            Case(),
            Output(text: ""),
            Spec(),
            CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("Output");
    }

    [Fact]
    public async Task AnswerRelevance_passes()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.85));
        var sut = new AnswerRelevanceMetric(judge.Object, registry.Object, recorder.Object, Log<AnswerRelevanceMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
    }

    // ---------- AnswerCorrectnessMetric ----------

    [Fact]
    public async Task AnswerCorrectness_warns_when_either_expected_or_output_missing()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new AnswerCorrectnessMetric(judge.Object, registry.Object, recorder.Object, Log<AnswerCorrectnessMetric>());

        var score = await sut.ScoreAsync(Case(expected: null), Output(), Spec(), CancellationToken.None);
        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("ExpectedOutput");

        score = await sut.ScoreAsync(Case(), Output(text: ""), Spec(), CancellationToken.None);
        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("Output");
    }

    [Fact]
    public async Task AnswerCorrectness_passes_with_high_judge_score()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0, "semantic match"));
        var sut = new AnswerCorrectnessMetric(judge.Object, registry.Object, recorder.Object, Log<AnswerCorrectnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.9), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Reasoning.Should().Be("semantic match");
    }

    // ---------- Default-convention PromptName (sentinel) ----------

    private sealed class SentinelRagMetric : RagJudgeMetricBase
    {
        private readonly string _key;
        public SentinelRagMetric(
            string key,
            ILlmJudge judge,
            IPromptRegistry promptRegistry,
            IPromptUsageRecorder usageRecorder)
            : base(judge, promptRegistry, usageRecorder, NullLogger<SentinelRagMetric>.Instance)
        {
            _key = key;
        }
        public override string Key => _key;
        protected override RagInputs RequiredFields => RagInputs.None;
        protected override IReadOnlyDictionary<string, string?> BuildVariables(EvalCase @case, AgentInvocationResult output)
            => new Dictionary<string, string?>();
        // Expose default PromptName for inspection.
        public string ResolvedPromptName => base.PromptName;
    }

    [Theory]
    [InlineData("faithfulness", "faithfulness-judge")]
    [InlineData("context_precision", "context-precision-judge")]
    [InlineData("Context_Precision", "context-precision-judge")] // upper-cased Key still normalizes
    [InlineData("multi_word_key", "multi-word-key-judge")]
    public void Default_promptname_convention_maps_snake_key_to_kebab_judge_name(string key, string expectedPromptName)
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new SentinelRagMetric(key, judge.Object, registry.Object, recorder.Object);

        sut.ResolvedPromptName.Should().Be(expectedPromptName);
    }

    [Fact]
    public async Task Default_promptname_resolves_against_registry_with_kebab_judge_name()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        var sut = new SentinelRagMetric("faithfulness", judge.Object, registry.Object, recorder.Object);

        await sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);

        registry.Verify(
            r => r.GetLatestAsync("faithfulness-judge", It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ---------- Prompt resolution + usage recording ----------

    [Fact]
    public async Task Missing_prompt_softfails_to_warn_without_invoking_judge()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        registry.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("not in registry"));

        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("template");
        judge.VerifyNoOtherCalls();
        recorder.Verify(
            r => r.RecordAsync(It.IsAny<PromptDescriptor>(), It.IsAny<PromptUsageContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PromptRegistryUnavailable_softfails_to_warn()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        registry.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PromptRegistryUnavailableException(
                "faithfulness-judge",
                "backend offline",
                new IOException("file locked")));

        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("resolve");
        judge.VerifyNoOtherCalls();
        recorder.Verify(
            r => r.RecordAsync(It.IsAny<PromptDescriptor>(), It.IsAny<PromptUsageContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Contract_violation_from_registry_propagates_as_defect()
    {
        // Registry impls that throw outside the documented contract (KeyNotFound /
        // PromptRegistryUnavailable / OperationCanceled) are defects — the suite should
        // fail loudly rather than mask them as Warn.
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        registry.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("misconfigured registry"));

        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        Func<Task> act = () => sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OperationCanceled_during_prompt_resolution_propagates()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(1.0));
        registry.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        Func<Task> act = () => sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Successful_score_records_prompt_usage_with_case_id_and_metric_key()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.9));
        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        await sut.ScoreAsync(Case(id: "case-42"), Output(), Spec(), CancellationToken.None);

        recorder.Verify(
            r => r.RecordAsync(
                It.Is<PromptDescriptor>(d => d.Name == "faithfulness-judge"),
                It.Is<PromptUsageContext>(c => c.CaseId == "case-42" && c.MetricKey == "faithfulness"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Descriptor_is_resolved_once_per_metric_instance_and_recorder_fires_per_case()
    {
        var (judge, registry, recorder) = Plumbing(Parsed(0.9));
        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        await sut.ScoreAsync(Case(id: "a"), Output(), Spec(), CancellationToken.None);
        await sut.ScoreAsync(Case(id: "b"), Output(), Spec(), CancellationToken.None);
        await sut.ScoreAsync(Case(id: "c"), Output(), Spec(), CancellationToken.None);

        registry.Verify(
            r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Per-case attribution is a stated invariant — the recorder MUST be called per case.
        recorder.Verify(
            r => r.RecordAsync(
                It.IsAny<PromptDescriptor>(),
                It.IsAny<PromptUsageContext>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task Faulted_resolution_evicts_lazy_so_next_case_retries()
    {
        // First call: registry throws → Warn. Second call: registry succeeds → Pass.
        // Proves the Lazy<Task<>> faulted-eviction path actually re-probes the backend.
        var (judge, registry, recorder) = Plumbing(Parsed(0.9));
        var firstCall = true;
        registry.Setup(r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string name, CancellationToken _) =>
            {
                if (firstCall)
                {
                    firstCall = false;
                    throw new PromptRegistryUnavailableException(
                        name,
                        "transient",
                        new IOException("blip"));
                }
                return Task.FromResult(new PromptDescriptor
                {
                    Name = name,
                    Version = new PromptVersion(1, 0),
                    ContentHash = "deadbeef",
                    Body = "ok",
                });
            });

        var sut = new FaithfulnessMetric(judge.Object, registry.Object, recorder.Object, Log<FaithfulnessMetric>());

        var first = await sut.ScoreAsync(Case(id: "a"), Output(), Spec(), CancellationToken.None);
        first.Verdict.Should().Be(Verdict.Warn);

        var second = await sut.ScoreAsync(Case(id: "b"), Output(), Spec(), CancellationToken.None);
        second.Verdict.Should().Be(Verdict.Pass);

        registry.Verify(
            r => r.GetLatestAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    private static object Create(Type metricType, object judge, object registry, object recorder)
    {
        // Construct via reflection: every RAG metric has the same 4-arg ctor shape.
        var loggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>).MakeGenericType(metricType);
        var logger = loggerType.GetField("Instance")!.GetValue(null)!;
        return Activator.CreateInstance(metricType, judge, registry, recorder, logger)!;
    }
}
