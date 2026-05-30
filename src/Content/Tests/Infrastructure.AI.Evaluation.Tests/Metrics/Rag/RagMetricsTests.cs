using Domain.AI.Evaluation;
using FluentAssertions;
using Infrastructure.AI.Evaluation.Metrics.Rag;
using Moq;
using Xunit;
using static Infrastructure.AI.Evaluation.Tests.Metrics.Rag.RagMetricTestHarness;

namespace Infrastructure.AI.Evaluation.Tests.Metrics.Rag;

/// <summary>
/// Behavior tests covering all 5 RAG metrics via the shared base class.
/// Each metric is exercised: key + template name + required-field validation +
/// pass/fail/warn paths + reasoning propagation.
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
        var (judge, templates) = Plumbing(Parsed(1.0));
        var metric = (Application.AI.Common.Evaluation.Interfaces.IEvalMetric)
            Create(metricType, judge.Object, templates.Object);

        metric.Key.Should().Be(expectedKey);
    }

    // ---------- FaithfulnessMetric ----------

    [Fact]
    public async Task Faithfulness_returns_warn_when_retrieved_context_missing()
    {
        var (judge, templates) = Plumbing(Parsed(1.0));
        var sut = new FaithfulnessMetric(judge.Object, templates.Object, Log<FaithfulnessMetric>());

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
        var (judge, templates) = Plumbing(Parsed(0.9, "grounded"));
        var sut = new FaithfulnessMetric(judge.Object, templates.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Score.Should().Be(0.9);
        score.Reasoning.Should().Be("grounded");
        score.CostUsd.Should().Be(0.001m);
    }

    [Fact]
    public async Task Faithfulness_fails_when_below_threshold()
    {
        var (judge, templates) = Plumbing(Parsed(0.4));
        var sut = new FaithfulnessMetric(judge.Object, templates.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Fail);
        score.Score.Should().Be(0.4);
    }

    [Fact]
    public async Task Faithfulness_warns_on_judge_malformed()
    {
        var (judge, templates) = Plumbing(Malformed());
        var sut = new FaithfulnessMetric(judge.Object, templates.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
    }

    // ---------- ContextPrecisionMetric ----------

    [Fact]
    public async Task ContextPrecision_warns_when_input_missing()
    {
        var (judge, templates) = Plumbing(Parsed(1.0));
        var sut = new ContextPrecisionMetric(judge.Object, templates.Object, Log<ContextPrecisionMetric>());

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
        var (judge, templates) = Plumbing(Parsed(1.0));
        var sut = new ContextPrecisionMetric(judge.Object, templates.Object, Log<ContextPrecisionMetric>());

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
        var (judge, templates) = Plumbing(Parsed(0.7));
        var sut = new ContextPrecisionMetric(judge.Object, templates.Object, Log<ContextPrecisionMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
    }

    // ---------- ContextRecallMetric ----------

    [Fact]
    public async Task ContextRecall_warns_when_expected_output_missing()
    {
        var (judge, templates) = Plumbing(Parsed(1.0));
        var sut = new ContextRecallMetric(judge.Object, templates.Object, Log<ContextRecallMetric>());

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
        var (judge, templates) = Plumbing(Parsed(0.95));
        var sut = new ContextRecallMetric(judge.Object, templates.Object, Log<ContextRecallMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.8), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Score.Should().Be(0.95);
    }

    // ---------- AnswerRelevanceMetric ----------

    [Fact]
    public async Task AnswerRelevance_warns_when_output_missing()
    {
        var (judge, templates) = Plumbing(Parsed(1.0));
        var sut = new AnswerRelevanceMetric(judge.Object, templates.Object, Log<AnswerRelevanceMetric>());

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
        var (judge, templates) = Plumbing(Parsed(0.85));
        var sut = new AnswerRelevanceMetric(judge.Object, templates.Object, Log<AnswerRelevanceMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.7), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
    }

    // ---------- AnswerCorrectnessMetric ----------

    [Fact]
    public async Task AnswerCorrectness_warns_when_either_expected_or_output_missing()
    {
        var (judge, templates) = Plumbing(Parsed(1.0));
        var sut = new AnswerCorrectnessMetric(judge.Object, templates.Object, Log<AnswerCorrectnessMetric>());

        // expected_output missing
        var score = await sut.ScoreAsync(Case(expected: null), Output(), Spec(), CancellationToken.None);
        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("ExpectedOutput");

        // output missing
        score = await sut.ScoreAsync(Case(), Output(text: ""), Spec(), CancellationToken.None);
        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("Output");
    }

    [Fact]
    public async Task AnswerCorrectness_passes_with_high_judge_score()
    {
        var (judge, templates) = Plumbing(Parsed(1.0, "semantic match"));
        var sut = new AnswerCorrectnessMetric(judge.Object, templates.Object, Log<AnswerCorrectnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(threshold: 0.9), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Pass);
        score.Reasoning.Should().Be("semantic match");
    }

    // ---------- Template loading failure ----------

    [Fact]
    public async Task Missing_template_softfails_to_warn_without_invoking_judge()
    {
        var (judge, templates) = Plumbing(Parsed(1.0));
        templates.Setup(t => t.Load(It.IsAny<string>()))
            .Throws(new FileNotFoundException("not embedded"));

        var sut = new FaithfulnessMetric(judge.Object, templates.Object, Log<FaithfulnessMetric>());

        var score = await sut.ScoreAsync(Case(), Output(), Spec(), CancellationToken.None);

        score.Verdict.Should().Be(Verdict.Warn);
        score.Reasoning.Should().Contain("template");
        judge.VerifyNoOtherCalls();
    }

    private static object Create(Type metricType, object judge, object templates)
    {
        // Construct via reflection: every RAG metric has the same 3-arg ctor shape.
        var loggerType = typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>).MakeGenericType(metricType);
        var logger = loggerType.GetField("Instance")!.GetValue(null)!;
        return Activator.CreateInstance(metricType, judge, templates, logger)!;
    }
}
