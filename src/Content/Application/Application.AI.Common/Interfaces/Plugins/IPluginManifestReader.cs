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
