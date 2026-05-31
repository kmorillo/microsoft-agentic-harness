using System.Diagnostics;
using Domain.AI.Prompts;
using FluentAssertions;
using Infrastructure.AI.Prompts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Prompts;

public sealed class OtelPromptUsageRecorderTests
{
    private static PromptDescriptor Desc() => new()
    {
        Name = "faithfulness-judge",
        Version = new PromptVersion(1, 0),
        ContentHash = "abc123",
        Body = "ignored"
    };

    private readonly OtelPromptUsageRecorder _sut = new(NullLogger<OtelPromptUsageRecorder>.Instance);

    [Fact]
    public async Task Records_descriptor_and_returns_usage_with_timestamp_when_no_activity_current()
    {
        var record = await _sut.RecordAsync(Desc(), PromptUsageContext.Empty, CancellationToken.None);

        record.Descriptor.Identifier.Should().Be("faithfulness-judge@v1.0");
        record.TraceId.Should().BeNull();
        record.SpanId.Should().BeNull();
        record.CaseId.Should().BeNull();
        record.MetricKey.Should().BeNull();
        record.RecordedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Records_capture_context_case_id_and_metric_key()
    {
        var context = new PromptUsageContext { CaseId = "case-7", MetricKey = "faithfulness" };

        var record = await _sut.RecordAsync(Desc(), context, CancellationToken.None);

        record.CaseId.Should().Be("case-7");
        record.MetricKey.Should().Be("faithfulness");
    }

    [Fact]
    public async Task Stamps_current_activity_with_descriptor_and_context_tags()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var src = new ActivitySource("OtelPromptUsageRecorderTests");
        using var activity = src.StartActivity("test-span");
        activity.Should().NotBeNull();

        var context = new PromptUsageContext
        {
            CaseId = "case-7",
            MetricKey = "faithfulness",
            Tags = new Dictionary<string, string> { ["custom.tag"] = "value" }
        };
        await _sut.RecordAsync(Desc(), context, CancellationToken.None);

        activity!.GetTagItem(OtelPromptUsageRecorder.TagName).Should().Be("faithfulness-judge");
        activity.GetTagItem(OtelPromptUsageRecorder.TagVersion).Should().Be("v1.0");
        activity.GetTagItem(OtelPromptUsageRecorder.TagHash).Should().Be("abc123");
        activity.GetTagItem(OtelPromptUsageRecorder.TagCaseId).Should().Be("case-7");
        activity.GetTagItem(OtelPromptUsageRecorder.TagMetric).Should().Be("faithfulness");
        activity.GetTagItem("custom.tag").Should().Be("value");
    }

    [Fact]
    public async Task Captures_trace_and_span_ids_when_activity_present()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var src = new ActivitySource("OtelPromptUsageRecorderTests");
        using var activity = src.StartActivity("test-span");

        var record = await _sut.RecordAsync(Desc(), PromptUsageContext.Empty, CancellationToken.None);

        record.TraceId.Should().Be(activity!.TraceId.ToString());
        record.SpanId.Should().Be(activity.SpanId.ToString());
    }
}
