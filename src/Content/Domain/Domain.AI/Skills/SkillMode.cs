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
