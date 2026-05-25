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

    /// <summary>
    /// Tool name whitelist for Injected-mode skills. When non-null, only these tools
    /// are provisioned. Applied before <see cref="DeniedTools"/>.
    /// </summary>
    public IReadOnlyList<string>? AllowedTools { get; set; }

    /// <summary>
    /// Tool name blacklist. Matched tools are removed after AllowedTools filtering.
    /// DeniedTools wins when a tool appears in both lists.
    /// </summary>
    public IReadOnlyList<string>? DeniedTools { get; set; }

    /// <summary>
    /// Autonomy level override for all tools from this plugin. Emits permission
    /// rules into the 3-phase resolver via <c>PluginPermissionRuleProvider</c>.
    /// Valid values: "Restricted", "Supervised", "Autonomous". Null means no override.
    /// Parsed to <c>Domain.AI.Governance.AutonomyLevel</c> at the Application layer.
    /// </summary>
    public string? AutonomyLevel { get; set; }
}
