using Domain.AI.Permissions;
using FluentAssertions;
using Xunit;

namespace Domain.AI.Tests.Permissions;

public sealed class PermissionRuleSourceTests
{
    [Fact]
    public void PluginDeclaration_EnumValue_Exists()
    {
        var source = PermissionRuleSource.PluginDeclaration;

        source.Should().BeDefined();
        source.ToString().Should().Be("PluginDeclaration");
    }
}
