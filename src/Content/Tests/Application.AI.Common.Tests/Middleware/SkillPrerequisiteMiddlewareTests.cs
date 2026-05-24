using Application.AI.Common.Interfaces.Skills;
using Application.AI.Common.Middleware;
using Application.AI.Common.Models;
using Application.AI.Common.Services.Skills;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.AI.Common.Tests.Middleware;

/// <summary>
/// Tests for <see cref="SkillPrerequisiteMiddleware"/> covering tool filtering
/// when prerequisites are unmet, tool unlocking on completion, completion detection
/// from LLM responses, transitive prerequisites, and options immutability.
/// </summary>
public sealed class SkillPrerequisiteMiddlewareTests
{
    private ChatOptions? _capturedOptions;

    private SkillPrerequisiteMiddleware CreateMiddleware(
        SkillPrerequisiteMap map,
        ISkillCompletionTracker tracker,
        string conversationId,
        ChatResponse? response = null)
    {
        var inner = new CapturingChatClient(
            opts => _capturedOptions = opts,
            response ?? CreateEmptyResponse());

        return new SkillPrerequisiteMiddleware(
            inner, tracker, map, conversationId,
            NullLogger<SkillPrerequisiteMiddleware>.Instance);
    }

    [Fact]
    public async Task FiltersToolsFromBlockedSkills()
    {
        // "validate" has a completion tool, so it's NOT automatically complete.
        // "deploy" depends on "validate", and "validate" hasn't been marked complete.
        var map = CreateMap(
            CreateEntry("validate", [], "check", ["check"]),
            CreateEntry("deploy", ["validate"], null, ["deploy_exec"]));
        var tracker = new InMemorySkillCompletionTracker();
        var middleware = CreateMiddleware(map, tracker, "conv1");

        var options = new ChatOptions { Tools = [CreateTool("check"), CreateTool("deploy_exec")] };
        await middleware.GetResponseAsync([], options);

        Assert.NotNull(_capturedOptions?.Tools);
        Assert.Single(_capturedOptions.Tools);
        Assert.Equal("check", _capturedOptions.Tools[0].Name);
    }

    [Fact]
    public async Task UnlocksToolsWhenPrerequisiteIsCompleted()
    {
        var map = CreateMap(
            CreateEntry("validate", [], "check", ["check"]),
            CreateEntry("deploy", ["validate"], null, ["deploy_exec"]));
        var tracker = new InMemorySkillCompletionTracker();
        tracker.MarkCompleted("conv1", "validate");
        var middleware = CreateMiddleware(map, tracker, "conv1");

        var options = new ChatOptions { Tools = [CreateTool("check"), CreateTool("deploy_exec")] };
        await middleware.GetResponseAsync([], options);

        Assert.Equal(2, _capturedOptions!.Tools!.Count);
    }

    [Fact]
    public async Task DetectsCompletionToolInResponse()
    {
        var map = CreateMap(
            CreateEntry("validate", [], "run_validation", ["run_validation"]));
        var tracker = new InMemorySkillCompletionTracker();

        var responseWithToolCall = CreateResponseWithToolCall("run_validation");
        var middleware = CreateMiddleware(map, tracker, "conv1", responseWithToolCall);

        var options = new ChatOptions { Tools = [CreateTool("run_validation")] };
        await middleware.GetResponseAsync([], options);

        Assert.True(tracker.IsCompleted("conv1", "validate"));
    }

    [Fact]
    public async Task PassesThroughAllToolsWhenNoPrerequisites()
    {
        var map = CreateMap(
            CreateEntry("skill1", [], null, ["tool1"]),
            CreateEntry("skill2", [], null, ["tool2"]));
        var tracker = new InMemorySkillCompletionTracker();
        var middleware = CreateMiddleware(map, tracker, "conv1");

        var options = new ChatOptions { Tools = [CreateTool("tool1"), CreateTool("tool2")] };
        await middleware.GetResponseAsync([], options);

        Assert.Equal(2, _capturedOptions!.Tools!.Count);
    }

    [Fact]
    public async Task SkillWithoutCompletionTool_IsAlwaysComplete()
    {
        // "auto" has no completion tool -> always complete.
        // "gated" depends on "auto" -> prereq satisfied -> tools available.
        var map = CreateMap(
            CreateEntry("auto", [], null, ["auto_tool"]),
            CreateEntry("gated", ["auto"], null, ["gated_tool"]));
        var tracker = new InMemorySkillCompletionTracker();
        var middleware = CreateMiddleware(map, tracker, "conv1");

        var options = new ChatOptions { Tools = [CreateTool("auto_tool"), CreateTool("gated_tool")] };
        await middleware.GetResponseAsync([], options);

        Assert.Equal(2, _capturedOptions!.Tools!.Count);
    }

    [Fact]
    public async Task TransitivePrerequisites_BlocksUntilAllMet()
    {
        // C requires B, B requires A. Only A is complete.
        var map = CreateMap(
            CreateEntry("a", [], "tool_a", ["tool_a"]),
            CreateEntry("b", ["a"], "tool_b", ["tool_b"]),
            CreateEntry("c", ["b"], null, ["tool_c"]));
        var tracker = new InMemorySkillCompletionTracker();
        tracker.MarkCompleted("conv1", "a");
        var middleware = CreateMiddleware(map, tracker, "conv1");

        var options = new ChatOptions { Tools = [CreateTool("tool_a"), CreateTool("tool_b"), CreateTool("tool_c")] };
        await middleware.GetResponseAsync([], options);

        // tool_a: available (no prereqs on skill "a")
        // tool_b: available (prereq "a" is complete)
        // tool_c: blocked (prereq "b" is NOT complete -- has completion_tool "tool_b" but not marked)
        Assert.Equal(2, _capturedOptions!.Tools!.Count);
        Assert.Contains(_capturedOptions.Tools, t => t.Name == "tool_a");
        Assert.Contains(_capturedOptions.Tools, t => t.Name == "tool_b");
    }

    [Fact]
    public async Task DoesNotMutateOriginalOptions()
    {
        // "validate" has a completion tool so it's gated (not auto-complete).
        // "deploy" depends on "validate" which hasn't been marked -> deploy_exec is blocked.
        // Middleware should clone options to filter, leaving the original untouched.
        var map = CreateMap(
            CreateEntry("validate", [], "check", ["check"]),
            CreateEntry("deploy", ["validate"], null, ["deploy_exec"]));
        var tracker = new InMemorySkillCompletionTracker();
        var middleware = CreateMiddleware(map, tracker, "conv1");

        var originalTools = new List<AITool> { CreateTool("check"), CreateTool("deploy_exec") };
        var options = new ChatOptions { Tools = originalTools };
        await middleware.GetResponseAsync([], options);

        // Original options should still have both tools
        Assert.Equal(2, options.Tools!.Count);
    }

    [Fact]
    public async Task NullOptions_PassesThrough()
    {
        var map = CreateMap(
            CreateEntry("skill", [], null, ["tool"]));
        var tracker = new InMemorySkillCompletionTracker();
        var middleware = CreateMiddleware(map, tracker, "conv1");

        await middleware.GetResponseAsync([], null);

        Assert.Null(_capturedOptions);
    }

    // --- Helpers ---

    private static SkillPrerequisiteMap CreateMap(params SkillPrerequisiteEntry[] entries)
    {
        var dict = entries.ToDictionary(
            e => e.SkillId,
            e => e,
            StringComparer.OrdinalIgnoreCase);
        return new SkillPrerequisiteMap { Skills = dict };
    }

    private static SkillPrerequisiteEntry CreateEntry(
        string skillId, string[] prerequisites, string? completionTool, string[] toolNames)
    {
        return new SkillPrerequisiteEntry
        {
            SkillId = skillId,
            Prerequisites = prerequisites,
            CompletionTool = completionTool,
            ToolNames = toolNames
        };
    }

    private static AITool CreateTool(string name)
    {
        return AIFunctionFactory.Create(() => "result", name);
    }

    private static ChatResponse CreateEmptyResponse()
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, "OK")]);
    }

    private static ChatResponse CreateResponseWithToolCall(string toolName)
    {
        var message = new ChatMessage(ChatRole.Assistant, [
            new FunctionCallContent(Guid.NewGuid().ToString(), toolName)
        ]);
        return new ChatResponse([message]);
    }

    /// <summary>
    /// Simple test chat client that captures options and returns a configurable response.
    /// </summary>
    private sealed class CapturingChatClient : IChatClient
    {
        private readonly Action<ChatOptions?> _capture;
        private readonly ChatResponse _response;

        public CapturingChatClient(Action<ChatOptions?> capture, ChatResponse response)
        {
            _capture = capture;
            _response = response;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _capture(options);
            return Task.FromResult(_response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _capture(options);
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public void Dispose() { }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
