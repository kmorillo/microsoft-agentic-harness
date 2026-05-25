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
