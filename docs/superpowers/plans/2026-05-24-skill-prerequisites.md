# Skill Prerequisites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable skills to declare prerequisites and completion signals so tools from dependent skills are withheld until prerequisites complete.

**Architecture:** DelegatingChatClient middleware filters tools per-LLM-call based on prerequisite completion state tracked in an in-memory conversation-scoped tracker. Cycle detection via topological sort at agent creation time.

**Tech Stack:** C# .NET 10, Microsoft.Extensions.AI (DelegatingChatClient, ChatOptions), MediatR, xUnit/Moq

---

### Task 1: SkillDefinition — Add Prerequisites and CompletionTool

**Files:**
- Modify: `src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs`
- Test: `src/Content/Tests/Domain.AI.Tests/Skills/SkillDefinitionTests.cs`

- [ ] **Step 1: Write failing test for new properties**

```csharp
[Fact]
public void Prerequisites_DefaultsToEmptyList()
{
    var skill = new SkillDefinition();
    Assert.Empty(skill.Prerequisites);
    Assert.False(skill.HasPrerequisites);
}

[Fact]
public void CompletionTool_DefaultsToNull()
{
    var skill = new SkillDefinition();
    Assert.Null(skill.CompletionTool);
    Assert.False(skill.HasCompletionTool);
}

[Fact]
public void HasPrerequisites_TrueWhenPopulated()
{
    var skill = new SkillDefinition { Prerequisites = ["validate"] };
    Assert.True(skill.HasPrerequisites);
}

[Fact]
public void HasCompletionTool_TrueWhenSet()
{
    var skill = new SkillDefinition { CompletionTool = "run_validation" };
    Assert.True(skill.HasCompletionTool);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Domain.AI.Tests --filter "SkillDefinition" -v n`
Expected: FAIL — properties don't exist yet

- [ ] **Step 3: Add properties to SkillDefinition**

In `SkillDefinition.cs`, add to the `Runtime Configuration` region after `License`:

```csharp
/// <summary>
/// Skill IDs that must complete before this skill's tools become available.
/// Empty list means no prerequisites (always unlocked).
/// </summary>
public IList<string> Prerequisites { get; set; } = new List<string>();

/// <summary>
/// Tool name whose successful invocation signals this skill is complete.
/// Null means the skill is always considered complete (no gate).
/// </summary>
public string? CompletionTool { get; set; }
```

In the `Computed Properties` region, add:

```csharp
/// <summary>Whether this skill has prerequisite dependencies.</summary>
public bool HasPrerequisites => Prerequisites.Count > 0;

/// <summary>Whether this skill declares a completion tool gate.</summary>
public bool HasCompletionTool => !string.IsNullOrEmpty(CompletionTool);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Domain.AI.Tests --filter "SkillDefinition" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs src/Content/Tests/Domain.AI.Tests/Skills/SkillDefinitionTests.cs
git commit -m "feat(prerequisites): add Prerequisites and CompletionTool to SkillDefinition"
```

---

### Task 2: SkillMetadataParser — Parse prerequisites and completion_tool

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs`
- Test: `src/Content/Tests/Infrastructure.AI.Tests/Skills/SkillMetadataParserTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void ParseFromFile_ExtractsPrerequisites()
{
    var content = "---\nname: deploy\nprerequisites: [validate, test]\n---\nDeploy instructions";
    var path = WriteSkillFile(content);

    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    Assert.Equal(new[] { "validate", "test" }, result.Prerequisites);
}

[Fact]
public void ParseFromFile_ExtractsCompletionTool()
{
    var content = "---\nname: validate\ncompletion_tool: run_validation\n---\nValidation instructions";
    var path = WriteSkillFile(content);

    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    Assert.Equal("run_validation", result.CompletionTool);
}

[Fact]
public void ParseFromFile_NoPrerequisites_ReturnsEmptyList()
{
    var content = "---\nname: simple\n---\nSimple instructions";
    var path = WriteSkillFile(content);

    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    Assert.Empty(result.Prerequisites);
}

[Fact]
public void ParseFromFile_NoCompletionTool_ReturnsNull()
{
    var content = "---\nname: simple\n---\nSimple instructions";
    var path = WriteSkillFile(content);

    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    Assert.Null(result.CompletionTool);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "SkillMetadataParser" -v n`
Expected: FAIL — properties not set during parsing

- [ ] **Step 3: Add parsing logic**

In `SkillMetadataParser.ParseFromFile`, add two lines to the return block:

```csharp
Prerequisites = ParseList(frontmatter, "prerequisites"),
CompletionTool = ParseString(frontmatter, "completion_tool"),
```

In `SkillMetadataParser.Parse`, add the same two lines:

```csharp
Prerequisites = ParseList(rawFrontmatter, "prerequisites"),
CompletionTool = ParseString(rawFrontmatter, "completion_tool"),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "SkillMetadataParser" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs src/Content/Tests/Infrastructure.AI.Tests/Skills/SkillMetadataParserTests.cs
git commit -m "feat(prerequisites): parse prerequisites and completion_tool from SKILL.md"
```

---

### Task 3: ISkillCompletionTracker and InMemorySkillCompletionTracker

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Skills/ISkillCompletionTracker.cs`
- Create: `src/Content/Application/Application.AI.Common/Services/Skills/InMemorySkillCompletionTracker.cs`
- Test: `src/Content/Tests/Application.AI.Common.Tests/Services/Skills/InMemorySkillCompletionTrackerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
public class InMemorySkillCompletionTrackerTests
{
    private readonly InMemorySkillCompletionTracker _tracker = new();

    [Fact]
    public void IsCompleted_ReturnsFalse_WhenNotMarked()
    {
        Assert.False(_tracker.IsCompleted("conv1", "validate"));
    }

    [Fact]
    public void MarkCompleted_ThenIsCompleted_ReturnsTrue()
    {
        _tracker.MarkCompleted("conv1", "validate");
        Assert.True(_tracker.IsCompleted("conv1", "validate"));
    }

    [Fact]
    public void MarkCompleted_ScopedToConversation()
    {
        _tracker.MarkCompleted("conv1", "validate");
        Assert.False(_tracker.IsCompleted("conv2", "validate"));
    }

    [Fact]
    public void GetCompletedSkills_ReturnsAll()
    {
        _tracker.MarkCompleted("conv1", "validate");
        _tracker.MarkCompleted("conv1", "test");
        var completed = _tracker.GetCompletedSkills("conv1");
        Assert.Equal(new HashSet<string> { "validate", "test" }, completed);
    }

    [Fact]
    public void GetCompletedSkills_EmptyForUnknownConversation()
    {
        Assert.Empty(_tracker.GetCompletedSkills("unknown"));
    }

    [Fact]
    public void ClearConversation_RemovesState()
    {
        _tracker.MarkCompleted("conv1", "validate");
        _tracker.ClearConversation("conv1");
        Assert.False(_tracker.IsCompleted("conv1", "validate"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "InMemorySkillCompletionTracker" -v n`
Expected: FAIL — types don't exist

- [ ] **Step 3: Create ISkillCompletionTracker interface**

```csharp
namespace Application.AI.Common.Interfaces.Skills;

/// <summary>
/// Tracks skill completion state per conversation. Used by the prerequisite
/// system to determine when a skill's tools should be unlocked.
/// </summary>
public interface ISkillCompletionTracker
{
    void MarkCompleted(string conversationId, string skillId);
    bool IsCompleted(string conversationId, string skillId);
    IReadOnlySet<string> GetCompletedSkills(string conversationId);
    void ClearConversation(string conversationId);
}
```

- [ ] **Step 4: Create InMemorySkillCompletionTracker**

```csharp
using System.Collections.Concurrent;
using Application.AI.Common.Interfaces.Skills;

namespace Application.AI.Common.Services.Skills;

/// <summary>
/// In-memory, conversation-scoped tracker for skill completion state.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public sealed class InMemorySkillCompletionTracker : ISkillCompletionTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _state = new();
    private readonly Lock _lock = new();

    public void MarkCompleted(string conversationId, string skillId)
    {
        lock (_lock)
        {
            var set = _state.GetOrAdd(conversationId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            set.Add(skillId);
        }
    }

    public bool IsCompleted(string conversationId, string skillId)
    {
        lock (_lock)
        {
            return _state.TryGetValue(conversationId, out var set) && set.Contains(skillId);
        }
    }

    public IReadOnlySet<string> GetCompletedSkills(string conversationId)
    {
        lock (_lock)
        {
            if (_state.TryGetValue(conversationId, out var set))
                return new HashSet<string>(set, StringComparer.OrdinalIgnoreCase);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void ClearConversation(string conversationId)
    {
        _state.TryRemove(conversationId, out _);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "InMemorySkillCompletionTracker" -v n`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/Skills/ISkillCompletionTracker.cs src/Content/Application/Application.AI.Common/Services/Skills/InMemorySkillCompletionTracker.cs src/Content/Tests/Application.AI.Common.Tests/Services/Skills/InMemorySkillCompletionTrackerTests.cs
git commit -m "feat(prerequisites): add ISkillCompletionTracker with in-memory implementation"
```

---

### Task 4: SkillPrerequisiteMap model and cycle detection

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Models/SkillPrerequisiteMap.cs`
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs`
- Test: `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentFactoryTests.cs`

- [ ] **Step 1: Create SkillPrerequisiteMap model**

```csharp
namespace Application.AI.Common.Models;

/// <summary>
/// Prerequisite metadata for all skills in a multi-skill agent context.
/// Stashed in AgentExecutionContext.AdditionalProperties for the middleware to consume.
/// </summary>
public sealed class SkillPrerequisiteMap
{
    public const string AdditionalPropertiesKey = "__skillPrerequisites";

    public required IReadOnlyDictionary<string, SkillPrerequisiteEntry> Skills { get; init; }

    public bool HasAnyPrerequisites => Skills.Values.Any(e => e.Prerequisites.Count > 0);
}

/// <summary>
/// Prerequisite and completion metadata for a single skill.
/// </summary>
public sealed class SkillPrerequisiteEntry
{
    public required string SkillId { get; init; }
    public required IReadOnlyList<string> Prerequisites { get; init; }
    public string? CompletionTool { get; init; }
    public required IReadOnlyList<string> ToolNames { get; init; }
}
```

- [ ] **Step 2: Write failing tests for cycle detection**

```csharp
[Fact]
public async Task CreateAgentFromSkillsAsync_CyclicPrerequisites_Throws()
{
    var skillA = new SkillDefinition { Id = "a", Name = "a", Prerequisites = ["b"] };
    var skillB = new SkillDefinition { Id = "b", Name = "b", Prerequisites = ["a"] };
    SetupRegistryReturns("a", skillA);
    SetupRegistryReturns("b", skillB);

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _factory.CreateAgentFromSkillsAsync(["a", "b"], new SkillAgentOptions()));

    Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task CreateAgentFromSkillsAsync_PrerequisiteNotInSkillList_Throws()
{
    var skill = new SkillDefinition { Id = "deploy", Name = "deploy", Prerequisites = ["validate"] };
    SetupRegistryReturns("deploy", skill);

    var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        _factory.CreateAgentFromSkillsAsync(["deploy"], new SkillAgentOptions()));

    Assert.Contains("validate", ex.Message);
}

[Fact]
public async Task CreateAgentFromSkillsAsync_ValidPrerequisites_Succeeds()
{
    var skillA = new SkillDefinition { Id = "validate", Name = "validate", CompletionTool = "run_validation" };
    var skillB = new SkillDefinition { Id = "deploy", Name = "deploy", Prerequisites = ["validate"] };
    SetupRegistryReturns("validate", skillA);
    SetupRegistryReturns("deploy", skillB);

    // Should not throw
    await _factory.CreateAgentFromSkillsAsync(["validate", "deploy"], new SkillAgentOptions());
}
```

- [ ] **Step 3: Add cycle detection to AgentFactory.CreateAgentFromSkillsAsync**

After resolving all skills and before calling `MapToAgentContextAsync`, add:

```csharp
ValidatePrerequisites(skills);
```

Add the validation method:

```csharp
private static void ValidatePrerequisites(IReadOnlyList<SkillDefinition> skills)
{
    var skillIds = new HashSet<string>(skills.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

    // Check all referenced prerequisites exist in the skill list
    foreach (var skill in skills)
    {
        foreach (var prereq in skill.Prerequisites)
        {
            if (!skillIds.Contains(prereq))
                throw new InvalidOperationException(
                    $"Skill '{skill.Id}' declares prerequisite '{prereq}' which is not in the agent's skill list. " +
                    $"Available skills: [{string.Join(", ", skillIds)}]");
        }
    }

    // Topological sort to detect cycles (Kahn's algorithm)
    var inDegree = skills.ToDictionary(s => s.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
    var adj = skills.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);

    foreach (var skill in skills)
    {
        foreach (var prereq in skill.Prerequisites)
        {
            adj[prereq].Add(skill.Id);
            inDegree[skill.Id]++;
        }
    }

    var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
    var sorted = 0;

    while (queue.Count > 0)
    {
        var current = queue.Dequeue();
        sorted++;
        foreach (var dependent in adj[current])
        {
            inDegree[dependent]--;
            if (inDegree[dependent] == 0)
                queue.Enqueue(dependent);
        }
    }

    if (sorted != skills.Count)
    {
        var cycleSkills = inDegree.Where(kv => kv.Value > 0).Select(kv => kv.Key);
        throw new InvalidOperationException(
            $"Prerequisite cycle detected among skills: [{string.Join(", ", cycleSkills)}]. " +
            "Remove or restructure prerequisites to eliminate the cycle.");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "AgentFactory" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Models/SkillPrerequisiteMap.cs src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs src/Content/Tests/Application.AI.Common.Tests/Factories/AgentFactoryTests.cs
git commit -m "feat(prerequisites): add SkillPrerequisiteMap model and cycle detection"
```

---

### Task 5: AgentExecutionContextFactory — Compute prerequisite metadata

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs`
- Test: `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task MapToAgentContextAsync_WithPrerequisites_StashesMapInAdditionalProperties()
{
    var validate = new SkillDefinition
    {
        Id = "validate", Name = "validate",
        CompletionTool = "run_validation",
        AllowedTools = ["check_syntax", "run_validation"]
    };
    var deploy = new SkillDefinition
    {
        Id = "deploy", Name = "deploy",
        Prerequisites = ["validate"],
        AllowedTools = ["deploy_execute"]
    };

    var context = await _factory.MapToAgentContextAsync([validate, deploy], new SkillAgentOptions());

    Assert.True(context.AdditionalProperties.ContainsKey(SkillPrerequisiteMap.AdditionalPropertiesKey));
    var map = (SkillPrerequisiteMap)context.AdditionalProperties[SkillPrerequisiteMap.AdditionalPropertiesKey];
    Assert.True(map.HasAnyPrerequisites);
    Assert.Equal(["validate"], map.Skills["deploy"].Prerequisites);
    Assert.Equal("run_validation", map.Skills["validate"].CompletionTool);
}

[Fact]
public async Task MapToAgentContextAsync_NoPrerequisites_NoMapInAdditionalProperties()
{
    var skill = new SkillDefinition { Id = "simple", Name = "simple" };

    var context = await _factory.MapToAgentContextAsync([skill], new SkillAgentOptions());

    Assert.False(context.AdditionalProperties.ContainsKey(SkillPrerequisiteMap.AdditionalPropertiesKey));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "AgentExecutionContextFactory" -v n`
Expected: FAIL

- [ ] **Step 3: Add prerequisite map computation to MapToAgentContextAsync**

After `BuildMergedToolsAsync`, compute the prerequisite map:

```csharp
var prerequisiteMap = BuildPrerequisiteMap(skills, tools);
```

Before returning the context, add to `additionalProps` if any prerequisites exist:

```csharp
if (prerequisiteMap.HasAnyPrerequisites)
    additionalProps[SkillPrerequisiteMap.AdditionalPropertiesKey] = prerequisiteMap;
```

Add the builder method:

```csharp
private static SkillPrerequisiteMap BuildPrerequisiteMap(
    IReadOnlyList<SkillDefinition> skills,
    IReadOnlyList<AITool> allTools)
{
    // Build tool name → skill ID mapping by checking which skill declared each tool
    var entries = new Dictionary<string, SkillPrerequisiteEntry>(StringComparer.OrdinalIgnoreCase);

    foreach (var skill in skills)
    {
        var skillToolNames = new List<string>();

        // Collect tool names that belong to this skill
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (skill.AllowedTools?.Count > 0)
            foreach (var t in skill.AllowedTools) declaredNames.Add(t);
        if (skill.ToolDeclarations?.Count > 0)
            foreach (var td in skill.ToolDeclarations) declaredNames.Add(td.Name);
        if (skill.Tools?.Count > 0)
            foreach (var t in skill.Tools) declaredNames.Add(t.Name);

        foreach (var tool in allTools)
        {
            if (declaredNames.Contains(tool.Name))
                skillToolNames.Add(tool.Name);
        }

        entries[skill.Id] = new SkillPrerequisiteEntry
        {
            SkillId = skill.Id,
            Prerequisites = skill.Prerequisites.ToList(),
            CompletionTool = skill.CompletionTool,
            ToolNames = skillToolNames
        };
    }

    return new SkillPrerequisiteMap { Skills = entries };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "AgentExecutionContextFactory" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryTests.cs
git commit -m "feat(prerequisites): compute SkillPrerequisiteMap in context factory"
```

---

### Task 6: SkillPrerequisiteMiddleware — Per-turn tool filtering

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Middleware/SkillPrerequisiteMiddleware.cs`
- Test: `src/Content/Tests/Application.AI.Common.Tests/Middleware/SkillPrerequisiteMiddlewareTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
public class SkillPrerequisiteMiddlewareTests
{
    [Fact]
    public async Task FiltersToolsFromBlockedSkills()
    {
        // validate skill has "check" tool, deploy skill has "deploy_exec" tool
        // deploy requires validate. Validate is NOT completed.
        // Expect: "deploy_exec" tool is filtered out.
        var map = CreateMap(
            ("validate", [], null, ["check"]),
            ("deploy", ["validate"], null, ["deploy_exec"]));
        var tracker = new InMemorySkillCompletionTracker();

        var middleware = CreateMiddleware(map, tracker, "conv1",
            tools: [MockTool("check"), MockTool("deploy_exec")]);

        var response = await middleware.GetResponseAsync([], CreateOptions(["check", "deploy_exec"]));

        // The inner client should have received only "check"
        Assert.Single(_capturedOptions!.Tools!);
        Assert.Equal("check", _capturedOptions.Tools![0].Name);
    }

    [Fact]
    public async Task UnlocksToolsWhenPrerequisiteCompletes()
    {
        var map = CreateMap(
            ("validate", [], "check", ["check"]),
            ("deploy", ["validate"], null, ["deploy_exec"]));
        var tracker = new InMemorySkillCompletionTracker();
        tracker.MarkCompleted("conv1", "validate");

        var middleware = CreateMiddleware(map, tracker, "conv1",
            tools: [MockTool("check"), MockTool("deploy_exec")]);

        await middleware.GetResponseAsync([], CreateOptions(["check", "deploy_exec"]));

        Assert.Equal(2, _capturedOptions!.Tools!.Count);
    }

    [Fact]
    public async Task DetectsCompletionToolInResponse()
    {
        var map = CreateMap(
            ("validate", [], "run_validation", ["run_validation"]));
        var tracker = new InMemorySkillCompletionTracker();

        // Inner client returns a response with FunctionCallContent for "run_validation"
        var middleware = CreateMiddlewareWithToolCallResponse(map, tracker, "conv1", "run_validation");

        await middleware.GetResponseAsync([], CreateOptions(["run_validation"]));

        Assert.True(tracker.IsCompleted("conv1", "validate"));
    }

    [Fact]
    public async Task PassesThroughWhenNoPrerequisiteMap()
    {
        // No SkillPrerequisiteMap — all tools pass through unchanged
        var middleware = CreateMiddlewareWithoutMap(tools: [MockTool("any_tool")]);

        await middleware.GetResponseAsync([], CreateOptions(["any_tool"]));

        Assert.Single(_capturedOptions!.Tools!);
    }

    [Fact]
    public async Task SkillWithoutCompletionTool_IsAlwaysComplete()
    {
        // Skill "auto" has no completion_tool — always complete
        // Skill "gated" requires "auto"
        var map = CreateMap(
            ("auto", [], null, ["auto_tool"]),
            ("gated", ["auto"], null, ["gated_tool"]));
        var tracker = new InMemorySkillCompletionTracker();

        var middleware = CreateMiddleware(map, tracker, "conv1",
            tools: [MockTool("auto_tool"), MockTool("gated_tool")]);

        await middleware.GetResponseAsync([], CreateOptions(["auto_tool", "gated_tool"]));

        // Both available because "auto" has no completion_tool → always complete
        Assert.Equal(2, _capturedOptions!.Tools!.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "SkillPrerequisiteMiddleware" -v n`
Expected: FAIL — type doesn't exist

- [ ] **Step 3: Implement SkillPrerequisiteMiddleware**

```csharp
using Application.AI.Common.Interfaces.Skills;
using Application.AI.Common.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Application.AI.Common.Middleware;

/// <summary>
/// Chat client middleware that filters tools per-LLM-call based on skill prerequisite
/// completion state. Tools from skills whose prerequisites haven't completed are withheld.
/// Also detects completion tool invocations in responses and marks skills as complete.
/// </summary>
public sealed class SkillPrerequisiteMiddleware : DelegatingChatClient
{
    private readonly ISkillCompletionTracker _tracker;
    private readonly SkillPrerequisiteMap _map;
    private readonly string _conversationId;
    private readonly ILogger _logger;

    public SkillPrerequisiteMiddleware(
        IChatClient innerClient,
        ISkillCompletionTracker tracker,
        SkillPrerequisiteMap map,
        string conversationId,
        ILogger<SkillPrerequisiteMiddleware> logger)
        : base(innerClient)
    {
        _tracker = tracker;
        _map = map;
        _conversationId = conversationId;
        _logger = logger;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options = FilterBlockedTools(options);
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        DetectCompletions(response);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        options = FilterBlockedTools(options);
        await foreach (var chunk in base.GetStreamingResponseAsync(messages, options, cancellationToken))
            yield return chunk;
    }

    private ChatOptions? FilterBlockedTools(ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
            return options;

        var blockedTools = GetBlockedToolNames();
        if (blockedTools.Count == 0)
            return options;

        var filtered = options.Tools.Where(t => !blockedTools.Contains(t.Name)).ToList();

        if (filtered.Count == options.Tools.Count)
            return options;

        _logger.LogInformation(
            "[Prerequisites] Withheld {Count} tool(s) from blocked skills: {Tools}",
            options.Tools.Count - filtered.Count,
            string.Join(", ", blockedTools));

        var cloned = options.Clone();
        cloned.Tools = filtered;
        return cloned;
    }

    private HashSet<string> GetBlockedToolNames()
    {
        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _map.Skills.Values)
        {
            if (entry.Prerequisites.Count == 0)
                continue;

            var allPrereqsMet = entry.Prerequisites.All(prereqId =>
            {
                // A prerequisite is met if:
                // 1. It has no completion_tool (always complete), OR
                // 2. It's been marked complete in the tracker
                if (_map.Skills.TryGetValue(prereqId, out var prereqEntry)
                    && prereqEntry.CompletionTool is null)
                    return true;

                return _tracker.IsCompleted(_conversationId, prereqId);
            });

            if (!allPrereqsMet)
            {
                foreach (var toolName in entry.ToolNames)
                    blocked.Add(toolName);
            }
        }

        return blocked;
    }

    private void DetectCompletions(ChatResponse response)
    {
        var toolCalls = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(fc => fc.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (toolCalls.Count == 0)
            return;

        foreach (var entry in _map.Skills.Values)
        {
            if (entry.CompletionTool is null)
                continue;

            if (toolCalls.Contains(entry.CompletionTool)
                && !_tracker.IsCompleted(_conversationId, entry.SkillId))
            {
                _tracker.MarkCompleted(_conversationId, entry.SkillId);
                _logger.LogInformation(
                    "[Prerequisites] Skill '{SkillId}' completed via tool '{CompletionTool}'",
                    entry.SkillId, entry.CompletionTool);
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "SkillPrerequisiteMiddleware" -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Middleware/SkillPrerequisiteMiddleware.cs src/Content/Tests/Application.AI.Common.Tests/Middleware/SkillPrerequisiteMiddlewareTests.cs
git commit -m "feat(prerequisites): add SkillPrerequisiteMiddleware for per-turn tool filtering"
```

---

### Task 7: Wire middleware into AgentFactory and register DI

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs`
- Modify: `src/Content/Application/Application.AI.Common/DependencyInjection.cs`
- Test: Integration tests in `src/Content/Tests/Application.Core.Tests/CQRS/AgentPipelineIntegrationTests.cs`

- [ ] **Step 1: Add ISkillCompletionTracker to AgentFactory constructor**

Add field and constructor parameter:

```csharp
private readonly ISkillCompletionTracker _completionTracker;
```

- [ ] **Step 2: Wire SkillPrerequisiteMiddleware in CreateAgentAsync**

After `ToolDiagnosticsMiddleware` and before `UseDistributedCache`, add:

```csharp
// Prerequisite filtering — only when the context has prerequisite metadata
if (agentContext.AdditionalProperties?.TryGetValue(
    SkillPrerequisiteMap.AdditionalPropertiesKey, out var prereqObj) == true
    && prereqObj is SkillPrerequisiteMap prereqMap
    && prereqMap.HasAnyPrerequisites)
{
    var conversationId = agentContext.AdditionalProperties.TryGetValue("conversationId", out var convId)
        ? convId.ToString()! : Guid.NewGuid().ToString();

    chatClientBuilder = chatClientBuilder.Use(inner =>
        new Middleware.SkillPrerequisiteMiddleware(
            inner, _completionTracker, prereqMap, conversationId,
            _loggerFactory.CreateLogger<Middleware.SkillPrerequisiteMiddleware>()));
}
```

- [ ] **Step 3: Register ISkillCompletionTracker in DependencyInjection.cs**

Add after the conversation cache registration:

```csharp
services.AddSingleton<ISkillCompletionTracker, InMemorySkillCompletionTracker>();
```

- [ ] **Step 4: Write integration test**

```csharp
[Fact]
public void DependencyInjection_RegistersSkillCompletionTracker()
{
    var tracker = _serviceProvider.GetService<ISkillCompletionTracker>();
    Assert.NotNull(tracker);
    Assert.IsType<InMemorySkillCompletionTracker>(tracker);
}
```

- [ ] **Step 5: Run full build and tests**

Run: `dotnet build src/AgenticHarness.slnx && dotnet test src/AgenticHarness.slnx`
Expected: Build succeeds, all new tests pass

- [ ] **Step 6: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs src/Content/Application/Application.AI.Common/DependencyInjection.cs src/Content/Tests/Application.Core.Tests/CQRS/AgentPipelineIntegrationTests.cs
git commit -m "feat(prerequisites): wire middleware and register DI"
```

---

### Task 8: Full solution build and verification

- [ ] **Step 1: Full build**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/AgenticHarness.slnx`
Expected: All new tests pass. Pre-existing failures (AgentHub EF Core, FileSystem) unchanged.

- [ ] **Step 3: Grep for consistency**

Verify no stale references:
- `CompletionTool` used consistently
- `Prerequisites` parsed and consumed correctly
- No orphaned imports

- [ ] **Step 4: Mark complete**
