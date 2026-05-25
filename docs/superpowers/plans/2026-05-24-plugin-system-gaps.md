# Plugin System Gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close 4 architectural gaps in the skill/plugin system: nested YAML parser (#6), required-tool enforcement (#8), local-only plugin system (#3), and dual skill mode (#4).

**Architecture:** Fix two bugs/compatibility issues first (parser, tool enforcement), then build the local-only plugin system (declare plugins in appsettings.json pointing at local directories, harness wires skills + MCP servers at startup), then add dual skill mode so plugin skills without `allowed-tools` get all their plugin's MCP tools.

**Tech Stack:** C# .NET 10, Clean Architecture, xUnit/Moq/FluentAssertions, System.Text.Json, IOptionsMonitor pattern

---

## File Map

### Gap #6 — Nested YAML Parser
| Action | File |
|--------|------|
| Modify | `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs` |
| Create | `src/Content/Tests/Infrastructure.AI.Tests/Skills/SkillMetadataParserNestedYamlTests.cs` |

### Gap #8 — Required-Tool Enforcement
| Action | File |
|--------|------|
| Modify | `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs` |
| Create | `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryToolEnforcementTests.cs` |

### Gap #3 — Local-Only Plugin System
| Action | File |
|--------|------|
| Create | `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginsConfig.cs` |
| Create | `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginDeclaration.cs` |
| Create | `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginManifest.cs` |
| Modify | `src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs` |
| Modify | `src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs` |
| Modify | `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs` |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginManifestReader.cs` |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginLoader.cs` |
| Create | `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginRegistry.cs` |
| Create | `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginManifestReader.cs` |
| Create | `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginLoader.cs` |
| Create | `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginRegistry.cs` |
| Modify | `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs` |
| Create | `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginManifestReaderTests.cs` |
| Create | `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginLoaderTests.cs` |
| Create | `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginRegistryTests.cs` |

### Gap #4 — Dual Skill Mode
| Action | File |
|--------|------|
| Create | `src/Content/Domain/Domain.AI/Skills/SkillMode.cs` |
| Modify | `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs` |
| Create | `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryDualModeTests.cs` |

---

### Task 1: Nested YAML Parser — Handle `metadata:` Blocks (Gap #6)

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Skills/SkillMetadataParserNestedYamlTests.cs`

**Context:** The current `ExtractFrontmatter` method returns the raw frontmatter string between `---` delimiters. `ParseString` iterates lines and matches `key: value`. When azure-skills has:
```yaml
metadata:
  author: Microsoft
  version: "1.1.2"
```
`ParseString(frontmatter, "version")` matches `version: "1.1.2"` correctly because it's still a line with `version:`. The actual problem is that `metadata:` itself is parsed as a key with empty value, and nested keys with leading whitespace are matched incorrectly when they share names with top-level keys.

The fix: add a `ParseNestedBlock` method that extracts all indented lines under a parent key, and populate `SkillDefinition.Metadata` dictionary. Also ensure `ParseString` and `ParseList` skip indented lines (lines starting with whitespace) to avoid false matches.

- [ ] **Step 1: Write the failing tests**

Create `src/Content/Tests/Infrastructure.AI.Tests/Skills/SkillMetadataParserNestedYamlTests.cs`:

```csharp
using Domain.AI.Skills;
using FluentAssertions;
using Infrastructure.AI.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Skills;

public sealed class SkillMetadataParserNestedYamlTests : IDisposable
{
    private readonly SkillMetadataParser _sut;
    private readonly string _tempDir;

    public SkillMetadataParserNestedYamlTests()
    {
        _sut = new SkillMetadataParser(NullLogger<SkillMetadataParser>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"nested-yaml-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string WriteSkillFile(string content)
    {
        var filePath = Path.Combine(_tempDir, "SKILL.md");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    [Fact]
    public void ParseFromFile_NestedMetadata_ExtractsMetadataDictionary()
    {
        var content = "---\nname: azure-deploy\nmetadata:\n  author: Microsoft\n  version: \"1.1.2\"\n---\nDeploy stuff.";
        var filePath = WriteSkillFile(content);

        var result = _sut.ParseFromFile(filePath, _tempDir);

        result.Metadata.Should().NotBeNull();
        result.Metadata!["author"].Should().Be("Microsoft");
        result.Metadata["version"].Should().Be("1.1.2");
    }

    [Fact]
    public void ParseFromFile_NestedMetadata_DoesNotPolluteTopLevelFields()
    {
        var content = "---\nname: test-skill\nversion: \"2.0\"\nmetadata:\n  version: \"1.1.2\"\n  author: Someone\n---\nBody.";
        var filePath = WriteSkillFile(content);

        var result = _sut.ParseFromFile(filePath, _tempDir);

        result.Version.Should().Be("2.0");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["version"].Should().Be("1.1.2");
    }

    [Fact]
    public void ParseFromFile_NoMetadataBlock_MetadataIsNull()
    {
        var content = "---\nname: simple-skill\ncategory: testing\n---\nBody.";
        var filePath = WriteSkillFile(content);

        var result = _sut.ParseFromFile(filePath, _tempDir);

        result.Metadata.Should().BeNull();
    }

    [Fact]
    public void ParseFromFile_NestedMetadata_PopulatesAuthorField()
    {
        var content = "---\nname: azure-deploy\nmetadata:\n  author: Microsoft\n---\nBody.";
        var filePath = WriteSkillFile(content);

        var result = _sut.ParseFromFile(filePath, _tempDir);

        result.Author.Should().Be("Microsoft");
    }

    [Fact]
    public void ParseFromFile_TopLevelVersion_NotOverriddenByNestedVersion()
    {
        var content = "---\nname: test\nmetadata:\n  version: \"nested-ver\"\n---\nBody.";
        var filePath = WriteSkillFile(content);

        var result = _sut.ParseFromFile(filePath, _tempDir);

        // No top-level version, so Version should remain null (metadata version is informational)
        result.Version.Should().BeNull();
        result.Metadata!["version"].Should().Be("nested-ver");
    }

    [Fact]
    public void ParseFromFile_MixedNestedAndTopLevel_ParsesCorrectly()
    {
        var content = "---\nname: mixed\ncategory: infra\nmetadata:\n  author: Team\n  custom-field: value123\ntags: [\"azure\", \"deploy\"]\n---\nInstructions.";
        var filePath = WriteSkillFile(content);

        var result = _sut.ParseFromFile(filePath, _tempDir);

        result.Name.Should().Be("mixed");
        result.Category.Should().Be("infra");
        result.Tags.Should().BeEquivalentTo(["azure", "deploy"]);
        result.Metadata.Should().NotBeNull();
        result.Metadata!["author"].Should().Be("Team");
        result.Metadata["custom-field"].Should().Be("value123");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~SkillMetadataParserNestedYamlTests" --no-build`

Expected: Tests fail because `Metadata` is not populated (remains null).

- [ ] **Step 3: Implement nested YAML parsing**

Modify `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs`:

Add a `ParseNestedBlock` method and a `ParseMetadata` method. Update `ParseString` and `ParseList` to skip indented lines. Wire `Metadata` and `Author` in both `ParseFromFile` and `Parse`.

```csharp
// Add after the existing ParseList method:

private static IDictionary<string, object>? ParseMetadata(string? frontmatter)
{
    var block = ParseNestedBlock(frontmatter, "metadata");
    if (block == null || block.Count == 0)
        return null;

    return block;
}

private static Dictionary<string, string>? ParseNestedBlock(string? frontmatter, string parentKey)
{
    if (string.IsNullOrEmpty(frontmatter))
        return null;

    var lines = frontmatter.Split('\n');
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var inBlock = false;

    for (var i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        var trimmed = line.Trim();

        if (!inBlock)
        {
            // Look for "parentKey:" with no value (or only whitespace after colon)
            if (trimmed.StartsWith(parentKey + ":", StringComparison.OrdinalIgnoreCase))
            {
                var afterColon = trimmed[(parentKey.Length + 1)..].Trim();
                if (string.IsNullOrEmpty(afterColon))
                {
                    inBlock = true;
                    continue;
                }
            }
            continue;
        }

        // We're inside the block — indented lines belong to it
        if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
        {
            var kvLine = trimmed;
            var colonIdx = kvLine.IndexOf(':');
            if (colonIdx > 0)
            {
                var key = kvLine[..colonIdx].Trim();
                var value = kvLine[(colonIdx + 1)..].Trim().Trim('"', '\'');
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    result[key] = value;
            }
        }
        else
        {
            // Non-indented line ends the block
            break;
        }
    }

    return result.Count > 0 ? result : null;
}
```

Update `ParseString` to skip indented lines:

```csharp
private static string? ParseString(string? frontmatter, string key)
{
    if (string.IsNullOrEmpty(frontmatter))
        return null;

    foreach (var line in frontmatter.Split('\n'))
    {
        // Skip indented lines (they belong to nested blocks)
        if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            continue;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            continue;

        var value = trimmed[(key.Length + 1)..].Trim().Trim('"', '\'');
        return string.IsNullOrEmpty(value) ? null : value;
    }

    return null;
}
```

Update `ParseList` similarly — add the same skip for indented lines:

```csharp
private static IList<string> ParseList(string? frontmatter, string key)
{
    if (string.IsNullOrEmpty(frontmatter))
        return [];

    foreach (var line in frontmatter.Split('\n'))
    {
        // Skip indented lines (they belong to nested blocks)
        if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            continue;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            continue;

        var rest = trimmed[(key.Length + 1)..].Trim();
        if (rest.StartsWith('['))
        {
            return rest.Trim('[', ']')
                .Split(',')
                .Select(s => s.Trim().Trim('"', '\''))
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }
    }

    return [];
}
```

Update the `ParseFromFile` object initializer to add:
```csharp
Metadata = ParseMetadata(frontmatter),
Author = ParseNestedBlock(frontmatter, "metadata") is { } meta
    && meta.TryGetValue("author", out var author) ? author : null,
```

Update the `Parse` object initializer identically:
```csharp
Metadata = ParseMetadata(rawFrontmatter),
Author = ParseNestedBlock(rawFrontmatter, "metadata") is { } meta
    && meta.TryGetValue("author", out var author) ? author : null,
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~SkillMetadataParserNestedYamlTests"`

Expected: All 6 tests PASS.

- [ ] **Step 5: Run all existing parser tests to verify no regressions**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~SkillMetadataParser"`

Expected: All tests PASS (existing + new).

- [ ] **Step 6: Commit**

```powershell
git add src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs src/Content/Tests/Infrastructure.AI.Tests/Skills/SkillMetadataParserNestedYamlTests.cs
git commit -m "fix(parser): handle nested YAML metadata blocks in SKILL.md frontmatter"
```

---

### Task 2: Required-Tool Enforcement (Gap #8)

**Files:**
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs:413-454`
- Create: `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryToolEnforcementTests.cs`

**Context:** `ProvisionToolAsync` at line 450-451 logs a warning for required (non-optional) tools that can't be resolved, but returns null and continues. The skill loads without a tool it declared as required. The fix: throw `InvalidOperationException` instead of logging a warning when `!declaration.Optional` and no resolution succeeded.

- [ ] **Step 1: Write the failing tests**

Create `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryToolEnforcementTests.cs`:

```csharp
using Application.AI.Common.Factories;
using Application.AI.Common.Interfaces.Tools;
using Domain.AI.Agents;
using Domain.AI.Skills;
using Domain.AI.Tools;
using Domain.Common.Config;
using Domain.Common.Config.AI;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Application.AI.Common.Tests.Factories;

public class AgentExecutionContextFactoryToolEnforcementTests
{
    private readonly AgentExecutionContextFactory _factory;

    public AgentExecutionContextFactoryToolEnforcementTests()
    {
        var appConfig = new AppConfig
        {
            AI = new AIConfig
            {
                AgentFramework = new AgentFrameworkConfig
                {
                    DefaultDeployment = "gpt-4o",
                    ClientType = AIAgentFrameworkClientType.AzureOpenAI
                }
            }
        };
        var monitor = Mock.Of<IOptionsMonitor<AppConfig>>(m => m.CurrentValue == appConfig);

        _factory = new AgentExecutionContextFactory(
            NullLogger<AgentExecutionContextFactory>.Instance,
            monitor,
            new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance);
    }

    [Fact]
    public async Task MapToAgentContextAsync_RequiredToolUnresolvable_ThrowsInvalidOperation()
    {
        var skill = new SkillDefinition
        {
            Id = "deploy",
            Name = "deploy",
            Instructions = "Deploy things",
            ToolDeclarations =
            [
                new ToolDeclaration { Name = "deploy_execute", Optional = false }
            ]
        };

        var act = () => _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*deploy_execute*could not be resolved*");
    }

    [Fact]
    public async Task MapToAgentContextAsync_OptionalToolUnresolvable_Succeeds()
    {
        var skill = new SkillDefinition
        {
            Id = "research",
            Name = "research",
            Instructions = "Research things",
            ToolDeclarations =
            [
                new ToolDeclaration { Name = "optional_helper", Optional = true }
            ]
        };

        var context = await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        context.Should().NotBeNull();
        context.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task MapToAgentContextAsync_RequiredToolWithManualFallback_Succeeds()
    {
        var skill = new SkillDefinition
        {
            Id = "review",
            Name = "review",
            Instructions = "Review code",
            ToolDeclarations =
            [
                new ToolDeclaration { Name = "missing_tool", Optional = false, Fallback = "manual" }
            ]
        };

        // manual fallback means "user handles it" — should not throw
        var context = await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        context.Should().NotBeNull();
    }

    [Fact]
    public async Task MapToAgentContextAsync_NoToolDeclarations_Succeeds()
    {
        var skill = new SkillDefinition
        {
            Id = "simple",
            Name = "simple",
            Instructions = "Do simple things"
        };

        var context = await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        context.Should().NotBeNull();
    }
}
```

Note: Add `using Moq;` at the top — this test file uses Moq for the `IOptionsMonitor` setup.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "FullyQualifiedName~ToolEnforcementTests" --no-build`

Expected: `RequiredToolUnresolvable_ThrowsInvalidOperation` FAILS (no exception thrown). Others may pass.

- [ ] **Step 3: Implement required-tool enforcement**

Modify `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs`.

Replace lines 450-451 in `ProvisionToolAsync`:

```csharp
// OLD:
if (!declaration.Optional)
    _logger.LogWarning("Required tool {ToolName} could not be resolved", declaration.Name);

return null;
```

With:

```csharp
// NEW:
if (!declaration.Optional && !declaration.FallbackIsManual)
{
    throw new InvalidOperationException(
        $"Required tool '{declaration.Name}' could not be resolved. " +
        "Ensure the tool is registered via keyed DI or available from an MCP server. " +
        "Mark the tool declaration as Optional = true if the skill can function without it.");
}

return null;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "FullyQualifiedName~ToolEnforcementTests"`

Expected: All 4 tests PASS.

- [ ] **Step 5: Run all existing factory tests to verify no regressions**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "FullyQualifiedName~AgentExecutionContextFactory"`

Expected: All tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryToolEnforcementTests.cs
git commit -m "fix(tools): throw on unresolvable required tool declarations instead of warning"
```

---

### Task 3: Plugin Domain Models (Gap #3)

**Files:**
- Create: `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginsConfig.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginDeclaration.cs`
- Create: `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginManifest.cs`
- Modify: `src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs`

**Context:** These are pure config POCOs that map to `AppConfig:AI:Plugins` in appsettings.json. `PluginManifest` maps to `plugin.json` — the manifest shipped inside a plugin directory. Only local source type needed (no GitHub, no NuGet).

- [ ] **Step 1: Create PluginsConfig**

Create `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginsConfig.cs`:

```csharp
namespace Domain.Common.Config.AI.Plugins;

/// <summary>
/// Configuration for the plugin system. Maps to <c>AppConfig:AI:Plugins</c>.
/// </summary>
public class PluginsConfig
{
    /// <summary>
    /// Declared plugins to resolve and load at startup.
    /// </summary>
    public IReadOnlyList<PluginDeclaration> Packages { get; set; } = [];
}
```

- [ ] **Step 2: Create PluginDeclaration**

Create `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginDeclaration.cs`:

```csharp
namespace Domain.Common.Config.AI.Plugins;

/// <summary>
/// A single plugin the harness should load from a local directory.
/// </summary>
public class PluginDeclaration
{
    /// <summary>Plugin identifier (e.g., "azure", "my-custom-tools").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Local filesystem path to the plugin directory containing plugin.json.
    /// Absolute or relative to the application's working directory.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Whether this plugin is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Environment variable overrides for this plugin's MCP servers.
    /// Merged with the plugin's declared env vars (declaration wins on conflict).
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = new();
}
```

- [ ] **Step 3: Create PluginManifest**

Create `src/Content/Domain/Domain.Common/Config/AI/Plugins/PluginManifest.cs`:

```csharp
namespace Domain.Common.Config.AI.Plugins;

/// <summary>
/// Deserialized from plugin.json — compatible with Microsoft's azure-skills format.
/// </summary>
public class PluginManifest
{
    /// <summary>Plugin display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable description of the plugin.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Plugin version (semver).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Plugin author information.</summary>
    public PluginAuthor? Author { get; set; }

    /// <summary>Plugin homepage URL.</summary>
    public string? Homepage { get; set; }

    /// <summary>Source repository URL.</summary>
    public string? Repository { get; set; }

    /// <summary>License identifier (e.g., "MIT").</summary>
    public string? License { get; set; }

    /// <summary>Searchable keywords.</summary>
    public IReadOnlyList<string> Keywords { get; set; } = [];

    /// <summary>Relative path to skills directory (e.g., "./skills/").</summary>
    public string? Skills { get; set; }

    /// <summary>Relative path to MCP config file (e.g., "./.mcp.json").</summary>
    public string? McpServers { get; set; }

    /// <summary>Hook configuration.</summary>
    public PluginHooksManifest? Hooks { get; set; }
}

/// <summary>Plugin author with optional URL.</summary>
public record PluginAuthor(string Name, string? Url = null);

/// <summary>Hook configuration from plugin.json.</summary>
public class PluginHooksManifest
{
    /// <summary>Relative paths to hook scripts.</summary>
    public IReadOnlyList<string> Paths { get; set; } = [];

    /// <summary>Whether plugin hooks replace existing hooks of the same type.</summary>
    public bool Exclusive { get; set; }
}
```

- [ ] **Step 4: Add Plugins property to AIConfig**

Modify `src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs`.

Add import at top:
```csharp
using Domain.Common.Config.AI.Plugins;
```

Add property after the `ToolOutputCompression` property (before the closing brace):
```csharp
/// <summary>
/// Plugin system configuration: local plugin declarations for skill
/// and MCP server discovery from external directories.
/// </summary>
public PluginsConfig Plugins { get; set; } = new();
```

Update the configuration hierarchy comment to include `Plugins`:
```
/// └── Plugins              — Local plugin declarations for external skill/MCP discovery
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/Content/Domain/Domain.Common/Domain.Common.csproj`

Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Commit**

```powershell
git add src/Content/Domain/Domain.Common/Config/AI/Plugins/ src/Content/Domain/Domain.Common/Config/AI/AIConfig.cs
git commit -m "feat(plugins): add domain config models for local plugin system"
```

---

### Task 4: Plugin Application Interfaces (Gap #3)

**Files:**
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginManifestReader.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginLoader.cs`
- Create: `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginRegistry.cs`
- Modify: `src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs`

**Context:** Three interfaces: manifest reader (parses plugin.json), loader (wires plugin capabilities into harness config), registry (runtime query of loaded plugins). Also add `PluginSource` to `SkillDefinition` to track origin.

- [ ] **Step 1: Create IPluginManifestReader**

Create `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginManifestReader.cs`:

```csharp
using Domain.Common.Config.AI.Plugins;

namespace Application.AI.Common.Interfaces.Plugins;

/// <summary>
/// Reads and validates a plugin.json manifest from a plugin directory.
/// </summary>
public interface IPluginManifestReader
{
    /// <summary>
    /// Reads plugin.json from the given directory and deserializes it.
    /// </summary>
    /// <param name="pluginDirectory">Directory containing plugin.json.</param>
    /// <returns>The parsed manifest, or null if plugin.json is missing or invalid.</returns>
    PluginManifest? Read(string pluginDirectory);
}
```

- [ ] **Step 2: Create IPluginLoader**

Create `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginLoader.cs`:

```csharp
using Domain.Common.Config.AI.Plugins;

namespace Application.AI.Common.Interfaces.Plugins;

/// <summary>
/// Reads a plugin manifest and wires its capabilities into the harness configuration.
/// Adds skill paths, merges MCP server configs, and registers hooks.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Loads a plugin from a local directory and wires its skills and MCP servers.
    /// </summary>
    /// <param name="pluginPath">Absolute path to the plugin directory.</param>
    /// <param name="declaration">The plugin declaration from config.</param>
    /// <param name="manifest">The parsed plugin manifest.</param>
    /// <returns>The loaded plugin record, or null if loading failed.</returns>
    LoadedPlugin? Load(string pluginPath, PluginDeclaration declaration, PluginManifest manifest);
}

/// <summary>
/// A plugin that has been loaded and wired into the harness.
/// </summary>
public record LoadedPlugin(
    string Name,
    string Version,
    string LocalPath,
    PluginManifest Manifest,
    PluginLoadStatus Status,
    IReadOnlyList<string> SkillPaths,
    IReadOnlyList<string> McpServerNames);

/// <summary>Plugin load status.</summary>
public enum PluginLoadStatus
{
    /// <summary>Plugin loaded successfully.</summary>
    Loaded,

    /// <summary>Plugin failed to load.</summary>
    Failed,

    /// <summary>Plugin is disabled in configuration.</summary>
    Disabled
}
```

- [ ] **Step 3: Create IPluginRegistry**

Create `src/Content/Application/Application.AI.Common/Interfaces/Plugins/IPluginRegistry.cs`:

```csharp
namespace Application.AI.Common.Interfaces.Plugins;

/// <summary>
/// Runtime query interface for loaded plugins.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>All currently loaded plugins.</summary>
    IReadOnlyList<LoadedPlugin> GetLoadedPlugins();

    /// <summary>Get a specific loaded plugin by name.</summary>
    LoadedPlugin? GetPlugin(string name);

    /// <summary>Whether a plugin is loaded and active.</summary>
    bool IsLoaded(string name);

    /// <summary>Registers a loaded plugin.</summary>
    void Register(LoadedPlugin plugin);
}
```

- [ ] **Step 4: Add PluginSource to SkillDefinition**

Modify `src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs`.

Add to the `Categorization` region (after `Author`):

```csharp
/// <summary>
/// Name of the plugin this skill was loaded from, if any.
/// Null for harness-native skills. Used for namespace tracking and dual skill mode.
/// </summary>
public string? PluginSource { get; set; }
```

Add to the `Computed Properties` region:

```csharp
/// <summary>Whether this skill was loaded from a plugin.</summary>
public bool IsPluginSkill => !string.IsNullOrEmpty(PluginSource);
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`

Expected: BUILD SUCCEEDED.

- [ ] **Step 6: Commit**

```powershell
git add src/Content/Application/Application.AI.Common/Interfaces/Plugins/ src/Content/Domain/Domain.AI/Skills/SkillDefinition.cs
git commit -m "feat(plugins): add application interfaces and PluginSource on SkillDefinition"
```

---

### Task 5: PluginManifestReader Implementation (Gap #3)

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginManifestReader.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginManifestReaderTests.cs`

**Context:** Reads `plugin.json` from a directory, deserializes it into `PluginManifest` using System.Text.Json. Handles missing file, invalid JSON, and missing required fields.

- [ ] **Step 1: Write the failing tests**

Create `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginManifestReaderTests.cs`:

```csharp
using Application.AI.Common.Interfaces.Plugins;
using Domain.Common.Config.AI.Plugins;
using FluentAssertions;
using Infrastructure.AI.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Plugins;

public sealed class PluginManifestReaderTests : IDisposable
{
    private readonly PluginManifestReader _sut;
    private readonly string _tempDir;

    public PluginManifestReaderTests()
    {
        _sut = new PluginManifestReader(NullLogger<PluginManifestReader>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"manifest-reader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Read_ValidManifest_ReturnsPluginManifest()
    {
        File.WriteAllText(Path.Combine(_tempDir, "plugin.json"), """
            {
                "name": "azure",
                "description": "Azure cloud skills",
                "version": "1.1.48",
                "skills": "./skills/",
                "mcpServers": "./.mcp.json",
                "keywords": ["azure", "cloud"]
            }
            """);

        var result = _sut.Read(_tempDir);

        result.Should().NotBeNull();
        result!.Name.Should().Be("azure");
        result.Description.Should().Be("Azure cloud skills");
        result.Version.Should().Be("1.1.48");
        result.Skills.Should().Be("./skills/");
        result.McpServers.Should().Be("./.mcp.json");
        result.Keywords.Should().BeEquivalentTo(["azure", "cloud"]);
    }

    [Fact]
    public void Read_WithAuthor_DeserializesAuthorRecord()
    {
        File.WriteAllText(Path.Combine(_tempDir, "plugin.json"), """
            {
                "name": "test",
                "version": "1.0",
                "author": { "name": "Microsoft", "url": "https://microsoft.com" }
            }
            """);

        var result = _sut.Read(_tempDir);

        result.Should().NotBeNull();
        result!.Author.Should().NotBeNull();
        result.Author!.Name.Should().Be("Microsoft");
        result.Author.Url.Should().Be("https://microsoft.com");
    }

    [Fact]
    public void Read_MissingPluginJson_ReturnsNull()
    {
        var result = _sut.Read(_tempDir);

        result.Should().BeNull();
    }

    [Fact]
    public void Read_InvalidJson_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_tempDir, "plugin.json"), "not json {{{");

        var result = _sut.Read(_tempDir);

        result.Should().BeNull();
    }

    [Fact]
    public void Read_MinimalManifest_DefaultsEmptyCollections()
    {
        File.WriteAllText(Path.Combine(_tempDir, "plugin.json"), """
            { "name": "minimal", "version": "0.1.0" }
            """);

        var result = _sut.Read(_tempDir);

        result.Should().NotBeNull();
        result!.Keywords.Should().BeEmpty();
        result.Skills.Should().BeNull();
        result.McpServers.Should().BeNull();
        result.Hooks.Should().BeNull();
    }

    [Fact]
    public void Read_WithHooks_DeserializesHooksManifest()
    {
        File.WriteAllText(Path.Combine(_tempDir, "plugin.json"), """
            {
                "name": "hooked",
                "version": "1.0",
                "hooks": {
                    "paths": ["./hooks/pre-tool.sh"],
                    "exclusive": true
                }
            }
            """);

        var result = _sut.Read(_tempDir);

        result.Should().NotBeNull();
        result!.Hooks.Should().NotBeNull();
        result.Hooks!.Paths.Should().ContainSingle("./hooks/pre-tool.sh");
        result.Hooks.Exclusive.Should().BeTrue();
    }

    [Fact]
    public void Read_NonexistentDirectory_ReturnsNull()
    {
        var result = _sut.Read(Path.Combine(_tempDir, "does-not-exist"));

        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~PluginManifestReaderTests" --no-build`

Expected: Compilation error — `PluginManifestReader` class doesn't exist yet.

- [ ] **Step 3: Implement PluginManifestReader**

Create `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginManifestReader.cs`:

```csharp
using System.Text.Json;
using Application.AI.Common.Interfaces.Plugins;
using Domain.Common.Config.AI.Plugins;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Plugins;

/// <summary>
/// Reads and validates plugin.json manifests from plugin directories.
/// </summary>
public sealed class PluginManifestReader : IPluginManifestReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<PluginManifestReader> _logger;

    public PluginManifestReader(ILogger<PluginManifestReader> logger)
    {
        _logger = logger;
    }

    public PluginManifest? Read(string pluginDirectory)
    {
        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");

        if (!File.Exists(manifestPath))
        {
            _logger.LogDebug("No plugin.json found at {Path}", manifestPath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);

            if (manifest == null || string.IsNullOrEmpty(manifest.Name))
            {
                _logger.LogWarning("Invalid plugin manifest at {Path}: missing name", manifestPath);
                return null;
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse plugin.json at {Path}", manifestPath);
            return null;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~PluginManifestReaderTests"`

Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginManifestReader.cs src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginManifestReaderTests.cs
git commit -m "feat(plugins): add PluginManifestReader for plugin.json deserialization"
```

---

### Task 6: PluginLoader Implementation (Gap #3)

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginLoader.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginLoaderTests.cs`

**Context:** The loader reads a plugin's manifest and wires its capabilities into existing config objects:
1. Skills — adds the plugin's skills directory to `SkillsConfig.AdditionalPaths`
2. MCP servers — reads `.mcp.json`, merges namespaced entries into `McpServersConfig.Servers`
3. Returns a `LoadedPlugin` record

The loader receives `SkillsConfig` and `McpServersConfig` references and mutates them (config is built before DI freezes it). The `PluginSource` property on `SkillDefinition` is set later by `SkillMetadataRegistry` when it discovers skills from plugin paths.

- [ ] **Step 1: Write the failing tests**

Create `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginLoaderTests.cs`:

```csharp
using System.Text.Json;
using Application.AI.Common.Interfaces.Plugins;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.MCP;
using Domain.Common.Config.AI.Plugins;
using FluentAssertions;
using Infrastructure.AI.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Infrastructure.AI.Tests.Plugins;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly PluginLoader _sut;
    private readonly SkillsConfig _skillsConfig;
    private readonly McpServersConfig _mcpServersConfig;
    private readonly string _tempDir;

    public PluginLoaderTests()
    {
        _skillsConfig = new SkillsConfig();
        _mcpServersConfig = new McpServersConfig();
        _sut = new PluginLoader(
            _skillsConfig,
            _mcpServersConfig,
            NullLogger<PluginLoader>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"plugin-loader-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private PluginDeclaration MakeDeclaration(string name = "test-plugin") =>
        new() { Name = name, Path = _tempDir, Enabled = true };

    [Fact]
    public void Load_WithSkillsDirectory_AddsToSkillsConfig()
    {
        var skillsDir = Path.Combine(_tempDir, "skills");
        Directory.CreateDirectory(skillsDir);

        var manifest = new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            Skills = "./skills/"
        };

        var result = _sut.Load(_tempDir, MakeDeclaration(), manifest);

        result.Should().NotBeNull();
        result!.Status.Should().Be(PluginLoadStatus.Loaded);
        result.SkillPaths.Should().ContainSingle().Which.Should().Be(skillsDir);
        _skillsConfig.AdditionalPaths.Should().Contain(skillsDir);
    }

    [Fact]
    public void Load_WithMcpJson_MergesNamespacedServers()
    {
        var mcpConfig = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["azure"] = new { command = "npx", args = new[] { "azure-mcp" } }
            }
        };
        File.WriteAllText(
            Path.Combine(_tempDir, ".mcp.json"),
            JsonSerializer.Serialize(mcpConfig));

        var manifest = new PluginManifest
        {
            Name = "azure-plugin",
            Version = "1.0.0",
            McpServers = "./.mcp.json"
        };

        var result = _sut.Load(_tempDir, MakeDeclaration("azure-plugin"), manifest);

        result.Should().NotBeNull();
        result!.McpServerNames.Should().ContainSingle("azure-plugin:azure");
        _mcpServersConfig.Servers.Should().ContainKey("azure-plugin:azure");
        _mcpServersConfig.Servers["azure-plugin:azure"].Command.Should().Be("npx");
    }

    [Fact]
    public void Load_EnvOverrides_MergedIntoMcpServers()
    {
        var mcpConfig = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["server"] = new
                {
                    command = "node",
                    args = new[] { "server.js" },
                    env = new Dictionary<string, string> { ["KEY"] = "original" }
                }
            }
        };
        File.WriteAllText(
            Path.Combine(_tempDir, ".mcp.json"),
            JsonSerializer.Serialize(mcpConfig));

        var declaration = MakeDeclaration();
        declaration.Env["KEY"] = "overridden";

        var manifest = new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            McpServers = "./.mcp.json"
        };

        _sut.Load(_tempDir, declaration, manifest);

        _mcpServersConfig.Servers["test-plugin:server"].Env["KEY"].Should().Be("overridden");
    }

    [Fact]
    public void Load_NoSkillsOrMcp_ReturnsLoadedWithEmptyLists()
    {
        var manifest = new PluginManifest
        {
            Name = "bare",
            Version = "1.0.0"
        };

        var result = _sut.Load(_tempDir, MakeDeclaration("bare"), manifest);

        result.Should().NotBeNull();
        result!.Status.Should().Be(PluginLoadStatus.Loaded);
        result.SkillPaths.Should().BeEmpty();
        result.McpServerNames.Should().BeEmpty();
    }

    [Fact]
    public void Load_SkillsDirectoryMissing_SkipsSilently()
    {
        var manifest = new PluginManifest
        {
            Name = "no-skills",
            Version = "1.0.0",
            Skills = "./nonexistent-skills/"
        };

        var result = _sut.Load(_tempDir, MakeDeclaration("no-skills"), manifest);

        result.Should().NotBeNull();
        result!.SkillPaths.Should().BeEmpty();
    }

    [Fact]
    public void Load_McpJsonMissing_SkipsSilently()
    {
        var manifest = new PluginManifest
        {
            Name = "no-mcp",
            Version = "1.0.0",
            McpServers = "./missing.mcp.json"
        };

        var result = _sut.Load(_tempDir, MakeDeclaration("no-mcp"), manifest);

        result.Should().NotBeNull();
        result!.McpServerNames.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~PluginLoaderTests" --no-build`

Expected: Compilation error — `PluginLoader` class doesn't exist.

- [ ] **Step 3: Implement PluginLoader**

Create `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginLoader.cs`:

```csharp
using System.Text.Json;
using Application.AI.Common.Interfaces.Plugins;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.MCP;
using Domain.Common.Config.AI.Plugins;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Plugins;

/// <summary>
/// Wires a plugin's skills and MCP servers into the harness configuration.
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly SkillsConfig _skillsConfig;
    private readonly McpServersConfig _mcpServersConfig;
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(
        SkillsConfig skillsConfig,
        McpServersConfig mcpServersConfig,
        ILogger<PluginLoader> logger)
    {
        _skillsConfig = skillsConfig;
        _mcpServersConfig = mcpServersConfig;
        _logger = logger;
    }

    public LoadedPlugin? Load(string pluginPath, PluginDeclaration declaration, PluginManifest manifest)
    {
        var skillPaths = new List<string>();
        var mcpServerNames = new List<string>();

        try
        {
            if (!string.IsNullOrEmpty(manifest.Skills))
            {
                var skillsDir = Path.GetFullPath(
                    Path.Combine(pluginPath, manifest.Skills.TrimStart('.', '/')));

                if (Directory.Exists(skillsDir))
                {
                    skillPaths.Add(skillsDir);
                    _skillsConfig.AdditionalPaths = [.._skillsConfig.AdditionalPaths, skillsDir];

                    _logger.LogInformation(
                        "Plugin {Name}: added skill path {Path}",
                        declaration.Name, skillsDir);
                }
                else
                {
                    _logger.LogDebug(
                        "Plugin {Name}: skills directory not found at {Path}",
                        declaration.Name, skillsDir);
                }
            }

            if (!string.IsNullOrEmpty(manifest.McpServers))
            {
                var mcpPath = Path.GetFullPath(
                    Path.Combine(pluginPath, manifest.McpServers.TrimStart('.', '/')));

                if (File.Exists(mcpPath))
                    mcpServerNames.AddRange(LoadMcpServers(mcpPath, declaration));
                else
                    _logger.LogDebug(
                        "Plugin {Name}: MCP config not found at {Path}",
                        declaration.Name, mcpPath);
            }

            _logger.LogInformation(
                "Plugin {Name} v{Version} loaded: {SkillCount} skill path(s), {McpCount} MCP server(s)",
                declaration.Name, manifest.Version, skillPaths.Count, mcpServerNames.Count);

            return new LoadedPlugin(
                declaration.Name,
                manifest.Version,
                pluginPath,
                manifest,
                PluginLoadStatus.Loaded,
                skillPaths,
                mcpServerNames);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load plugin {Name}", declaration.Name);
            return new LoadedPlugin(
                declaration.Name,
                manifest.Version,
                pluginPath,
                manifest,
                PluginLoadStatus.Failed,
                [],
                []);
        }
    }

    private List<string> LoadMcpServers(string mcpPath, PluginDeclaration declaration)
    {
        var names = new List<string>();

        try
        {
            var json = File.ReadAllText(mcpPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("mcpServers", out var serversElement))
                return names;

            foreach (var serverProp in serversElement.EnumerateObject())
            {
                var namespacedName = $"{declaration.Name}:{serverProp.Name}";
                var definition = new McpServerDefinition
                {
                    Enabled = true,
                    Type = McpServerType.Stdio,
                    Description = $"[Plugin: {declaration.Name}] {serverProp.Name}"
                };

                if (serverProp.Value.TryGetProperty("command", out var cmd))
                    definition.Command = cmd.GetString() ?? string.Empty;

                if (serverProp.Value.TryGetProperty("args", out var args))
                    definition.Args = args.EnumerateArray()
                        .Select(a => a.GetString() ?? string.Empty)
                        .ToList();

                // Base env from plugin manifest
                if (serverProp.Value.TryGetProperty("env", out var env))
                {
                    foreach (var envProp in env.EnumerateObject())
                        definition.Env[envProp.Name] = envProp.Value.GetString() ?? string.Empty;
                }

                // Declaration env overrides
                foreach (var (key, value) in declaration.Env)
                    definition.Env[key] = value;

                _mcpServersConfig.Servers[namespacedName] = definition;
                names.Add(namespacedName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse MCP config at {Path}", mcpPath);
        }

        return names;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~PluginLoaderTests"`

Expected: All 6 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginLoader.cs src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginLoaderTests.cs
git commit -m "feat(plugins): add PluginLoader for wiring skills and MCP servers from plugins"
```

---

### Task 7: PluginRegistry Implementation (Gap #3)

**Files:**
- Create: `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginRegistry.cs`
- Create: `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginRegistryTests.cs`

**Context:** Simple in-memory registry tracking loaded plugins. Thread-safe, queryable by name.

- [ ] **Step 1: Write the failing tests**

Create `src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginRegistryTests.cs`:

```csharp
using Application.AI.Common.Interfaces.Plugins;
using Domain.Common.Config.AI.Plugins;
using FluentAssertions;
using Infrastructure.AI.Plugins;
using Xunit;

namespace Infrastructure.AI.Tests.Plugins;

public class PluginRegistryTests
{
    private readonly PluginRegistry _sut = new();

    private static LoadedPlugin MakePlugin(string name, PluginLoadStatus status = PluginLoadStatus.Loaded) =>
        new(name, "1.0.0", $"/plugins/{name}", new PluginManifest { Name = name, Version = "1.0.0" },
            status, [], []);

    [Fact]
    public void GetLoadedPlugins_Initially_ReturnsEmpty()
    {
        _sut.GetLoadedPlugins().Should().BeEmpty();
    }

    [Fact]
    public void Register_ThenGetPlugin_ReturnsPlugin()
    {
        var plugin = MakePlugin("azure");
        _sut.Register(plugin);

        _sut.GetPlugin("azure").Should().Be(plugin);
    }

    [Fact]
    public void IsLoaded_RegisteredPlugin_ReturnsTrue()
    {
        _sut.Register(MakePlugin("azure"));

        _sut.IsLoaded("azure").Should().BeTrue();
    }

    [Fact]
    public void IsLoaded_UnregisteredPlugin_ReturnsFalse()
    {
        _sut.IsLoaded("missing").Should().BeFalse();
    }

    [Fact]
    public void GetPlugin_CaseInsensitive_ReturnsPlugin()
    {
        _sut.Register(MakePlugin("Azure"));

        _sut.GetPlugin("azure").Should().NotBeNull();
        _sut.GetPlugin("AZURE").Should().NotBeNull();
    }

    [Fact]
    public void GetLoadedPlugins_MultiplePlugins_ReturnsAll()
    {
        _sut.Register(MakePlugin("a"));
        _sut.Register(MakePlugin("b"));
        _sut.Register(MakePlugin("c"));

        _sut.GetLoadedPlugins().Should().HaveCount(3);
    }

    [Fact]
    public void IsLoaded_FailedPlugin_ReturnsFalse()
    {
        _sut.Register(MakePlugin("broken", PluginLoadStatus.Failed));

        _sut.IsLoaded("broken").Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~PluginRegistryTests" --no-build`

Expected: Compilation error — `PluginRegistry` doesn't exist.

- [ ] **Step 3: Implement PluginRegistry**

Create `src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginRegistry.cs`:

```csharp
using System.Collections.Concurrent;
using Application.AI.Common.Interfaces.Plugins;

namespace Infrastructure.AI.Plugins;

/// <summary>
/// Thread-safe in-memory registry of loaded plugins.
/// </summary>
public sealed class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LoadedPlugin> GetLoadedPlugins() =>
        _plugins.Values.ToList();

    public LoadedPlugin? GetPlugin(string name) =>
        _plugins.GetValueOrDefault(name);

    public bool IsLoaded(string name) =>
        _plugins.TryGetValue(name, out var plugin) && plugin.Status == PluginLoadStatus.Loaded;

    public void Register(LoadedPlugin plugin) =>
        _plugins[plugin.Name] = plugin;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Infrastructure.AI.Tests --filter "FullyQualifiedName~PluginRegistryTests"`

Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Content/Infrastructure/Infrastructure.AI/Plugins/PluginRegistry.cs src/Content/Tests/Infrastructure.AI.Tests/Plugins/PluginRegistryTests.cs
git commit -m "feat(plugins): add PluginRegistry for tracking loaded plugins"
```

---

### Task 8: Plugin DI Registration and SkillDefinition PluginSource Wiring (Gap #3)

**Files:**
- Modify: `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs`
- Modify: `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs`

**Context:** Register plugin services in DI. Add a `pluginSource` parameter to `SkillMetadataParser.ParseFromFile` so skills loaded from plugin paths get their `PluginSource` set. The caller (`SkillMetadataRegistry`) passes the plugin name when it discovers skills from plugin-added paths.

The startup flow:
1. Host reads `PluginsConfig` from appsettings
2. Host creates `PluginManifestReader`, `PluginLoader`, `PluginRegistry` manually (pre-DI)
3. For each enabled plugin: read manifest, call loader (which mutates `SkillsConfig` and `McpServersConfig`)
4. Register `IPluginRegistry` as singleton with the populated registry
5. Normal DI registration proceeds — `SkillMetadataRegistry` discovers skills from all paths including plugin-added ones

- [ ] **Step 1: Add pluginSource parameter to SkillMetadataParser.ParseFromFile**

Modify `src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs`.

Update `ParseFromFile` signature to add an optional `pluginSource` parameter:

```csharp
public SkillDefinition ParseFromFile(string skillFilePath, string sourcePath, string? pluginSource = null)
```

Add to the returned object initializer (after `IsFullyLoaded = true`):

```csharp
PluginSource = pluginSource,
```

Update `Parse` the same way:

```csharp
public SkillDefinition Parse(string skillName, string? skillDescription, string body, string sourcePath, string? pluginSource = null)
```

And add to its returned object initializer:

```csharp
PluginSource = pluginSource,
```

- [ ] **Step 2: Register plugin services in DI**

Modify `src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs`.

Add imports at the top:

```csharp
using Application.AI.Common.Interfaces.Plugins;
using Infrastructure.AI.Plugins;
```

Add in the `AddInfrastructureAIDependencies` method, in the `Skills and agents` section (after line 128):

```csharp
// --- Plugins ---

services.AddSingleton<IPluginManifestReader, PluginManifestReader>();
```

Note: `IPluginRegistry` is registered by the host startup code after plugin loading completes (it's populated before DI is built). `IPluginLoader` is used only during startup, not resolved from DI.

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/AgenticHarness.slnx`

Expected: BUILD SUCCEEDED.

- [ ] **Step 4: Commit**

```powershell
git add src/Content/Infrastructure/Infrastructure.AI/DependencyInjection.cs src/Content/Infrastructure/Infrastructure.AI/Skills/SkillMetadataParser.cs
git commit -m "feat(plugins): register plugin services in DI and wire PluginSource through parser"
```

---

### Task 9: Dual Skill Mode — SkillMode Enum and Injected Tool Pass-Through (Gap #4)

**Files:**
- Create: `src/Content/Domain/Domain.AI/Skills/SkillMode.cs`
- Modify: `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs`
- Create: `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryDualModeTests.cs`

**Context:** Plugin skills (like azure-skills) have no `allowed-tools` or `ToolDeclarations` — they're pure markdown instruction sets. When the LLM runs with these skills, it should have access to all MCP tools from the plugin's MCP servers. The dual mode:
- **Managed** (default): harness-native skills with explicit tool declarations. Tools resolved via keyed DI + MCP provider per declaration.
- **Injected**: plugin skills without tool declarations. All available MCP tools are passed through (the MCP tools from the plugin's servers are already in the tool provider via the plugin loader wiring).

The key insight: `BuildToolsAsync` returns empty tools for injected skills because there are no `ToolDeclarations`, no `AllowedTools`, and no pre-created `Tools`. The fix is: when a skill is a plugin skill (`IsPluginSkill`) with no tool declarations, fetch all available MCP tools.

- [ ] **Step 1: Create SkillMode enum**

Create `src/Content/Domain/Domain.AI/Skills/SkillMode.cs`:

```csharp
namespace Domain.AI.Skills;

/// <summary>
/// Determines how tools are resolved for a skill.
/// </summary>
public enum SkillMode
{
    /// <summary>
    /// Harness-native skill with explicit tool declarations.
    /// Tools resolved via keyed DI and MCP provider per declaration.
    /// </summary>
    Managed,

    /// <summary>
    /// Plugin skill without tool declarations.
    /// All available MCP tools from the plugin's servers are passed through.
    /// </summary>
    Injected
}
```

- [ ] **Step 2: Add SkillMode computed property to SkillDefinition**

This is already handled by the `IsPluginSkill` property added in Task 4. The mode is derived:
- `IsPluginSkill && !HasToolDeclarations && !HasToolRestrictions` → Injected
- Everything else → Managed

No additional changes to `SkillDefinition` needed.

- [ ] **Step 3: Write the failing tests**

Create `src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryDualModeTests.cs`:

Note: `IMcpToolProvider.GetAllToolsAsync` returns `Dictionary<string, IList<AITool>>` (server name → tools). The injected mode implementation flattens this into a single list.

```csharp
using Application.AI.Common.Factories;
using Application.AI.Common.Interfaces;
using Domain.AI.Agents;
using Domain.AI.Skills;
using Domain.Common.Config;
using Domain.Common.Config.AI;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Application.AI.Common.Tests.Factories;

public class AgentExecutionContextFactoryDualModeTests
{
    private readonly Mock<IMcpToolProvider> _mcpToolProvider;
    private readonly AgentExecutionContextFactory _factory;

    public AgentExecutionContextFactoryDualModeTests()
    {
        var appConfig = new AppConfig
        {
            AI = new AIConfig
            {
                AgentFramework = new AgentFrameworkConfig
                {
                    DefaultDeployment = "gpt-4o",
                    ClientType = AIAgentFrameworkClientType.AzureOpenAI
                }
            }
        };
        var monitor = Mock.Of<IOptionsMonitor<AppConfig>>(m => m.CurrentValue == appConfig);

        _mcpToolProvider = new Mock<IMcpToolProvider>();

        _factory = new AgentExecutionContextFactory(
            NullLogger<AgentExecutionContextFactory>.Instance,
            monitor,
            new ServiceCollection().BuildServiceProvider(),
            NullLoggerFactory.Instance,
            mcpToolProvider: _mcpToolProvider.Object);
    }

    [Fact]
    public async Task MapToAgentContextAsync_InjectedPluginSkill_GetsAllMcpTools()
    {
        _mcpToolProvider
            .Setup(p => p.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IList<AITool>>
            {
                ["azure:azure"] = new List<AITool>
                {
                    AIFunctionFactory.Create(() => "result", "azure_deploy"),
                    AIFunctionFactory.Create(() => "result", "azure_validate")
                }
            });

        var skill = new SkillDefinition
        {
            Id = "azure-deploy",
            Name = "azure-deploy",
            Instructions = "Deploy Azure resources",
            PluginSource = "azure"
            // No AllowedTools, no ToolDeclarations — this is an injected skill
        };

        var context = await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        context.Tools.Should().HaveCount(2);
        context.Tools.Should().Contain(t => t.Name == "azure_deploy");
        context.Tools.Should().Contain(t => t.Name == "azure_validate");
    }

    [Fact]
    public async Task MapToAgentContextAsync_ManagedSkillWithAllowedTools_DoesNotGetAllMcpTools()
    {
        _mcpToolProvider
            .Setup(p => p.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IList<AITool>>());

        var skill = new SkillDefinition
        {
            Id = "managed-skill",
            Name = "managed-skill",
            Instructions = "Do managed things",
            PluginSource = "some-plugin",
            AllowedTools = ["tool_a"]
        };

        await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        // Should NOT call GetAllToolsAsync — has AllowedTools (managed mode)
        _mcpToolProvider.Verify(
            p => p.GetAllToolsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MapToAgentContextAsync_NonPluginSkillNoTools_DoesNotGetAllMcpTools()
    {
        var skill = new SkillDefinition
        {
            Id = "native-skill",
            Name = "native-skill",
            Instructions = "Native skill"
            // No PluginSource — this is a harness-native skill
        };

        await _factory.MapToAgentContextAsync(skill, new SkillAgentOptions());

        _mcpToolProvider.Verify(
            p => p.GetAllToolsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MapToAgentContextAsync_MixedManagedAndInjected_MergesTools()
    {
        _mcpToolProvider
            .Setup(p => p.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IList<AITool>>
            {
                ["azure:azure"] = new List<AITool>
                {
                    AIFunctionFactory.Create(() => "r", "plugin_tool_1"),
                    AIFunctionFactory.Create(() => "r", "plugin_tool_2")
                }
            });

        var managedSkill = new SkillDefinition
        {
            Id = "research",
            Name = "research",
            Instructions = "Research things",
            Tools = [AIFunctionFactory.Create(() => "r", "search_tool")]
        };
        var injectedSkill = new SkillDefinition
        {
            Id = "azure-deploy",
            Name = "azure-deploy",
            Instructions = "Deploy",
            PluginSource = "azure"
        };

        var context = await _factory.MapToAgentContextAsync(
            [managedSkill, injectedSkill], new SkillAgentOptions());

        context.Tools.Should().HaveCount(3);
        context.Tools.Should().Contain(t => t.Name == "search_tool");
        context.Tools.Should().Contain(t => t.Name == "plugin_tool_1");
        context.Tools.Should().Contain(t => t.Name == "plugin_tool_2");
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "FullyQualifiedName~DualModeTests" --no-build`

Expected: `InjectedPluginSkill_GetsAllMcpTools` FAILS (tools list is empty). Others may vary.

- [ ] **Step 5: Implement injected skill tool pass-through**

`IMcpToolProvider.GetAllToolsAsync` already exists (returns `Dictionary<string, IList<AITool>>`). No interface changes needed.

Modify `src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs`.

In the `BuildToolsAsync` method, add a check at the beginning (before step 1 "Pre-created tools"):

```csharp
private async Task<List<AITool>> BuildToolsAsync(SkillDefinition skill, SkillAgentOptions options)
{
    var tools = new List<AITool>();

    // Injected mode: plugin skill with no tool declarations gets all available MCP tools
    if (skill.IsPluginSkill && !skill.HasToolDeclarations && !skill.HasToolRestrictions
        && skill.Tools is not { Count: > 0 } && _mcpToolProvider != null)
    {
        var allMcpTools = await _mcpToolProvider.GetAllToolsAsync();
        foreach (var serverTools in allMcpTools.Values)
            tools.AddRange(serverTools);

        _logger.LogInformation(
            "Injected mode: skill {SkillId} from plugin {Plugin} received {Count} MCP tools",
            skill.Id, skill.PluginSource, tools.Count);

        // Still add additional tools from options
        if (options.AdditionalTools?.Count > 0)
            tools.AddRange(options.AdditionalTools);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return tools.Where(t => seen.Add(t.Name)).ToList();
    }

    // ... rest of existing BuildToolsAsync (managed mode) unchanged
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "FullyQualifiedName~DualModeTests"`

Expected: All 4 tests PASS.

- [ ] **Step 8: Run all factory tests to verify no regressions**

Run: `dotnet test src/Content/Tests/Application.AI.Common.Tests --filter "FullyQualifiedName~AgentExecutionContextFactory"`

Expected: All tests PASS.

- [ ] **Step 9: Commit**

```powershell
git add src/Content/Domain/Domain.AI/Skills/SkillMode.cs src/Content/Application/Application.AI.Common/Factories/AgentExecutionContextFactory.cs src/Content/Tests/Application.AI.Common.Tests/Factories/AgentExecutionContextFactoryDualModeTests.cs
git commit -m "feat(skills): add dual skill mode — injected plugin skills get all MCP tools"
```

---

### Task 10: Full Build Verification and Gap Tracker Update

**Files:**
- Modify: (none — verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build src/AgenticHarness.slnx`

Expected: BUILD SUCCEEDED with 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test src/AgenticHarness.slnx`

Expected: All tests PASS.

- [ ] **Step 3: Verify new test count**

Count the new tests added:
- Task 1: 6 tests (nested YAML parser)
- Task 2: 4 tests (required-tool enforcement)
- Task 5: 7 tests (manifest reader)
- Task 6: 6 tests (plugin loader)
- Task 7: 7 tests (plugin registry)
- Task 9: 4 tests (dual mode)
- Total: **34 new tests**

---

## Dependency Graph

```
Task 1 (Parser fix) ──┐
                       ├── Task 3 (Domain models) ── Task 4 (Interfaces) ── Task 5 (ManifestReader)
Task 2 (Tool enforce) ─┘                                                      │
                                                                    Task 6 (PluginLoader)
                                                                               │
                                                                    Task 7 (PluginRegistry)
                                                                               │
                                                                    Task 8 (DI + parser wiring)
                                                                               │
                                                                    Task 9 (Dual skill mode)
                                                                               │
                                                                    Task 10 (Verification)
```

Tasks 1 and 2 are independent (can be parallelized). Tasks 3-8 are sequential. Task 9 depends on Task 4 (PluginSource on SkillDefinition). Task 10 is the final verification.
