using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.GitOps;
using FluentAssertions;
using Infrastructure.AI.GitOps;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Infrastructure.AI.Tests.GitOps;

/// <summary>
/// Tests <see cref="K8sGptMcpClient"/> against a mocked <see cref="IMcpToolProvider"/>.
/// Covers successful analyze deserialization, the missing-tool path, the
/// unconfigured-server path, and MCP-side failure surfacing as a stable code.
/// </summary>
public sealed class K8sGptMcpClientTests
{
    private readonly Mock<IMcpToolProvider> _mcp = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-06-08T12:00:00Z"));

    private K8sGptMcpClient CreateSut(string serverName = GitOpsTestConfig.K8sGptServerName)
    {
        var appConfig = GitOpsTestConfig.ValidAppConfig();
        appConfig.AI.GitOps.K8sGptMcpServerName = serverName;
        var config = GitOpsTestConfig.Monitor(appConfig);
        return new K8sGptMcpClient(_mcp.Object, config, NullLogger<K8sGptMcpClient>.Instance, _time);
    }

    // Returns the structured analyze response the way an MCP tool surfaces it:
    // a JSON object (Microsoft.Extensions.AI hands the harness a JsonElement of
    // ValueKind.Object when the tool delegate returns an object graph).
    private static AIFunction AnalyzeTool(object response)
        => AIFunctionFactory.Create(() => response, new AIFunctionFactoryOptions { Name = "analyze" });

    private static AIFunction EmptyResultsTool()
        => AnalyzeTool(new Dictionary<string, object?> { ["results"] = Array.Empty<object>() });

    private static AIFunction NamedTool(string name)
        => AIFunctionFactory.Create(() => new Dictionary<string, object?>(), new AIFunctionFactoryOptions { Name = name });

    [Fact]
    public async Task AnalyzeAsync_SuccessfulAnalysis_DeserializesFindingsAndExplanation()
    {
        var response = new Dictionary<string, object?>
        {
            ["results"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["kind"] = "Pod", ["name"] = "web-abc", ["namespace"] = "prod",
                    ["error"] = "ImagePullBackOff", ["severity"] = "high"
                },
                new Dictionary<string, object?>
                {
                    ["kind"] = "Deployment", ["name"] = "api", ["namespace"] = "prod",
                    ["error"] = "replica mismatch", ["severity"] = "warning"
                }
            },
            ["explanation"] = "Two workloads are unhealthy."
        };
        _mcp.Setup(m => m.GetToolsAsync(GitOpsTestConfig.K8sGptServerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnalyzeTool(response)]);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync(new K8sGptAnalysisRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().HaveCount(2);
        result.Value.Findings[0].Severity.Should().Be(K8sGptSeverity.High);
        result.Value.Findings[1].Severity.Should().Be(K8sGptSeverity.Medium);
        result.Value.Explanation.Should().Be("Two workloads are unhealthy.");
        result.Value.CapturedAt.Should().Be(_time.GetUtcNow());
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyResults_ReturnsSuccessWithNoFindings()
    {
        _mcp.Setup(m => m.GetToolsAsync(GitOpsTestConfig.K8sGptServerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([EmptyResultsTool()]);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync(new K8sGptAnalysisRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().BeEmpty();
        result.Value.Explanation.Should().BeNull();
    }

    [Fact]
    public async Task AnalyzeAsync_ResponseIsJsonStringElement_UnwrapsAndDeserializes()
    {
        // Some MCP transports return the analyze payload as a JSON *string* rather
        // than an object graph; Microsoft.Extensions.AI surfaces that as a
        // JsonElement of ValueKind.String. The client must unwrap it to the inner
        // JSON and parse the object — not treat it as a bare string literal.
        const string jsonPayload =
            "{\"results\":[{\"kind\":\"Pod\",\"name\":\"web-xyz\",\"namespace\":\"prod\"," +
            "\"error\":\"CrashLoopBackOff\",\"severity\":\"high\"}]," +
            "\"explanation\":\"One pod is crashing.\"}";
        _mcp.Setup(m => m.GetToolsAsync(GitOpsTestConfig.K8sGptServerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AnalyzeTool(jsonPayload)]);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync(new K8sGptAnalysisRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Findings.Should().ContainSingle();
        result.Value.Findings[0].Name.Should().Be("web-xyz");
        result.Value.Findings[0].Severity.Should().Be(K8sGptSeverity.High);
        result.Value.Explanation.Should().Be("One pod is crashing.");
    }

    [Fact]
    public async Task AnalyzeAsync_AnalyzeToolMissing_ReturnsFailWithStableCode()
    {
        _mcp.Setup(m => m.GetToolsAsync(GitOpsTestConfig.K8sGptServerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([NamedTool("some_other_tool")]);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync(new K8sGptAnalysisRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.k8sgpt.analyze_tool_missing");
    }

    [Fact]
    public async Task AnalyzeAsync_ServerNameNotConfigured_ReturnsFailWithStableCode()
    {
        var sut = CreateSut(serverName: "");

        var result = await sut.AnalyzeAsync(new K8sGptAnalysisRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.k8sgpt.server_name_not_configured");
        _mcp.Verify(m => m.GetToolsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnalyzeAsync_McpProviderThrows_ReturnsUnexpectedErrorCode()
    {
        _mcp.Setup(m => m.GetToolsAsync(GitOpsTestConfig.K8sGptServerName, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("mcp down"));
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync(new K8sGptAnalysisRequest(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().Contain("gitops.k8sgpt.unexpected_error");
    }

    [Fact]
    public async Task AnalyzeAsync_PassesRequestArgumentsToAnalyzeTool()
    {
        AIFunctionArguments? captured = null;
        var probe = AIFunctionFactory.Create(
            (AIFunctionArguments args) =>
            {
                captured = args;
                return (object)new Dictionary<string, object?> { ["results"] = Array.Empty<object>() };
            },
            new AIFunctionFactoryOptions { Name = "analyze" });
        _mcp.Setup(m => m.GetToolsAsync(GitOpsTestConfig.K8sGptServerName, It.IsAny<CancellationToken>()))
            .ReturnsAsync([probe]);
        var sut = CreateSut();

        var result = await sut.AnalyzeAsync(
            new K8sGptAnalysisRequest { Namespace = "prod", Filters = ["Pod"], Explain = false },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!["namespace"].Should().Be("prod");
        captured["explain"].Should().Be(false);
    }
}
