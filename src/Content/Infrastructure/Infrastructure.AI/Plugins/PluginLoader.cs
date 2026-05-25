using System.Text.Json;
using Application.AI.Common.Interfaces.Plugins;
using Domain.Common.Config.AI;
using Domain.Common.Config.AI.MCP;
using Domain.Common.Config.AI.Plugins;
using Microsoft.Extensions.Logging;

namespace Infrastructure.AI.Plugins;

/// <summary>
/// Wires a plugin's skills and MCP servers into the harness configuration.
/// Skills are added to <see cref="SkillsConfig.AdditionalPaths"/>; MCP servers are merged
/// into <see cref="McpServersConfig.Servers"/> under namespaced keys (plugin-name:server-name).
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private readonly SkillsConfig _skillsConfig;
    private readonly McpServersConfig _mcpServersConfig;
    private readonly ILogger<PluginLoader> _logger;

    /// <summary>Initializes a new instance of <see cref="PluginLoader"/>.</summary>
    public PluginLoader(
        SkillsConfig skillsConfig,
        McpServersConfig mcpServersConfig,
        ILogger<PluginLoader> logger)
    {
        _skillsConfig = skillsConfig;
        _mcpServersConfig = mcpServersConfig;
        _logger = logger;
    }

    /// <inheritdoc />
    public LoadedPlugin? Load(string pluginPath, PluginDeclaration declaration, PluginManifest manifest)
    {
        var skillPaths = new List<string>();
        var mcpServerNames = new List<string>();

        try
        {
            if (!string.IsNullOrEmpty(manifest.Skills))
                skillPaths.AddRange(LoadSkills(pluginPath, declaration, manifest.Skills));

            if (!string.IsNullOrEmpty(manifest.McpServers))
                mcpServerNames.AddRange(LoadMcpServers(pluginPath, declaration, manifest.McpServers));

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

    private List<string> LoadSkills(string pluginPath, PluginDeclaration declaration, string skillsRelativePath)
    {
        var skillsDir = Path.GetFullPath(Path.Combine(pluginPath, skillsRelativePath))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!IsContainedWithin(skillsDir, pluginPath))
        {
            _logger.LogWarning(
                "Plugin {Name}: skills path {Path} escapes plugin directory, skipping",
                declaration.Name, skillsDir);
            return [];
        }

        if (!Directory.Exists(skillsDir))
        {
            _logger.LogDebug(
                "Plugin {Name}: skills directory not found at {Path}",
                declaration.Name, skillsDir);
            return [];
        }

        _skillsConfig.AdditionalPaths = _skillsConfig.AdditionalPaths.Append(skillsDir).ToList();

        _logger.LogInformation(
            "Plugin {Name}: added skill path {Path}",
            declaration.Name, skillsDir);

        return [skillsDir];
    }

    private List<string> LoadMcpServers(string pluginPath, PluginDeclaration declaration, string mcpRelativePath)
    {
        var mcpPath = Path.GetFullPath(Path.Combine(pluginPath, mcpRelativePath));

        if (!IsContainedWithin(mcpPath, pluginPath))
        {
            _logger.LogWarning(
                "Plugin {Name}: MCP config path {Path} escapes plugin directory, skipping",
                declaration.Name, mcpPath);
            return [];
        }

        if (!File.Exists(mcpPath))
        {
            _logger.LogDebug(
                "Plugin {Name}: MCP config not found at {Path}",
                declaration.Name, mcpPath);
            return [];
        }

        return ParseAndMergeMcpServers(mcpPath, declaration);
    }

    private List<string> ParseAndMergeMcpServers(string mcpPath, PluginDeclaration declaration)
    {
        var names = new List<string>();

        try
        {
            var json = File.ReadAllText(mcpPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (!doc.RootElement.TryGetProperty("mcpServers", out var serversElement))
                return names;

            foreach (var serverProp in serversElement.EnumerateObject())
            {
                var namespacedName = $"{declaration.Name}:{serverProp.Name}";
                var definition = BuildServerDefinition(serverProp.Value, declaration, serverProp.Name);

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

    private static bool IsContainedWithin(string resolvedPath, string basePath)
    {
        var canonicalBase = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var canonicalTarget = Path.GetFullPath(resolvedPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return canonicalTarget.StartsWith(canonicalBase + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(canonicalTarget, canonicalBase, StringComparison.Ordinal);
    }

    private static McpServerDefinition BuildServerDefinition(
        JsonElement serverElement,
        PluginDeclaration declaration,
        string serverName)
    {
        var definition = new McpServerDefinition
        {
            Enabled = true,
            Type = McpServerType.Stdio,
            Description = $"[Plugin: {declaration.Name}] {serverName}"
        };

        if (serverElement.TryGetProperty("command", out var cmd))
            definition.Command = cmd.GetString() ?? string.Empty;

        if (serverElement.TryGetProperty("args", out var args))
            definition.Args = args.EnumerateArray()
                .Select(a => a.GetString() ?? string.Empty)
                .ToList();

        if (serverElement.TryGetProperty("env", out var env))
        {
            foreach (var envProp in env.EnumerateObject())
                definition.Env[envProp.Name] = envProp.Value.GetString() ?? string.Empty;
        }

        // Declaration env overrides take precedence over manifest-declared env
        foreach (var (key, value) in declaration.Env)
            definition.Env[key] = value;

        return definition;
    }
}
