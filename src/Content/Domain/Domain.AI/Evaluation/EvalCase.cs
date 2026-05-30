namespace Domain.AI.Evaluation;

/// <summary>
/// A single evaluation case: an input the harness will be invoked with,
/// plus the metric specifications that score the resulting output.
/// </summary>
/// <remarks>
/// <para>
/// Cases are typically authored in YAML and loaded by an <c>IEvalDatasetLoader</c>.
/// Each case is independent — runners may execute cases in parallel.
/// </para>
/// <para>
/// The <see cref="MetricSpecs"/> collection drives scoring: each entry names a registered
/// <c>IEvalMetric</c> by key and supplies an opaque parameters bag the metric interprets.
/// This avoids coupling the domain to specific metric implementations.
/// </para>
/// </remarks>
public sealed record EvalCase
{
    /// <summary>Stable identifier for the case. Used in reports and CI test results.</summary>
    public required string Id { get; init; }

    /// <summary>The input the harness receives (typically the user message for the agent turn).</summary>
    public required string Input { get; init; }

    /// <summary>
    /// Optional reference answer used by metrics that need an expected output
    /// (e.g. exact-match, semantic-similarity, answer-correctness).
    /// </summary>
    public string? ExpectedOutput { get; init; }

    /// <summary>
    /// Optional retrieved context used by RAG metrics (faithfulness, context-precision, etc.).
    /// Either captured from a real run or hand-authored.
    /// </summary>
    public string? RetrievedContext { get; init; }

    /// <summary>
    /// Free-form tags for filtering (e.g. "pii", "rag", "smoke"). Allows
    /// running a subset of cases via <c>--tags</c>.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Optional per-case parameter overrides applied to the agent invocation
    /// (e.g. temperature, deployment, system prompt addendum). Implementation-defined.
    /// </summary>
    public IReadOnlyDictionary<string, string> InvocationOverrides { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// The metric specifications that score this case. Each names a registered metric
    /// by key (e.g. <c>"exact_match"</c>, <c>"llm_judge"</c>, <c>"faithfulness"</c>)
    /// and supplies metric-specific parameters.
    /// </summary>
    public required IReadOnlyList<MetricSpec> MetricSpecs { get; init; }
}

/// <summary>
/// References a registered metric by key and supplies the parameters that drive it
/// for one case (e.g. expected substrings, threshold, judge rubric).
/// </summary>
public sealed record MetricSpec
{
    /// <summary>The registered metric key (matches an <c>IEvalMetric</c> keyed DI registration).</summary>
    public required string MetricKey { get; init; }

    /// <summary>
    /// Threshold the score must meet for a <see cref="Verdict.Pass"/>.
    /// Interpretation is metric-specific — most use 0.0–1.0 with a default of 1.0 for binary metrics
    /// and 0.7–0.8 for graded metrics.
    /// </summary>
    public double Threshold { get; init; } = 1.0;

    /// <summary>
    /// Free-form parameters the metric consumes. Strings are used for portability across
    /// YAML/JSON; metrics parse to richer types as needed (e.g. JSON arrays into <c>string[]</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>();
}
