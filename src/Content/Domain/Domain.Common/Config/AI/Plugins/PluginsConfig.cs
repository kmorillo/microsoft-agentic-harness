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
