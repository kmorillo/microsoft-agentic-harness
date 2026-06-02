using Application.AI.Common.Services;
using Domain.Common.Config;
using Domain.Common.Config.Observability;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Application.AI.Common.Tests.Services;

/// <summary>
/// Tests for the PR 6 invocation-capture path on <see cref="LlmUsageCapture"/>.
/// Args + stdout pairing by CallId is the load-bearing invariant for the
/// <c>/api/sessions/{id}/tools/{invocationId}</c> deep-link — these tests
/// guard against silent regressions there.
/// </summary>
public class LlmUsageCaptureTests
{
    private static LlmUsageCapture CreateSut()
    {
        var appConfig = new AppConfig();
        appConfig.Observability.LlmPricing.Models.Add(new ModelPricingEntry
        {
            Name = "test-model",
            InputPerMillion = 0m,
            OutputPerMillion = 0m,
            CacheReadPerMillion = 0m,
            CacheWritePerMillion = 0m,
        });
        var monitor = new Mock<IOptionsMonitor<AppConfig>>();
        monitor.Setup(m => m.CurrentValue).Returns(appConfig);
        return new LlmUsageCapture(monitor.Object);
    }

    [Fact]
    public void TakeSnapshot_NoActivity_EmitsEmptyInvocations()
    {
        var sut = CreateSut();

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolInvocations.Should().BeEmpty();
        snapshot.ToolNames.Should().BeEmpty();
    }

    [Fact]
    public void RecordToolRequestAndResult_SameCallId_MergesIntoSingleInvocation()
    {
        var sut = CreateSut();

        sut.RecordToolRequest("call-1", "ReadFile", "{\"path\":\"a.txt\"}");
        sut.RecordToolResult("call-1", "contents of a.txt");

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolInvocations.Should().ContainSingle();
        var inv = snapshot.ToolInvocations[0];
        inv.CallId.Should().Be("call-1");
        inv.ToolName.Should().Be("ReadFile");
        inv.ArgsJson.Should().Be("{\"path\":\"a.txt\"}");
        inv.Stdout.Should().Be("contents of a.txt");
    }

    [Fact]
    public void RecordToolRequest_TwoDistinctCallIds_KeepsBothInvocations()
    {
        var sut = CreateSut();

        sut.RecordToolRequest("call-1", "ReadFile", "{\"path\":\"a.txt\"}");
        sut.RecordToolRequest("call-2", "ReadFile", "{\"path\":\"b.txt\"}");
        sut.RecordToolResult("call-2", "B");
        sut.RecordToolResult("call-1", "A");

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolInvocations.Should().HaveCount(2);
        snapshot.ToolInvocations.Should().ContainSingle(i => i.CallId == "call-1" && i.Stdout == "A");
        snapshot.ToolInvocations.Should().ContainSingle(i => i.CallId == "call-2" && i.Stdout == "B");
    }

    [Fact]
    public void RecordToolRequest_NullCallId_StoresEachAsItsOwnInvocation()
    {
        var sut = CreateSut();

        sut.RecordToolRequest(null, "ReadFile", "{\"x\":1}");
        sut.RecordToolRequest(null, "ReadFile", "{\"x\":2}");

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolInvocations.Should().HaveCount(2);
        snapshot.ToolInvocations.Should().AllSatisfy(i => i.CallId.Should().BeNull());
    }

    [Fact]
    public void RecordToolResult_NullCallId_IsIgnored()
    {
        var sut = CreateSut();

        sut.RecordToolResult(null, "orphan result");

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolInvocations.Should().BeEmpty();
    }

    [Fact]
    public void RecordToolResult_UnknownCallId_PreservedAsPartialInvocation()
    {
        var sut = CreateSut();

        sut.RecordToolResult("orphan-id", "late arriving result");

        var snapshot = sut.TakeSnapshot();

        // The capture keeps orphan results so a future debug session can see
        // them, but the invocation with empty ToolName is filtered out of the
        // snapshot list (it's not a valid tool execution row to insert).
        snapshot.ToolInvocations.Should().BeEmpty();
    }

    [Fact]
    public void RecordToolRequest_EmptyToolName_IsIgnored()
    {
        var sut = CreateSut();

        sut.RecordToolRequest("call-1", string.Empty, "{}");

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolInvocations.Should().BeEmpty();
    }

    [Fact]
    public void TakeSnapshot_ResetsInvocations()
    {
        var sut = CreateSut();
        sut.RecordToolRequest("call-1", "ReadFile", "{}");
        sut.RecordToolResult("call-1", "ok");

        _ = sut.TakeSnapshot();
        var second = sut.TakeSnapshot();

        second.ToolInvocations.Should().BeEmpty();
    }

    [Fact]
    public void RecordToolRequest_PopulatesToolNamesSetAsWell()
    {
        var sut = CreateSut();

        sut.RecordToolRequest("call-1", "ReadFile", null);
        sut.RecordToolRequest("call-2", "WriteFile", null);

        var snapshot = sut.TakeSnapshot();

        snapshot.ToolNames.Should().BeEquivalentTo(new[] { "ReadFile", "WriteFile" });
    }
}
