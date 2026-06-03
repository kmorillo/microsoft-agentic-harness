using Domain.AI.Telemetry.Conventions;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Telemetry;

/// <summary>
/// Tests for <see cref="GenAiSemconvRegistry"/> — the single re-export +
/// gap-fill registry for OpenTelemetry GenAI semantic-convention keys.
/// </summary>
/// <remarks>
/// Two test groups:
/// <list type="bullet">
///   <item><description>
///     <see cref="ReExports_MatchSourceConstants"/> — every key declared
///     here as a re-export of another <c>Conventions/*.cs</c> constant
///     MUST equal the source constant at compile time. This catches
///     accidental string drift if either side is edited in isolation.
///   </description></item>
///   <item><description>
///     <see cref="NetNewKeys_HaveExpectedValues"/> — every key NEWLY
///     declared in this registry (not re-exported) MUST hold the literal
///     value the OTel SemConv spec defines. Drift between registry and
///     spec surfaces here before it surfaces in dashboards.
///   </description></item>
/// </list>
/// </remarks>
public sealed class GenAiSemconvRegistryTests
{
    [Theory]
    [InlineData(GenAiSemconvRegistry.System, AgentConventions.GenAiSystem)]
    [InlineData(GenAiSemconvRegistry.OperationName, ToolConventions.GenAiOperationName)]
    [InlineData(GenAiSemconvRegistry.OperationChat, ToolConventions.ChatOperation)]
    [InlineData(GenAiSemconvRegistry.OperationTextCompletion, ToolConventions.TextCompletionOperation)]
    [InlineData(GenAiSemconvRegistry.OperationEmbeddings, ToolConventions.EmbeddingsOperation)]
    [InlineData(GenAiSemconvRegistry.OperationExecuteTool, ToolConventions.ExecuteToolOperation)]
    [InlineData(GenAiSemconvRegistry.OperationInvokeAgent, ToolConventions.InvokeAgentOperation)]
    [InlineData(GenAiSemconvRegistry.RequestModel, TokenConventions.GenAiRequestModel)]
    [InlineData(GenAiSemconvRegistry.UsageInputTokens, TokenConventions.GenAiInputTokens)]
    [InlineData(GenAiSemconvRegistry.UsageOutputTokens, TokenConventions.GenAiOutputTokens)]
    [InlineData(GenAiSemconvRegistry.UsageCacheReadInputTokens, TokenConventions.GenAiCacheReadTokens)]
    [InlineData(GenAiSemconvRegistry.UsageCacheCreationInputTokens, TokenConventions.GenAiCacheWriteTokens)]
    [InlineData(GenAiSemconvRegistry.ToolName, ToolConventions.GenAiToolName)]
    [InlineData(GenAiSemconvRegistry.ToolCallId, ToolConventions.GenAiToolCallId)]
    [InlineData(GenAiSemconvRegistry.ToolType, ToolConventions.GenAiToolType)]
    [InlineData(GenAiSemconvRegistry.ToolDescription, ToolConventions.GenAiToolDescription)]
    [InlineData(GenAiSemconvRegistry.ToolCallArguments, ToolConventions.ToolCallArguments)]
    [InlineData(GenAiSemconvRegistry.ToolCallResult, ToolConventions.ToolCallResult)]
    [InlineData(GenAiSemconvRegistry.LegacyConversationId, AgentConventions.ConversationId)]
    [InlineData(GenAiSemconvRegistry.HarnessCandidateId, ToolConventions.HarnessCandidateId)]
    [InlineData(GenAiSemconvRegistry.HarnessIteration, ToolConventions.HarnessIteration)]
    public void ReExports_MatchSourceConstants(string reExport, string source)
    {
        reExport.Should().Be(source);
    }

    [Theory]
    [InlineData(GenAiSemconvRegistry.SystemIntended, "gen_ai.harness.system.intended")]
    [InlineData(GenAiSemconvRegistry.RequestMaxTokens, "gen_ai.request.max_tokens")]
    [InlineData(GenAiSemconvRegistry.RequestTemperature, "gen_ai.request.temperature")]
    [InlineData(GenAiSemconvRegistry.RequestTopP, "gen_ai.request.top_p")]
    [InlineData(GenAiSemconvRegistry.RequestTopK, "gen_ai.request.top_k")]
    [InlineData(GenAiSemconvRegistry.RequestStopSequences, "gen_ai.request.stop_sequences")]
    [InlineData(GenAiSemconvRegistry.ResponseId, "gen_ai.response.id")]
    [InlineData(GenAiSemconvRegistry.ResponseModel, "gen_ai.response.model")]
    [InlineData(GenAiSemconvRegistry.ResponseFinishReasons, "gen_ai.response.finish_reasons")]
    [InlineData(GenAiSemconvRegistry.AgentName, "gen_ai.agent.name")]
    [InlineData(GenAiSemconvRegistry.AgentId, "gen_ai.agent.id")]
    [InlineData(GenAiSemconvRegistry.AgentDescription, "gen_ai.agent.description")]
    [InlineData(GenAiSemconvRegistry.ConversationId, "gen_ai.conversation.id")]
    [InlineData(GenAiSemconvRegistry.OutputType, "gen_ai.output.type")]
    [InlineData(GenAiSemconvRegistry.ErrorType, "error.type")]
    [InlineData(GenAiSemconvRegistry.HarnessSkillName, "gen_ai.harness.skill.name")]
    [InlineData(GenAiSemconvRegistry.HarnessSkillMode, "gen_ai.harness.skill.mode")]
    [InlineData(GenAiSemconvRegistry.HarnessPluginId, "gen_ai.harness.plugin.id")]
    public void NetNewKeys_HaveExpectedValues(string actual, string expected)
    {
        actual.Should().Be(expected);
    }

    [Fact]
    public void ConversationId_And_LegacyConversationId_AreDistinct_ForParallelEmission()
    {
        // The blueprint's parallel-emit contract requires both keys be emitted
        // on every agent span during the migration. They must hold different
        // string values so a single Activity.SetTag for each lands as two
        // separate attributes.
        GenAiSemconvRegistry.ConversationId
            .Should().NotBe(GenAiSemconvRegistry.LegacyConversationId);
    }
}
