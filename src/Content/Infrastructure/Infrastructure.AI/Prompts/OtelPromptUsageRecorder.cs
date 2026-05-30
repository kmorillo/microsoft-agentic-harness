using System.Diagnostics;
using Application.AI.Common.Prompts.Interfaces;
using Application.AI.Common.Prompts.Models;
using Domain.AI.Prompts;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Prompts;

/// <summary>
/// <see cref="IPromptUsageRecorder"/> that stamps the current <see cref="Activity"/>
/// with prompt name/version/hash tags so OTel exporters and downstream consumers
/// (Tempo, Grafana, Postgres trace store) can attribute spans to specific prompt
/// versions.
/// </summary>
/// <remarks>
/// <para>
/// Tag names follow the OTel semantic-convention pattern:
/// <list type="bullet">
///   <item><description><c>ai.prompt.name</c> — registry name</description></item>
///   <item><description><c>ai.prompt.version</c> — <c>v{Major}.{Minor}</c></description></item>
///   <item><description><c>ai.prompt.hash</c> — SHA-256 of body, hex lowercase</description></item>
/// </list>
/// </para>
/// <para>
/// Never throws. If <c>Activity.Current</c> is null the recorder silently records
/// the timestamp + descriptor into the returned <see cref="PromptUsageRecord"/>
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
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var activity = Activity.Current;
        try
        {
            activity?.SetTag(TagName, descriptor.Name);
            activity?.SetTag(TagVersion, descriptor.Version.ToString());
            activity?.SetTag(TagHash, descriptor.ContentHash);
        }
        catch (Exception ex)
        {
            // Observability code must never break the caller.
            _logger.LogWarning(ex, "Failed to stamp prompt usage tags on Activity.");
        }

        var record = new PromptUsageRecord
        {
            Descriptor = descriptor,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            RecordedAtUtc = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(record);
    }
}
