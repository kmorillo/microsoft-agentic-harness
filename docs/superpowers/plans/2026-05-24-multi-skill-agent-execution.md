# Multi-Skill Agent Execution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable agents to compose multiple skills into a single execution context — merged instructions, merged tools, LLM self-orchestrates.

**Architecture:** Widen the pipeline from single-skill to multi-skill at each touchpoint: `AgentDefinition` → `ExecuteAgentTurnCommandHandler` → `AgentConversationCache` → `AgentFactory` → `AgentExecutionContextFactory`. No new classes — existing types accept lists. Backward compatible via single-element list path.

**Tech Stack:** C# .NET 10, xUnit, Moq, FluentAssertions, MediatR, Microsoft.Agents.AI

**Design Spec:** `documentation/design/multi-skill-agent-execution.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Content/Domain/Domain.AI/Agents/AgentDefinition.cs` | Modify | `Skill` → `Skills` (list) |
| `src/Content/Infrastructure/Infrastructure.AI/Agents/AgentMetadataParser.cs` | Modify | Parse `skills:` list from YAML frontmatter |
| `src/Content/Application/Application.AI.Common/Interfaces/IAgentConversationCache.cs` | Modify | Widen `skillId` → `skillIds` |
| `src/Content/Application/Application.AI.Common/Services/AgentConversationCache.cs` | Modify | Pass list to factory |
| `src/Content/Application/Application.AI.Common/Interfaces/IAgentFactory.cs` | Modify | Add `CreateAgentFromSkillsAsync` (merged context) |
| `src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs` | Modify | New multi-skill overload, old becomes wrapper |
| `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs` | Modify | New list overload, instruction merge, tool merge + whitelist |
| `src/Content/Application/Application.Core/CQRS/Agents/ExecuteAgentTurn/ExecuteAgentTurnCommandHandler.cs` | Modify | Resolve full skills list from agent definition |
| `src/Content/Tests/Domain.AI.Tests/Agents/AgentDefinitionTests.cs` | Modify | Tests for Skills list |
| `src/Content/Tests/Infrastructure.AI.Tests/Agents/AgentMetadataParserTests.cs` | Modify | Tests for skills: list parsing |
| `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentFactoryTests.cs` | Modify | Tests for multi-skill creation |
| `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryTests.cs` | Modify | Tests for instruction merge, tool merge, whitelist |
| `src/Content/Tests/Application.Core.Tests/CQRS/ExecuteAgentTurnCommandHandlerTests.cs` | Modify | Tests for multi-skill resolution |

---

### Task 1: AgentDefinition — Skill to Skills

**Files:**
- Modify: `src/Content/Domain/Domain.AI/Agents/AgentDefinition.cs:46`
- Test: `src/Content/Tests/Domain.AI.Tests/Agents/AgentDefinitionTests.cs`

- [ ] **Step 1: Write failing test for Skills list property**

```csharp
[Fact]
public void Skills_DefaultsToEmptyList()
{
    var def = new AgentDefinition { Id = "test", Name = "Test" };
    def.Skills.Should().NotBeNull().And.BeEmpty();
}

[Fact]
public void Skills_CanBeInitializedWithMultipleSkills()
{
    var def = new AgentDefinition
    {
        Id = "test",
        Name = "Test",
        Skills = ["research-topic", "make-ppt"]
    };
    def.Skills.Should().HaveCount(2);
    def.Skills[0].Should().Be("research-topic");
    def.Skills[1].Should().Be("make-ppt");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Domain.AI.Tests --filter "Skills_DefaultsToEmptyList|Skills_CanBeInitializedWithMultipleSkills" --no-restore -v minimal`
Expected: FAIL — `Skills` property doesn't exist yet

- [ ] **Step 3: Replace Skill with Skills on AgentDefinition**

In `AgentDefinition.cs`, replace line 46:

```csharp
// Remove:
public string? Skill { get; init; }

// Add:
/// <summary>
/// Skill IDs that provide this agent's instructions, tool declarations, and behaviour.
/// When empty, consumers should fall back to <see cref="Id"/> as a single skill ID.
/// Populated from the <c>skills:</c> frontmatter list or the Skills Table in AGENT.md.
/// </summary>
public IReadOnlyList<string> Skills { get; init; } = [];
```

- [ ] **Step 4: Fix all compilation errors from Skill → Skills rename**

Search for all references to `.Skill` on `AgentDefinition` and update:

In `AgentMetadataParser.cs` line 54, change:
```csharp
// Before:
Skill = ParseString(yaml, "skill"),
// After:
Skills = ParseSkills(yaml),
```

In `ExecuteAgentTurnCommandHandler.cs` line 56, change:
```csharp
// Before:
var skillId = _agentRegistry.TryGet(request.AgentName)?.Skill ?? request.AgentName;
// After (temporary — full fix in Task 5):
var agentDef = _agentRegistry.TryGet(request.AgentName);
var skillId = agentDef?.Skills.FirstOrDefault() ?? request.AgentName;
```

Any other callers that reference `.Skill` — update to `.Skills.FirstOrDefault()` or `.Skills` as appropriate.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Domain.AI.Tests --no-restore -v minimal`
Expected: PASS

- [ ] **Step 6: Build full solution to catch any remaining references**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add src/Content/Domain/Domain.AI/Agents/AgentDefinition.cs src/Content/Tests/Domain.AI.Tests/Agents/AgentDefinitionTests.cs
git commit -m "refactor: replace AgentDefinition.Skill with Skills list"
```

---

### Task 2: AgentMetadataParser — Parse skills: List

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI/Agents/AgentMetadataParser.cs:54`
- Test: `src/Content/Tests/Infrastructure.AI.Tests/Agents/AgentMetadataParserTests.cs`

- [ ] **Step 1: Write failing tests for skills list parsing**

```csharp
[Fact]
public void ParseFromFile_SkillsListInFrontmatter_ParsesAllSkillIds()
{
    // Arrange — AGENT.md with skills: [research-topic, make-ppt]
    var content = """
        ---
        name: content-agent
        skills: [research-topic, make-ppt]
        ---
        Agent body text.
        """;
    var path = WriteTempAgentMd(content);

    // Act
    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    // Assert
    result.Skills.Should().BeEquivalentTo(["research-topic", "make-ppt"]);
}

[Fact]
public void ParseFromFile_SingleSkillInFrontmatter_ParsesAsSingleElementList()
{
    // Arrange — AGENT.md with skills: [my-skill] (single element list)
    var content = """
        ---
        name: single-skill-agent
        skills: [my-skill]
        ---
        Agent body text.
        """;
    var path = WriteTempAgentMd(content);

    // Act
    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    // Assert
    result.Skills.Should().BeEquivalentTo(["my-skill"]);
}

[Fact]
public void ParseFromFile_NoSkillsFrontmatter_ReturnsEmptyList()
{
    var content = """
        ---
        name: bare-agent
        ---
        Agent body text.
        """;
    var path = WriteTempAgentMd(content);

    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    result.Skills.Should().BeEmpty();
}

[Fact]
public void ParseFromFile_LegacySkillSingular_ParsesAsSingleElementList()
{
    // Backward compat: old `skill: my-skill` format
    var content = """
        ---
        name: legacy-agent
        skill: my-skill
        ---
        Agent body text.
        """;
    var path = WriteTempAgentMd(content);

    var result = _parser.ParseFromFile(path, Path.GetDirectoryName(path)!);

    result.Skills.Should().BeEquivalentTo(["my-skill"]);
}
```

Add a helper method `WriteTempAgentMd` if it doesn't already exist:
```csharp
private static string WriteTempAgentMd(string content)
{
    var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "AGENT.md");
    File.WriteAllText(path, content);
    return path;
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "ParseFromFile_SkillsListInFrontmatter|ParseFromFile_SingleSkillInFrontmatter|ParseFromFile_NoSkillsFrontmatter|ParseFromFile_LegacySkillSingular" --no-restore -v minimal`
Expected: FAIL

- [ ] **Step 3: Add ParseSkills method to AgentMetadataParser**

In `AgentMetadataParser.cs`, add after the `ParseList` method:

```csharp
private static IReadOnlyList<string> ParseSkills(string? frontmatter)
{
    // Try plural `skills: [a, b]` first
    var list = ParseList(frontmatter, "skills");
    if (list.Count > 0)
        return list;

    // Fallback: singular `skill: my-skill` for backward compatibility
    var single = ParseString(frontmatter, "skill");
    return single is not null ? [single] : [];
}
```

Update the `ParseFromFile` method to use it (line 54):

```csharp
// Replace:
Skill = ParseString(yaml, "skill"),
// With:
Skills = ParseSkills(yaml),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "ParseFromFile_Skills" --no-restore -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Content/Infrastructure/Infrastructure.AI/Agents/AgentMetadataParser.cs src/Content/Tests/Infrastructure.AI.Tests/
git commit -m "feat: parse skills list from AGENT.md frontmatter with legacy fallback"
```

---

### Task 3: IAgentConversationCache + Implementation — Widen to List

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Interfaces/IAgentConversationCache.cs`
- Modify: `src/Content/Application/Application.AI.Common/Services/AgentConversationCache.cs`

- [ ] **Step 1: Update IAgentConversationCache interface**

```csharp
/// <summary>
/// Returns the cached agent for <paramref name="conversationId"/>, creating and caching
/// a new one on a miss using the supplied <paramref name="skillIds"/> and <paramref name="options"/>.
/// </summary>
Task<AIAgent> GetOrCreateAsync(
    string conversationId,
    IReadOnlyList<string> skillIds,
    SkillAgentOptions options,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Update AgentConversationCache implementation**

```csharp
public async Task<AIAgent> GetOrCreateAsync(
    string conversationId,
    IReadOnlyList<string> skillIds,
    SkillAgentOptions options,
    CancellationToken cancellationToken = default)
{
    if (_cache.TryGetValue(conversationId, out AIAgent? cached) && cached is not null)
        return cached;

    var agent = await _agentFactory.CreateAgentFromSkillsAsync(skillIds, options, cancellationToken);

    _cache.Set(conversationId, agent, new MemoryCacheEntryOptions
    {
        SlidingExpiration = SlidingExpiration
    });

    return agent;
}
```

- [ ] **Step 3: Fix compilation errors in callers**

The only caller is `ExecuteAgentTurnCommandHandler`. Update the temporary fix from Task 1 Step 4 to pass the full list (full fix in Task 5).

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: 0 errors (may need Task 4 first — do Tasks 3+4 together if needed)

- [ ] **Step 5: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/IAgentConversationCache.cs src/Content/Application/Application.AI.Common/Services/AgentConversationCache.cs
git commit -m "refactor: widen IAgentConversationCache to accept skill ID list"
```

---

### Task 4: IAgentFactory + AgentFactory — Multi-Skill Overload

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Interfaces/IAgentFactory.cs`
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs`
- Test: `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentFactoryTests.cs`

- [ ] **Step 1: Write failing tests for multi-skill agent creation**

```csharp
[Fact]
public async Task CreateAgentFromSkillsAsync_MultipleSkills_ResolvesAllAndDelegatesToContextFactory()
{
    // Arrange
    var skill1 = new SkillDefinition { Id = "research", Name = "Research" };
    var skill2 = new SkillDefinition { Id = "make-ppt", Name = "Make PPT" };
    _skillRegistry.Setup(r => r.TryGet("research")).Returns(skill1);
    _skillRegistry.Setup(r => r.TryGet("make-ppt")).Returns(skill2);

    var expectedContext = new AgentExecutionContext
    {
        Name = "TestAgent",
        Instruction = "merged instructions",
        AIAgentFrameworkType = AIAgentFrameworkClientType.AzureOpenAI
    };
    _contextFactory
        .Setup(f => f.MapToAgentContextAsync(
            It.Is<IReadOnlyList<SkillDefinition>>(s => s.Count == 2),
            It.IsAny<SkillAgentOptions>()))
        .ReturnsAsync(expectedContext);

    // Act
    var agent = await _factory.CreateAgentFromSkillsAsync(
        ["research", "make-ppt"], new SkillAgentOptions());

    // Assert
    _contextFactory.Verify(f => f.MapToAgentContextAsync(
        It.Is<IReadOnlyList<SkillDefinition>>(s =>
            s.Count == 2 && s[0].Id == "research" && s[1].Id == "make-ppt"),
        It.IsAny<SkillAgentOptions>()), Times.Once);
}

[Fact]
public async Task CreateAgentFromSkillsAsync_RequiredSkillMissing_Throws()
{
    _skillRegistry.Setup(r => r.TryGet("missing")).Returns((SkillDefinition?)null);

    var act = () => _factory.CreateAgentFromSkillsAsync(
        ["missing"], new SkillAgentOptions());

    await act.Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*missing*not found*");
}

[Fact]
public async Task CreateAgentFromSkillAsync_SingleSkill_DelegatesToMultiSkillOverload()
{
    var skill = new SkillDefinition { Id = "single", Name = "Single" };
    _skillRegistry.Setup(r => r.TryGet("single")).Returns(skill);

    var expectedContext = new AgentExecutionContext
    {
        Name = "SingleAgent",
        Instruction = "single instructions",
        AIAgentFrameworkType = AIAgentFrameworkClientType.AzureOpenAI
    };
    _contextFactory
        .Setup(f => f.MapToAgentContextAsync(
            It.Is<IReadOnlyList<SkillDefinition>>(s => s.Count == 1),
            It.IsAny<SkillAgentOptions>()))
        .ReturnsAsync(expectedContext);

    var agent = await _factory.CreateAgentFromSkillAsync("single", new SkillAgentOptions());

    _contextFactory.Verify(f => f.MapToAgentContextAsync(
        It.Is<IReadOnlyList<SkillDefinition>>(s => s.Count == 1 && s[0].Id == "single"),
        It.IsAny<SkillAgentOptions>()), Times.Once);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "CreateAgentFromSkillsAsync|CreateAgentFromSkillAsync_SingleSkill_Delegates" --no-restore -v minimal`
Expected: FAIL

- [ ] **Step 3: Add CreateAgentFromSkillsAsync to IAgentFactory**

In `IAgentFactory.cs`, add:

```csharp
/// <summary>
/// Creates a single AI agent with multiple skills merged into one execution context.
/// All skills' instructions and tools are combined. The LLM self-orchestrates between skills.
/// </summary>
/// <param name="skillIds">Skill identifiers to merge into one agent context.</param>
/// <param name="options">Configuration for resource loading and agent overrides.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <exception cref="InvalidOperationException">Thrown when no skills could be resolved.</exception>
Task<AIAgent> CreateAgentFromSkillsAsync(
    IReadOnlyList<string> skillIds,
    SkillAgentOptions options,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement CreateAgentFromSkillsAsync in AgentFactory**

```csharp
public async Task<AIAgent> CreateAgentFromSkillsAsync(
    IReadOnlyList<string> skillIds,
    SkillAgentOptions options,
    CancellationToken cancellationToken = default)
{
    _logger.LogDebug("Creating agent from {Count} skill(s): {SkillIds}",
        skillIds.Count, string.Join(", ", skillIds));

    var skills = new List<SkillDefinition>();
    foreach (var id in skillIds)
    {
        var skill = _skillRegistry.TryGet(id);
        if (skill is null)
        {
            _logger.LogError("Skill '{SkillId}' not found in registry", id);
            throw new InvalidOperationException(
                $"Skill '{id}' not found. Ensure it exists in the configured skill paths.");
        }
        skills.Add(skill);
    }

    var agentContext = await _agentContextFactory.MapToAgentContextAsync(skills, options);
    var agent = await CreateAgentAsync(agentContext, cancellationToken);

    _logger.LogInformation("Created agent {AgentName} from {Count} skill(s): {SkillIds}",
        agentContext.Name, skillIds.Count, string.Join(", ", skillIds));
    return agent;
}
```

- [ ] **Step 5: Refactor existing single-skill overloads to delegate**

```csharp
public Task<AIAgent> CreateAgentFromSkillAsync(string skillId, CancellationToken cancellationToken = default)
    => CreateAgentFromSkillsAsync([skillId], new SkillAgentOptions(), cancellationToken);

public Task<AIAgent> CreateAgentFromSkillAsync(string skillId, SkillAgentOptions options, CancellationToken cancellationToken = default)
    => CreateAgentFromSkillsAsync([skillId], options, cancellationToken);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --no-restore -v minimal`
Expected: PASS (may require Task 5 for MapToAgentContextAsync list overload — stub it if needed)

- [ ] **Step 7: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Interfaces/IAgentFactory.cs src/Content/Application/Application.AI.Common/Factories/AgentFactory.cs src/Content/Tests/Application.AI.Common.Tests/
git commit -m "feat: add CreateAgentFromSkillsAsync for multi-skill merged context"
```

---

### Task 5: AgentExecutionContextFactory — Instruction Merge + Tool Merge + Whitelist

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs`
- Test: `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryTests.cs`

This is the core task. The factory needs a new overload that accepts a list of skills and merges their instructions, tools, providers, and middleware.

- [ ] **Step 1: Write failing tests for instruction merging**

```csharp
[Fact]
public async Task MapToAgentContextAsync_MultipleSkills_MergesInstructionsWithSectionHeaders()
{
    var skills = new List<SkillDefinition>
    {
        new() { Id = "research", Name = "Research", Instructions = "Search and synthesize sources." },
        new() { Id = "make-ppt", Name = "Make PPT", Instructions = "Create PowerPoint presentations." }
    };

    var context = await _factory.MapToAgentContextAsync(skills, new SkillAgentOptions());

    context.Instruction.Should().Contain("## Skill: Research");
    context.Instruction.Should().Contain("Search and synthesize sources.");
    context.Instruction.Should().Contain("## Skill: Make PPT");
    context.Instruction.Should().Contain("Create PowerPoint presentations.");
}

[Fact]
public async Task MapToAgentContextAsync_MultipleSkills_PreservesSkillOrder()
{
    var skills = new List<SkillDefinition>
    {
        new() { Id = "first", Name = "First", Instructions = "First instructions." },
        new() { Id = "second", Name = "Second", Instructions = "Second instructions." }
    };

    var context = await _factory.MapToAgentContextAsync(skills, new SkillAgentOptions());

    var firstIdx = context.Instruction.IndexOf("## Skill: First");
    var secondIdx = context.Instruction.IndexOf("## Skill: Second");
    firstIdx.Should().BeLessThan(secondIdx);
}

[Fact]
public async Task MapToAgentContextAsync_MultipleSkills_AdditionalContextAppendsAfterAllSkills()
{
    var skills = new List<SkillDefinition>
    {
        new() { Id = "skill1", Name = "Skill1", Instructions = "Skill 1 content." }
    };
    var options = new SkillAgentOptions { AdditionalContext = "Extra agent-level context." };

    var context = await _factory.MapToAgentContextAsync(skills, options);

    var skillIdx = context.Instruction.IndexOf("## Skill: Skill1");
    var extraIdx = context.Instruction.IndexOf("Extra agent-level context.");
    extraIdx.Should().BeGreaterThan(skillIdx);
}

[Fact]
public async Task MapToAgentContextAsync_SingleSkill_SameAsSingleSkillOverload()
{
    var skill = new SkillDefinition { Id = "solo", Name = "Solo", Instructions = "Solo instructions." };

    var listContext = await _factory.MapToAgentContextAsync(new List<SkillDefinition> { skill }, new SkillAgentOptions());
    var singleContext = await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

    listContext.Instruction.Should().Be(singleContext.Instruction);
}
```

- [ ] **Step 2: Write failing tests for tool merging**

```csharp
[Fact]
public async Task MapToAgentContextAsync_MultipleSkills_MergesToolsFromAllSkills()
{
    var tool1 = AIFunctionFactory.Create(() => "a", "tool_a", "Tool A");
    var tool2 = AIFunctionFactory.Create(() => "b", "tool_b", "Tool B");
    var skills = new List<SkillDefinition>
    {
        new() { Id = "s1", Name = "S1", Tools = [tool1] },
        new() { Id = "s2", Name = "S2", Tools = [tool2] }
    };

    var context = await _factory.MapToAgentContextAsync(skills, new SkillAgentOptions());

    context.Tools.Should().HaveCount(2);
    context.Tools.Select(t => t.Name).Should().Contain("tool_a").And.Contain("tool_b");
}

[Fact]
public async Task MapToAgentContextAsync_DuplicateToolAcrossSkills_Deduplicated()
{
    var tool = AIFunctionFactory.Create(() => "shared", "shared_tool", "Shared");
    var skills = new List<SkillDefinition>
    {
        new() { Id = "s1", Name = "S1", Tools = [tool] },
        new() { Id = "s2", Name = "S2", Tools = [tool] }
    };

    var context = await _factory.MapToAgentContextAsync(skills, new SkillAgentOptions());

    context.Tools.Should().HaveCount(1);
    context.Tools[0].Name.Should().Be("shared_tool");
}
```

- [ ] **Step 3: Write failing tests for AllowedTools whitelist**

```csharp
[Fact]
public async Task MapToAgentContextAsync_WithAllowedToolsWhitelist_FiltersToOnlyAllowed()
{
    var toolA = AIFunctionFactory.Create(() => "a", "tool_a", "Tool A");
    var toolB = AIFunctionFactory.Create(() => "b", "tool_b", "Tool B");
    var skills = new List<SkillDefinition>
    {
        new() { Id = "s1", Name = "S1", Tools = [toolA, toolB] }
    };
    var options = new SkillAgentOptions();

    // Simulate agent-level AllowedTools whitelist via options or parameter
    var context = await _factory.MapToAgentContextAsync(skills, options, allowedTools: ["tool_a"]);

    context.Tools.Should().HaveCount(1);
    context.Tools[0].Name.Should().Be("tool_a");
}

[Fact]
public async Task MapToAgentContextAsync_NoAllowedToolsWhitelist_AllToolsAvailable()
{
    var toolA = AIFunctionFactory.Create(() => "a", "tool_a", "Tool A");
    var toolB = AIFunctionFactory.Create(() => "b", "tool_b", "Tool B");
    var skills = new List<SkillDefinition>
    {
        new() { Id = "s1", Name = "S1", Tools = [toolA, toolB] }
    };

    var context = await _factory.MapToAgentContextAsync(skills, new SkillAgentOptions());

    context.Tools.Should().HaveCount(2);
}
```

- [ ] **Step 4: Run all new tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "MapToAgentContextAsync_Multiple|MapToAgentContextAsync_Duplicate|MapToAgentContextAsync_With|MapToAgentContextAsync_No" --no-restore -v minimal`
Expected: FAIL

- [ ] **Step 5: Implement BuildMergedInstruction method**

In `AgentExecutionContextFactory.cs`, add:

```csharp
private static string BuildMergedInstruction(IReadOnlyList<SkillDefinition> skills, SkillAgentOptions options)
{
    var parts = new List<string>();

    foreach (var skill in skills)
    {
        if (string.IsNullOrEmpty(skill.Instructions))
            continue;

        if (skills.Count > 1)
            parts.Add($"## Skill: {skill.Name}\n\n{skill.Instructions}");
        else
            parts.Add(skill.Instructions);
    }

    if (!string.IsNullOrEmpty(options.AdditionalContext))
        parts.Add(options.AdditionalContext);

    return string.Join("\n\n", parts);
}
```

- [ ] **Step 6: Implement BuildMergedToolsAsync method**

In `AgentExecutionContextFactory.cs`, add:

```csharp
private async Task<List<AITool>> BuildMergedToolsAsync(
    IReadOnlyList<SkillDefinition> skills,
    SkillAgentOptions options,
    IReadOnlyList<string>? allowedTools = null)
{
    var allTools = new List<AITool>();

    foreach (var skill in skills)
    {
        var skillTools = await BuildToolsAsync(skill, options);
        allTools.AddRange(skillTools);
    }

    // Deduplicate by name across all skills
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var deduplicated = allTools.Where(t => seen.Add(t.Name)).ToList();

    // Apply agent-level AllowedTools whitelist
    if (allowedTools is { Count: > 0 })
    {
        var allowed = new HashSet<string>(allowedTools, StringComparer.OrdinalIgnoreCase);
        deduplicated = deduplicated.Where(t => allowed.Contains(t.Name)).ToList();
    }

    return deduplicated;
}
```

- [ ] **Step 7: Implement the list overload of MapToAgentContextAsync**

Add the new public method. It follows the same structure as the existing single-skill version but delegates to the merged instruction and tool builders:

```csharp
public async Task<AgentExecutionContext> MapToAgentContextAsync(
    IReadOnlyList<SkillDefinition> skills,
    SkillAgentOptions options,
    IReadOnlyList<string>? allowedTools = null)
{
    if (skills.Count == 0)
        throw new ArgumentException("At least one skill is required.", nameof(skills));

    var primarySkill = skills[0];
    var deploymentName = ResolveDeploymentName(primarySkill, options);
    var agentName = options.AgentNameOverride ?? ToAgentName(primarySkill.Name);
    var instruction = BuildMergedInstruction(skills, options);
    var tools = await BuildMergedToolsAsync(skills, options, allowedTools);
    var middlewareTypes = ResolveMiddlewareTypes(primarySkill, options);
    var aiContextProviders = BuildMergedAIContextProviders(skills, options);
    var frameworkType = options.FrameworkType
        ?? ResolveFrameworkTypeFromMetadata(primarySkill)
        ?? _appConfig.CurrentValue.AI?.AgentFramework?.ClientType
        ?? AIAgentFrameworkClientType.AzureOpenAI;

    var traceScope = options.TraceScope ?? TraceScope.ForExecution(Guid.NewGuid());

    // Token budget tracking
    if (_budgetTracker != null)
    {
        var instructionTokens = TokenEstimationHelper.EstimateTokens(instruction);
        _budgetTracker.RecordAllocation(agentName, "system_prompt", instructionTokens);

        ContextBudgetMetrics.SystemPromptTokens.Record(instructionTokens,
            new KeyValuePair<string, object?>(AgentConventions.Name, agentName));
        ContextSourceMetrics.SourceTokens.Record(instructionTokens,
            new KeyValuePair<string, object?>(ContextConventions.SourceType, ContextConventions.SourceTypeValues.SystemPrompt),
            new KeyValuePair<string, object?>(AgentConventions.Name, agentName));

        if (tools?.Count > 0)
        {
            var toolTokens = tools.Count * 50;
            _budgetTracker.RecordAllocation(agentName, "tool_schemas", toolTokens);

            ContextBudgetMetrics.ToolsSchemaTokens.Record(toolTokens,
                new KeyValuePair<string, object?>(AgentConventions.Name, agentName));
            ContextSourceMetrics.SourceTokens.Record(toolTokens,
                new KeyValuePair<string, object?>(ContextConventions.SourceType, ContextConventions.SourceTypeValues.ToolsSchema),
                new KeyValuePair<string, object?>(AgentConventions.Name, agentName));
        }
    }

    var additionalProps = BuildAdditionalProperties(primarySkill, options);

    if (_skillContentProvider != null)
        additionalProps[ISkillContentProvider.AdditionalPropertiesKey] = _skillContentProvider;

    if (_resilientChatClientProvider is not null)
    {
        var resilientClient = await _resilientChatClientProvider.GetResilientChatClientAsync();
        additionalProps["__resilientChatClient"] = resilientClient;
    }

    if (_traceStore != null)
    {
        var metadata = new RunMetadata
        {
            AgentName = agentName,
            StartedAt = DateTimeOffset.UtcNow
        };
        var traceWriter = await _traceStore.StartRunAsync(traceScope, metadata);
        additionalProps[ITraceWriter.AdditionalPropertiesKey] = traceWriter;

        if (traceScope.CandidateId.HasValue)
        {
            System.Diagnostics.Activity.Current?.AddBaggage(
                Domain.AI.Telemetry.Conventions.ToolConventions.HarnessCandidateId,
                traceScope.CandidateId.Value.ToString("D"));
        }
    }

    var context = new AgentExecutionContext
    {
        Name = agentName,
        Description = string.Join("; ", skills.Select(s => s.Description).Where(d => !string.IsNullOrEmpty(d))),
        Instruction = instruction,
        DeploymentName = deploymentName,
        AgentId = options.AgentId ?? primarySkill.AgentId,
        AIAgentFrameworkType = frameworkType,
        Tools = tools,
        AIContextProviders = aiContextProviders,
        MiddlewareTypes = middlewareTypes,
        TraceScope = traceScope,
        Temperature = options.Temperature,
        AdditionalProperties = additionalProps
    };

    _agentConfigReporter?.RegisterAgent(
        agentName,
        deploymentName,
        (options.Temperature ?? 0.7).ToString("0.##"),
        tools?.Count ?? 0,
        aiContextProviders?.Count ?? 0,
        _mcpToolProvider != null ? 1 : 0);

    _logger.LogInformation(
        "Mapped {SkillCount} skill(s) [{SkillIds}] to agent context {AgentName} with {ToolCount} tools",
        skills.Count, string.Join(", ", skills.Select(s => s.Id)), agentName, tools?.Count ?? 0);

    return context;
}
```

- [ ] **Step 8: Add BuildMergedAIContextProviders helper**

```csharp
private IList<AIContextProvider>? BuildMergedAIContextProviders(
    IReadOnlyList<SkillDefinition> skills,
    SkillAgentOptions options)
{
    var providers = new List<AIContextProvider>();

    // Skill paths provider (shared across all skills)
    var skillPaths = ResolveSkillPaths(options);
    if (skillPaths.Count > 0)
    {
        var builder = new AgentSkillsProviderBuilder()
            .UseFileScriptRunner(NoOpScriptRunner);
        foreach (var path in skillPaths)
            builder.UseFileSkill(path);
        providers.Add(builder.Build());
    }

    // Union AllowedTools from all skills for the permission filter
    var allAllowedTools = skills
        .Where(s => s.AllowedTools?.Count > 0)
        .SelectMany(s => s.AllowedTools!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (allAllowedTools.Count > 0)
        providers.Add(new Services.Agent.ToolPermissionFilter(allAllowedTools));

    return providers.Count > 0 ? providers : null;
}
```

- [ ] **Step 9: Refactor single-skill overload to delegate**

```csharp
public Task<AgentExecutionContext> MapToAgentContextAsync(SkillDefinition skill, SkillAgentOptions options)
    => MapToAgentContextAsync([skill], options);
```

Remove the now-unused `BuildInstruction(SkillDefinition, SkillAgentOptions)` method.

- [ ] **Step 10: Run all tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --no-restore -v minimal`
Expected: PASS

- [ ] **Step 11: Build full solution**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: 0 errors

- [ ] **Step 12: Commit**

```bash
git add src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs src/Content/Tests/Application.AI.Common.Tests/
git commit -m "feat: multi-skill instruction merge, tool merge, and AllowedTools whitelist"
```

---

### Task 6: ExecuteAgentTurnCommandHandler — Full Multi-Skill Resolution

**Files:**
- Modify: `src/Content/Application/Application.Core/CQRS/Agents/ExecuteAgentTurn/ExecuteAgentTurnCommandHandler.cs:56-67`
- Test: `src/Content/Tests/Application.Core.Tests/CQRS/ExecuteAgentTurnCommandHandlerTests.cs`

- [ ] **Step 1: Write failing tests for multi-skill resolution**

```csharp
[Fact]
public async Task Handle_AgentWithMultipleSkills_PassesAllSkillIdsToCache()
{
    var agentDef = new AgentDefinition
    {
        Id = "content-agent",
        Name = "Content Agent",
        Skills = ["research", "make-ppt"]
    };
    _agentRegistry
        .Setup(r => r.TryGet("content-agent"))
        .Returns(agentDef);

    var agent = new TestableAIAgent("response");
    _agentCache
        .Setup(c => c.GetOrCreateAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyList<string>>(s => s.Count == 2 && s[0] == "research" && s[1] == "make-ppt"),
            It.IsAny<SkillAgentOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(agent);

    var command = CreateCommand(agentName: "content-agent");
    var result = await _handler.Handle(command, CancellationToken.None);

    result.Success.Should().BeTrue();
    _agentCache.Verify(c => c.GetOrCreateAsync(
        It.IsAny<string>(),
        It.Is<IReadOnlyList<string>>(s => s.Count == 2),
        It.IsAny<SkillAgentOptions>(),
        It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task Handle_AgentWithNoSkills_FallsBackToAgentNameAsSingleSkill()
{
    var agentDef = new AgentDefinition
    {
        Id = "legacy-agent",
        Name = "Legacy Agent",
        Skills = []
    };
    _agentRegistry
        .Setup(r => r.TryGet("legacy-agent"))
        .Returns(agentDef);

    var agent = new TestableAIAgent("response");
    _agentCache
        .Setup(c => c.GetOrCreateAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyList<string>>(s => s.Count == 1 && s[0] == "legacy-agent"),
            It.IsAny<SkillAgentOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(agent);

    var command = CreateCommand(agentName: "legacy-agent");
    var result = await _handler.Handle(command, CancellationToken.None);

    result.Success.Should().BeTrue();
}

[Fact]
public async Task Handle_NoAgentInRegistry_FallsBackToAgentNameAsSingleSkill()
{
    // Registry returns null — existing backward compat behavior
    _agentRegistry
        .Setup(r => r.TryGet("unknown"))
        .Returns((AgentDefinition?)null);

    var agent = new TestableAIAgent("response");
    _agentCache
        .Setup(c => c.GetOrCreateAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyList<string>>(s => s.Count == 1 && s[0] == "unknown"),
            It.IsAny<SkillAgentOptions>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(agent);

    var command = CreateCommand(agentName: "unknown");
    var result = await _handler.Handle(command, CancellationToken.None);

    result.Success.Should().BeTrue();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.Core.Tests --filter "Handle_AgentWithMultipleSkills|Handle_AgentWithNoSkills|Handle_NoAgentInRegistry" --no-restore -v minimal`
Expected: FAIL

- [ ] **Step 3: Update handler to resolve full skills list**

In `ExecuteAgentTurnCommandHandler.cs`, replace lines 56-67:

```csharp
// Resolve skill list from agent manifest. Falls back to treating
// AgentName as a single skill id for backward compatibility.
var agentDef = _agentRegistry.TryGet(request.AgentName);
IReadOnlyList<string> skillIds = agentDef?.Skills is { Count: > 0 }
    ? agentDef.Skills
    : [request.AgentName];

var agent = await _agentCache.GetOrCreateAsync(
    request.ConversationId,
    skillIds,
    new SkillAgentOptions
    {
        AdditionalContext = request.SystemPromptOverride,
        DeploymentName = request.DeploymentOverride,
        Temperature = request.Temperature,
    },
    cancellationToken);
```

- [ ] **Step 4: Update existing tests that mock single skillId**

Existing tests that set up `_agentCache.Setup(c => c.GetOrCreateAsync(..., "TestAgent", ...))` need updating to match the new `IReadOnlyList<string>` parameter:

```csharp
// Before:
_agentCache
    .Setup(c => c.GetOrCreateAsync(
        It.IsAny<string>(),
        "TestAgent",
        It.IsAny<SkillAgentOptions>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(agent);

// After:
_agentCache
    .Setup(c => c.GetOrCreateAsync(
        It.IsAny<string>(),
        It.IsAny<IReadOnlyList<string>>(),
        It.IsAny<SkillAgentOptions>(),
        It.IsAny<CancellationToken>()))
    .ReturnsAsync(agent);
```

- [ ] **Step 5: Run all tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.Core.Tests --no-restore -v minimal`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Content/Application/Application.Core/CQRS/Agents/ExecuteAgentTurn/ExecuteAgentTurnCommandHandler.cs src/Content/Tests/Application.Core.Tests/
git commit -m "feat: resolve full skills list from agent manifest in handler"
```

---

### Task 7: Full Solution Build + Integration Verification

**Files:** None (verification only)

- [ ] **Step 1: Build full solution**

Run: `dotnet build src/AgenticHarness.slnx`
Expected: 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test src/AgenticHarness.slnx --no-build`
Expected: All new tests pass. Pre-existing AgentHub EF Core failures unchanged.

- [ ] **Step 3: Verify backward compatibility — grep for any remaining .Skill references**

Run: `grep -rn "\.Skill[^s]" src/Content/ --include="*.cs" | grep -v "\.Skills" | grep -v "SkillId" | grep -v "SkillDefinition" | grep -v "SkillAgent" | grep -v "SkillMetadata" | grep -v "SkillContent" | grep -v "SkillPaths" | grep -v "//"`
Expected: No remaining references to the old singular `.Skill` property

- [ ] **Step 4: Commit any remaining fixes**

```bash
git add -A
git commit -m "chore: fix remaining references from Skill to Skills migration"
```

---

## Summary

| Task | What it does | Files modified | Tests added |
|------|-------------|---------------|-------------|
| 1 | `AgentDefinition.Skill` → `.Skills` list | 3+ (definition + all callers) | 2 |
| 2 | Parser: `skills: [a, b]` + legacy `skill:` fallback | 1 | 4 |
| 3 | `IAgentConversationCache` widens to list | 2 | 0 (tested via handler) |
| 4 | `IAgentFactory` + `AgentFactory` multi-skill overload | 2 | 3 |
| 5 | Context factory: instruction merge + tool merge + whitelist | 1 | 6 |
| 6 | Handler resolves full skills list | 1 | 3 |
| 7 | Full build + integration verification | 0 | 0 |
| **Total** | | **~10 files** | **~18 tests** |
