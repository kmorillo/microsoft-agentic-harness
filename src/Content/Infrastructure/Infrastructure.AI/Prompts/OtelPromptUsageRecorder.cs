using System.Diagnostics;
using Application.AI.Common.Prompts.Interfaces;
using Application.AI.Common.Prompts.Models;
using Domain.AI.Prompts;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Prompts;

/// <summary>
/// <see cref="IPromptUsageRecorder"/> that stamps the current <see cref="Activity"/>
/// with prompt name/version/hash tags plus any consumer-supplied context (case id,
/// metric key, free-form tags) so OTel exporters and downstream consumers can
/// attribute spans to specific prompt versions AND the case that triggered them.
/// </summary>
/// <remarks>
/// <para>
/// Tag names follow the OTel semantic-convention pattern:
/// <list type="bullet">
///   <item><description><c>ai.prompt.name</c> — registry name</description></item>
///   <item><description><c>ai.prompt.version</c> — <c>v{Major}.{Minor}</c></description></item>
///   <item><description><c>ai.prompt.hash</c> — SHA-256 of body, hex lowercase</description></item>
///   <item><description><c>ai.prompt.case_id</c> — <c>PromptUsageContext.CaseId</c> when supplied</description></item>
///   <item><description><c>ai.prompt.metric</c> — <c>PromptUsageContext.MetricKey</c> when supplied</description></item>
/// </list>
/// </para>
/// <para>
/// Honors the <see cref="IPromptUsageRecorder"/> "Never throws" contract: any failure
/// inside the recorder is caught and logged, never propagated. If
/// <c>Activity.Current</c> is null the recorder silently records the timestamp +
/// descriptor + context into the returned <see cref="PromptUsageRecord"/>
/// (useful for tests + persistence chains) without OTel side effects.
/// </para>
/// </remarks>
public sealed class OtelPromptUsageRecorder : IPromptUsageRecorder
{
    /// <summary>OTel tag for the registry name.</summary>
    public const string TagName = "ai.prompt.name";

    /// <summary>OTel tag for the version (<c>v{Major}.{Minor}</c>).</summary>
    public const string TagVersion = "ai.prompt.version";

    /// <summary>OTel tag for the SHA-256 content hash.</summary>
    public const string TagHash = "ai.prompt.hash";

    /// <summary>OTel tag for the case / unit-of-work id.</summary>
    public const string TagCaseId = "ai.prompt.case_id";

    /// <summary>OTel tag for the consuming surface (metric key / command name).</summary>
    public const string TagMetric = "ai.prompt.metric";

    private readonly ILogger<OtelPromptUsageRecorder> _logger;

    /// <summary>Initializes a new instance.</summary>
    public OtelPromptUsageRecorder(ILogger<OtelPromptUsageRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PromptUsageRecord> RecordAsync(
        PromptDescriptor descriptor,
        PromptUsageContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(context);

        var activity = Activity.Current;
        try
        {
            activity?.SetTag(TagName, descriptor.Name);
            activity?.SetTag(TagVersion, descriptor.Version.ToString());
            activity?.SetTag(TagHash, descriptor.ContentHash);

            if (!string.IsNullOrEmpty(context.CaseId)) activity?.SetTag(TagCaseId, context.CaseId);
            if (!string.IsNullOrEmpty(context.MetricKey)) activity?.SetTag(TagMetric, context.MetricKey);
            if (context.Tags is not null)
            {
                foreach (var (key, value) in context.Tags)
                {
                    activity?.SetTag(key, value);
                }
            }
        }
        catch (Exception ex)
        {
            // Honors the IPromptUsageRecorder contract: observability code never breaks the caller.
            _logger.LogWarning(ex, "Failed to stamp prompt usage tags on Activity.");
        }

        var record = new PromptUsageRecord
        {
            Descriptor = descriptor,
            CaseId = context.CaseId,
            MetricKey = context.MetricKey,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            RecordedAtUtc = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(record);
    }
}
