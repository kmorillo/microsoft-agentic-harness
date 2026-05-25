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
    IReadOnlyList<string> McpServerNames,
    PluginDeclaration Declaration);

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
