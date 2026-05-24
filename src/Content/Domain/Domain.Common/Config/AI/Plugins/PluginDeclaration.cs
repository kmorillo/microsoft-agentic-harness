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
