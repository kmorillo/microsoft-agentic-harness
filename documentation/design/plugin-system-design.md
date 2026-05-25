# Plugin System Design

## Problem

The harness has four extension points (skills, tools, MCP servers, hooks) but all require manual installation — copying files, editing config, recompiling for .NET tools. There's no way to declare "I want the azure-skills plugin" and have the harness fetch, cache, and wire it automatically.

External plugin ecosystems already exist (Microsoft's `azure-skills`, the `apm` multi-harness installer) but they target agent hosts like Claude Code and Cursor. Our harness can't consume them.

## Goals

1. Declare plugins in `appsettings.json` — the harness resolves, caches, and loads them at startup
2. Compatible with Microsoft's `plugin.json` manifest format (azure-skills, future plugins)
3. Support multiple sources: GitHub repos, local directories, NuGet packages
4. Automatically wire plugin capabilities into existing systems: skills → `ISkillMetadataRegistry`, MCP servers → `McpConnectionManager`, hooks → `IHookExecutor`
5. Version pinning with optional auto-update
6. Offline-capable — once cached, plugins work without network

## Non-Goals

- Runtime hot-loading of plugins (restart required)
- Plugin marketplace / discovery UI (future)
- Custom .NET tool assemblies from plugins (Phase 2 — security implications)
- Plugin authoring tooling

---

## Architecture

### Conceptual Flow

```
appsettings.json          Plugin Cache              DI Container
┌──────────────┐    ┌──────────────────┐    ┌────────────────────┐
│ Plugins:     │    │ .plugins/        │    │                    │
│  - azure     │───▶│  azure/1.1.48/   │───▶│ Skills registered  │
│    source:   │    │    plugin.json   │    │ MCP servers added  │
│    github:// │    │    skills/       │    │ Hooks wired        │
│    version:  │    │    .mcp.json     │    │                    │
│    1.1.48    │    │  my-plugin/dev/  │    │                    │
└──────────────┘    └──────────────────┘    └────────────────────┘
     Config             Resolver                   Loader
```

### Pipeline

```
Startup
  │
  ├─ 1. Read PluginsConfig from appsettings.json
  │
  ├─ 2. For each plugin declaration:
  │     ├─ 2a. Check cache (.plugins/{name}/{version}/)
  │     ├─ 2b. If miss → IPluginResolver fetches from source
  │     └─ 2c. Validate plugin.json manifest exists and parses
  │
  ├─ 3. Resolve transitive dependencies:
  │     ├─ 3a. Read each manifest's Dependencies[]
  │     ├─ 3b. For deps not in Packages[] → auto-resolve from same source type
  │     ├─ 3c. Build full dependency graph
  │     ├─ 3d. Detect cycles → fail with clear error
  │     └─ 3e. Topological sort → load order
  │
  ├─ 4. For each plugin (in dependency order):
  │     ├─ 4a. Register skill paths → SkillsConfig.AdditionalPaths
  │     ├─ 4b. Merge MCP server defs → McpServersConfig.Servers
  │     ├─ 4c. Register hooks → HooksConfig
  │     └─ 4d. Emit PluginLoaded event (observability)
  │
  └─ 5. Normal harness startup continues (skills discovered, MCP connected, etc.)
```

### Clean Architecture Placement

```
Domain.Common/Config/AI/Plugins/
├── PluginsConfig.cs              — Top-level config bound to AppConfig:AI:Plugins
├── PluginDeclaration.cs          — Single plugin entry in config
├── PluginSourceType.cs           — Enum: GitHub, Local, NuGet
└── PluginManifest.cs             — Parsed plugin.json (the manifest the plugin ships)

Application.AI.Common/Interfaces/Plugins/
├── IPluginResolver.cs            — Fetches plugin from source → local cache
├── IPluginLoader.cs              — Reads manifest, wires into DI
├── IPluginRegistry.cs            — Runtime query: what's loaded, versions, status
├── IPluginManifestReader.cs      — Parses plugin.json into PluginManifest
└── IPluginDependencyResolver.cs  — Builds dependency graph, topological sort, cycle detection

Infrastructure.AI/Plugins/
├── Resolvers/
│   ├── GitHubPluginResolver.cs   — Clone/download from GitHub repos
│   ├── LocalPluginResolver.cs    — Symlink/copy from local path
│   └── NuGetPluginResolver.cs    — Extract from NuGet package (Phase 2)
├── PluginManifestReader.cs       — JSON deserialization of plugin.json
├── PluginLoader.cs               — Wires manifest contents into config/DI
├── PluginCache.cs                — Manages .plugins/ directory, version checks
├── PluginRegistry.cs             — Tracks loaded plugins, exposes status
└── PluginDependencyResolver.cs   — Transitive dependency resolution + cycle detection

Infrastructure.AI/DependencyInjection.Plugins.cs
  — Registers resolver, loader, cache, registry

Presentation layer (host startup):
  — Calls IPluginResolver + IPluginLoader before normal DI registration
```

---

## Domain Models

### PluginsConfig

```csharp
// Bound to AppConfig:AI:Plugins
public class PluginsConfig
{
    /// <summary>
    /// Local directory where resolved plugins are cached.
    /// Default: ".plugins" relative to working directory.
    /// </summary>
    public string CachePath { get; set; } = ".plugins";

    /// <summary>
    /// Whether to check for plugin updates on startup.
    /// When false, uses cached version if available.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Declared plugins to resolve and load.
    /// </summary>
    public IReadOnlyList<PluginDeclaration> Packages { get; set; } = [];
}
```

### PluginDeclaration

```csharp
// A single plugin the harness should load
public class PluginDeclaration
{
    /// <summary>Plugin identifier (e.g., "azure", "my-custom-tools").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Where to fetch the plugin from.</summary>
    public PluginSourceType Source { get; set; }

    /// <summary>
    /// Source-specific location.
    /// GitHub: "microsoft/azure-skills"
    /// Local: "C:/plugins/my-plugin" or "./plugins/my-plugin"
    /// NuGet: "Microsoft.Azure.Skills" (Phase 2)
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Version constraint. Semver range for GitHub/NuGet, ignored for Local.
    /// Examples: "1.1.48", "^1.0.0", "latest"
    /// </summary>
    public string Version { get; set; } = "latest";

    /// <summary>
    /// Subdirectory within the source that contains plugin.json.
    /// For azure-skills: ".github/plugins/azure-skills"
    /// Default: root of the repo/package.
    /// </summary>
    public string? PluginPath { get; set; }

    /// <summary>Whether this plugin is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Environment variable overrides for this plugin's MCP servers.
    /// Merged with the plugin's declared env vars (declaration wins on conflict).
    /// </summary>
    public Dictionary<string, string> Env { get; set; } = new();
}
```

### PluginSourceType

```csharp
public enum PluginSourceType
{
    /// <summary>GitHub repository. Location = "owner/repo".</summary>
    GitHub,

    /// <summary>Local filesystem directory. Location = absolute or relative path.</summary>
    Local,

    /// <summary>NuGet package (Phase 2). Location = package ID.</summary>
    NuGet
}
```

### PluginManifest

```csharp
// Deserialized from plugin.json — compatible with Microsoft's format
public class PluginManifest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public PluginAuthor? Author { get; set; }
    public string? Homepage { get; set; }
    public string? Repository { get; set; }
    public string? License { get; set; }
    public IReadOnlyList<string> Keywords { get; set; } = [];

    /// <summary>Relative path to skills directory (e.g., "./skills/").</summary>
    public string? Skills { get; set; }

    /// <summary>Relative path to MCP config file (e.g., "./.mcp.json").</summary>
    public string? McpServers { get; set; }

    /// <summary>Hook configuration.</summary>
    public PluginHooksManifest? Hooks { get; set; }

    /// <summary>
    /// Plugin dependencies. Each entry names a required plugin and optionally
    /// provides a version constraint and source hint for auto-resolution.
    /// </summary>
    public IReadOnlyList<PluginDependency> Dependencies { get; set; } = [];
}

public record PluginAuthor(string Name, string? Url = null);

public class PluginHooksManifest
{
    public IReadOnlyList<string> Paths { get; set; } = [];
    public bool Exclusive { get; set; }
}
```

---

## Application Interfaces

### IPluginResolver

```csharp
public interface IPluginResolver
{
    /// <summary>Which source type this resolver handles.</summary>
    PluginSourceType SourceType { get; }

    /// <summary>
    /// Resolves a plugin declaration to a local directory.
    /// Downloads/clones if not cached, validates manifest exists.
    /// Returns the local path to the plugin root.
    /// </summary>
    Task<Result<string>> ResolveAsync(
        PluginDeclaration declaration,
        string cachePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a newer version is available without downloading.
    /// </summary>
    Task<Result<PluginVersionInfo>> CheckForUpdateAsync(
        PluginDeclaration declaration,
        string currentVersion,
        CancellationToken cancellationToken = default);
}
```

### IPluginLoader

```csharp
public interface IPluginLoader
{
    /// <summary>
    /// Reads a plugin manifest and wires its capabilities into the harness.
    /// Adds skill paths, merges MCP server configs, registers hooks.
    /// </summary>
    Result<LoadedPlugin> Load(
        string pluginPath,
        PluginDeclaration declaration);
}
```

### IPluginRegistry

```csharp
public interface IPluginRegistry
{
    /// <summary>All currently loaded plugins.</summary>
    IReadOnlyList<LoadedPlugin> GetLoadedPlugins();

    /// <summary>Get a specific loaded plugin by name.</summary>
    LoadedPlugin? GetPlugin(string name);

    /// <summary>Whether a plugin is loaded and active.</summary>
    bool IsLoaded(string name);
}

public record LoadedPlugin(
    string Name,
    string Version,
    string LocalPath,
    PluginManifest Manifest,
    PluginLoadStatus Status,
    IReadOnlyList<string> SkillPaths,
    IReadOnlyList<string> McpServerNames);

public enum PluginLoadStatus
{
    Loaded,
    Failed,
    Disabled
}
```

### IPluginManifestReader

```csharp
public interface IPluginManifestReader
{
    /// <summary>
    /// Reads and validates a plugin.json manifest from the given directory.
    /// </summary>
    Result<PluginManifest> Read(string pluginDirectory);
}
```

### IPluginDependencyResolver

```csharp
public interface IPluginDependencyResolver
{
    /// <summary>
    /// Given the set of explicitly declared plugins (already resolved to local paths),
    /// reads their manifests, discovers transitive dependencies, resolves any that
    /// aren't already declared, and returns the full set in load order.
    /// </summary>
    /// <param name="resolved">
    /// Already-resolved plugins: declaration + local path + parsed manifest.
    /// </param>
    /// <param name="resolvers">
    /// Available resolvers keyed by source type, used to fetch transitive deps.
    /// </param>
    /// <param name="cachePath">Plugin cache directory.</param>
    /// <returns>
    /// Ordered list of all plugins (declared + transitive) in dependency-first order.
    /// Fails with a clear error if a cycle is detected or a dependency can't be resolved.
    /// </returns>
    Task<Result<IReadOnlyList<ResolvedPlugin>>> ResolveAsync(
        IReadOnlyList<ResolvedPlugin> resolved,
        IReadOnlyDictionary<PluginSourceType, IPluginResolver> resolvers,
        string cachePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A plugin that has been resolved to a local path and its manifest parsed.
/// Intermediate type between resolution and loading.
/// </summary>
public record ResolvedPlugin(
    PluginDeclaration Declaration,
    string LocalPath,
    PluginManifest Manifest);
```

---

## Infrastructure — Resolvers

### GitHubPluginResolver

Fetches plugins from GitHub repositories.

**Strategy**: Download release tarball (preferred) or clone at tag/branch.

```
Resolution flow:
1. Parse location → owner/repo
2. Check cache: .plugins/{name}/{version}/
3. If cached and no update check → return cached path
4. If "latest" → gh api repos/{owner}/{repo}/releases/latest → get tag
5. Download tarball → extract to cache directory
6. If PluginPath specified → validate subdirectory exists
7. Return resolved path (cachePath + pluginPath)
```

**Why tarball over clone?**
- Faster (no git history)
- Smaller disk footprint
- Deterministic (tagged releases)
- Clone available as fallback for branch-pinned dev scenarios

**Caching structure:**
```
.plugins/
├── azure/
│   ├── 1.1.48/           ← pinned version
│   │   ├── plugin.json
│   │   ├── skills/
│   │   └── .mcp.json
│   └── latest.lock       ← records resolved version for "latest"
└── my-tools/
    └── dev/               ← local plugin (symlinked, no version)
```

### LocalPluginResolver

For development: points to a local directory. No download, no caching. Creates a symlink in `.plugins/` for consistency.

### NuGetPluginResolver (Phase 2)

For .NET tool assemblies shipped as NuGet packages. Extract to cache, load assemblies via `AssemblyLoadContext`. **Deferred** — security implications of loading arbitrary assemblies need design.

---

## Infrastructure — Dependency Resolution

Plugins can declare dependencies on other plugins via the `Dependencies` field in `plugin.json`. The dependency resolver handles transitive resolution, cycle detection, and load ordering.

### PluginDependency Model

```csharp
// In PluginManifest — replaces the simple string list
public class PluginDependency
{
    /// <summary>Plugin name this plugin depends on.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional version constraint. Semver range (e.g., "^1.0.0", ">=2.0.0").
    /// If omitted, any version satisfies.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Optional source hint for auto-resolution of transitive deps.
    /// If the dependency isn't explicitly declared in PluginsConfig,
    /// the resolver uses this to fetch it automatically.
    /// </summary>
    public PluginDependencySource? Source { get; set; }
}

public class PluginDependencySource
{
    public PluginSourceType Type { get; set; }
    public string Location { get; set; } = string.Empty;
    public string? PluginPath { get; set; }
}
```

### Resolution Algorithm

```
Input: List<ResolvedPlugin> explicitly declared plugins
Output: List<ResolvedPlugin> in dependency-first topological order

1. Build initial graph from declared plugins
2. For each plugin P in graph:
   a. Read P.Manifest.Dependencies
   b. For each dep D:
      - If D.Name already in graph → add edge P→D
      - If D.Name NOT in graph:
        i.  If D.Source is provided → resolve from that source
        ii. If D.Source is null → check if any declared plugin provides it
            (look at manifest.Name across all resolved plugins)
        iii. If still unresolved → fail with:
             "Plugin '{P.Name}' depends on '{D.Name}' which is not declared
              and has no source hint. Add it to Plugins.Packages or add a
              Source hint in {P.Name}'s plugin.json Dependencies."
      - Add resolved dep to graph, recurse (step 2)
3. Cycle detection: Kahn's algorithm (BFS topological sort)
   - If any nodes remain after sort → cycle exists
   - Report cycle path: "Cycle detected: A → B → C → A"
4. Return sorted list (dependencies before dependents)
```

### Transitive Dependency Example

```
Plugin A (declared) depends on B
Plugin B (not declared, auto-resolved from A's source hint) depends on C
Plugin C (not declared, auto-resolved from B's source hint)

Config declares only A:
  Packages: [{ Name: "A", Source: "GitHub", Location: "org/plugin-a" }]

A's plugin.json:
  Dependencies: [{ Name: "B", Source: { Type: "GitHub", Location: "org/plugin-b" } }]

B's plugin.json:
  Dependencies: [{ Name: "C", Source: { Type: "GitHub", Location: "org/plugin-c" } }]

Resolution order: C → B → A
Load order: C first, then B, then A
```

### Conflict Resolution

When multiple plugins depend on the same transitive dependency at different versions:

1. **Explicit declaration wins** — if the user declared the dependency in `Packages[]` with a specific version, that version is used regardless of what dependents request
2. **Highest compatible version** — among transitive version constraints, pick the highest version that satisfies all constraints
3. **Fail on incompatible ranges** — if plugin A needs `^1.0.0` and plugin B needs `^2.0.0`, fail with a clear error listing the conflict. The user resolves by adding an explicit declaration

---

## Infrastructure — PluginLoader

The loader reads a resolved plugin's manifest and merges its capabilities into the harness configuration.

### Skill Registration

```csharp
// Adds the plugin's skills directory to SkillsConfig.AdditionalPaths
// so SkillMetadataRegistry discovers them during normal startup
var skillsPath = Path.Combine(pluginPath, manifest.Skills);
if (Directory.Exists(skillsPath))
{
    skillsConfig.AdditionalPaths = [..skillsConfig.AdditionalPaths, skillsPath];
}
```

Skills are discovered by the existing `SkillMetadataRegistry` — no new discovery mechanism needed. The loader just adds paths.

### MCP Server Registration

```csharp
// Reads .mcp.json from plugin, merges into McpServersConfig
// Plugin servers are namespaced: "azure:azure" to avoid collisions
var mcpConfigPath = Path.Combine(pluginPath, manifest.McpServers);
var pluginMcp = JsonSerializer.Deserialize<McpPluginConfig>(File.ReadAllText(mcpConfigPath));

foreach (var (name, server) in pluginMcp.McpServers)
{
    var namespacedName = $"{declaration.Name}:{name}";
    var definition = new McpServerDefinition
    {
        Enabled = true,
        Type = McpServerType.Stdio,  // or detect from config
        Command = server.Command,
        Args = server.Args,
        Env = MergeEnv(server.Env, declaration.Env),
        Description = $"[Plugin: {declaration.Name}] {name}"
    };
    mcpServersConfig.Servers[namespacedName] = definition;
}
```

### Hook Registration

Hooks from plugins are registered into the existing hook system. The `exclusive` flag in the manifest controls whether plugin hooks replace or supplement existing hooks of the same type.

---

## Configuration Example

```json
{
  "AppConfig": {
    "AI": {
      "Plugins": {
        "CachePath": ".plugins",
        "CheckForUpdates": true,
        "Packages": [
          {
            "Name": "azure",
            "Source": "GitHub",
            "Location": "microsoft/azure-skills",
            "Version": "latest",
            "PluginPath": ".github/plugins/azure-skills",
            "Enabled": true,
            "Env": {
              "AZURE_SUBSCRIPTION_ID": "${AZURE_SUBSCRIPTION_ID}"
            }
          },
          {
            "Name": "my-local-tools",
            "Source": "Local",
            "Location": "./plugins/my-tools",
            "Enabled": true
          }
        ]
      }
    }
  }
}
```

---

## Startup Integration

Plugin resolution runs **before** normal DI registration so that skill paths and MCP server configs are populated before services that depend on them are constructed.

```csharp
// In Presentation host startup (Program.cs or DI composition root)
public static async Task<IServiceCollection> AddPluginDependencies(
    this IServiceCollection services,
    PluginsConfig config,
    SkillsConfig skillsConfig,
    McpServersConfig mcpServersConfig,
    ILogger logger)
{
    var manifestReader = new PluginManifestReader();
    var resolvers = new Dictionary<PluginSourceType, IPluginResolver>
    {
        [PluginSourceType.GitHub] = new GitHubPluginResolver(logger),
        [PluginSourceType.Local] = new LocalPluginResolver(logger),
    };
    var dependencyResolver = new PluginDependencyResolver(manifestReader, logger);
    var loader = new PluginLoader(manifestReader, skillsConfig, mcpServersConfig, logger);
    var registry = new PluginRegistry();

    // Phase 1: Resolve explicitly declared plugins
    var resolved = new List<ResolvedPlugin>();
    foreach (var declaration in config.Packages.Where(p => p.Enabled))
    {
        var resolver = resolvers[declaration.Source];
        var resolveResult = await resolver.ResolveAsync(declaration, config.CachePath);
        if (!resolveResult.IsSuccess)
        {
            logger.LogWarning("Plugin {Name} failed to resolve: {Error}",
                declaration.Name, resolveResult.Error);
            registry.Register(declaration.Name, PluginLoadStatus.Failed);
            continue;
        }

        var manifest = manifestReader.Read(resolveResult.Value);
        if (!manifest.IsSuccess)
        {
            logger.LogWarning("Plugin {Name} has invalid manifest: {Error}",
                declaration.Name, manifest.Error);
            registry.Register(declaration.Name, PluginLoadStatus.Failed);
            continue;
        }

        resolved.Add(new ResolvedPlugin(declaration, resolveResult.Value, manifest.Value));
    }

    // Phase 2: Resolve transitive dependencies and compute load order
    var orderedResult = await dependencyResolver.ResolveAsync(
        resolved, resolvers, config.CachePath);

    if (!orderedResult.IsSuccess)
    {
        logger.LogError("Plugin dependency resolution failed: {Error}", orderedResult.Error);
        // Load what we can without dependency ordering
    }

    var loadOrder = orderedResult.IsSuccess ? orderedResult.Value : resolved;

    // Phase 3: Load in dependency-first order
    foreach (var plugin in loadOrder)
    {
        var loadResult = loader.Load(plugin.LocalPath, plugin.Declaration);
        registry.Register(loadResult);
    }

    services.AddSingleton<IPluginRegistry>(registry);
    return services;
}
```

---

## Compatibility with azure-skills

The azure-skills `plugin.json` maps directly to our `PluginManifest`:

| azure-skills field | PluginManifest field | Notes |
|---|---|---|
| `name` | `Name` | Direct |
| `description` | `Description` | Direct |
| `version` | `Version` | Direct |
| `author` | `Author` | `{ name, url }` → `PluginAuthor` record |
| `skills` | `Skills` | Relative path to skills dir |
| `mcpServers` | `McpServers` | Relative path to .mcp.json |
| `hooks` | `Hooks` | `{ paths, exclusive }` → `PluginHooksManifest` |
| `homepage` | `Homepage` | Direct |
| `repository` | `Repository` | Direct |
| `license` | `License` | Direct |
| `keywords` | `Keywords` | Direct |

The `.mcp.json` format (`{ "mcpServers": { "name": { "command", "args" } } }`) maps to our existing `McpServerDefinition` with `Type = Stdio`.

**No adapter layer needed** — the formats are directly compatible.

---

## Skill Format Compatibility

Azure-skills SKILL.md uses standard frontmatter:

```yaml
---
name: azure-deploy
description: "..."
license: MIT
metadata:
  author: Microsoft
  version: "1.1.2"
---
```

Our `SkillMetadataParser` already parses:
- `name`, `description` → direct match
- `metadata.version` → needs minor parser update to read nested YAML
- `license` → new optional field (informational only)

The parser extracts harness-specific fields (`category`, `tags`, `skill_type`, `allowed-tools`) which azure-skills doesn't use. These default gracefully — skills without them are still discoverable and loadable.

**One parser change needed**: tolerate `metadata:` as a nested YAML block for version/author. Currently the parser reads flat key-value pairs only.

---

## Phased Implementation

### Phase 1 — Local + GitHub resolvers, dependency resolution, skill + MCP wiring

- Domain models: `PluginsConfig`, `PluginDeclaration`, `PluginManifest`, `PluginSourceType`, `PluginDependency`, `PluginDependencySource`
- Application interfaces: `IPluginResolver`, `IPluginLoader`, `IPluginRegistry`, `IPluginManifestReader`, `IPluginDependencyResolver`
- Infrastructure: `LocalPluginResolver`, `GitHubPluginResolver`, `PluginManifestReader`, `PluginLoader`, `PluginCache`, `PluginRegistry`, `PluginDependencyResolver`
- Presentation: startup integration, DI registration
- Test: azure-skills as integration test target, dependency graph unit tests (cycle detection, transitive resolution, conflict detection)
- Parser update: handle nested `metadata:` in SKILL.md frontmatter
- `SkillDefinition` update: add `PluginSource` property for namespace tracking

**Deliverable**: `dotnet run` with azure-skills configured in appsettings.json discovers 27 Azure skills, connects the Azure MCP server, and correctly resolves any declared plugin dependencies in load order.

### Phase 2 — NuGet resolver + .NET tool assemblies

- `NuGetPluginResolver` — download and extract NuGet packages
- Assembly loading via `AssemblyLoadContext` — isolated plugin assemblies
- Plugin manifest `tools` field — declare ITool implementations by type name
- Security: assembly signing validation, sandboxed execution context

### Phase 3 — Version management + update flow

- Semver range resolution (not just "latest" or exact)
- `dotnet harness plugin update` CLI command
- Lock file (`.plugins/plugin-lock.json`) for reproducible builds
- Update notifications on startup

### Phase 4 — Plugin marketplace

- Plugin search/browse from a registry
- `dotnet harness plugin search azure`
- Community plugin submissions
- Plugin ratings/reviews

---

## Security Considerations

1. **GitHub downloads**: Use HTTPS, verify release checksums when available, pin to specific tags (not branches) in production
2. **MCP server processes**: Plugin MCP servers inherit the harness's sandbox constraints. `SandboxConfig` applies to plugin-spawned processes
3. **No arbitrary code execution in Phase 1**: Plugins provide data (skills, config) not executable code. .NET assembly loading is deferred to Phase 2 with explicit security design
4. **Env var injection**: Plugin declarations can override env vars for their MCP servers. The harness never exposes its own secrets to plugin processes unless explicitly configured
5. **Namespace isolation**: Plugin MCP servers are prefixed with the plugin name (`azure:azure`) to prevent collisions with harness-configured servers

---

## Decisions

1. **Plugin dependencies**: Full transitive resolution in Phase 1. Plugins declare dependencies with optional source hints for auto-resolution. Cycle detection via Kahn's algorithm. Conflict resolution: explicit > highest compatible > fail on incompatible ranges.

2. **Plugin cache gitignored**: Yes. Add `.plugins/` to `.gitignore`. It's a resolved artifact, not source.

3. **Skill namespacing**: Yes. Plugin skills are namespaced (`azure:azure-deploy`). The loader adds a `PluginSource` property to `SkillDefinition` to distinguish origin. Harness skills take precedence when names collide after stripping the namespace prefix.

4. **`apm.yml` support**: No. We consume `plugin.json` which is the runtime manifest. `apm.yml` is an installer config for the APM tool, not for harness consumption.

## Open Questions

1. **Update semantics for "latest"**: Should we check on every startup, or only when explicitly asked?
   - **Recommendation**: `CheckForUpdates: true` checks on startup but uses cache if offline. A `latest.lock` file records the resolved version. Force update via CLI or config.

2. **Transitive dependency source inference**: When a dep has no `Source` hint, should we assume the same source type as the parent plugin?
   - **Recommendation**: Yes — if plugin A is from GitHub `org/plugin-a` and depends on `B` with no source, try `org/plugin-b` on GitHub as a convention. Fail clearly if not found.
