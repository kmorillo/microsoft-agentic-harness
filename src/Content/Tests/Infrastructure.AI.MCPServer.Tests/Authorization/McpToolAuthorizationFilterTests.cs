using System.Security.Claims;
using FluentAssertions;
using Infrastructure.AI.MCPServer.Authorization;
using Xunit;

namespace Infrastructure.AI.MCPServer.Tests.Authorization;

/// <summary>
/// Tests the baseline per-tool-call authorization gate. Encodes the rule that
/// matters: when authentication is configured, an inbound tool call without an
/// authenticated principal must be refused at the dispatch layer — independent of
/// the endpoint's transport-level guard.
/// </summary>
public sealed class McpToolAuthorizationFilterTests
{
    [Fact]
    public void Evaluate_AuthRequired_NoPrincipal_Denies()
    {
        var result = McpToolAuthorizationFilter.Evaluate(authenticationRequired: true, user: null);

        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AuthRequired_UnauthenticatedPrincipal_Denies()
    {
        // A ClaimsIdentity with no authentication type reports IsAuthenticated == false.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        var result = McpToolAuthorizationFilter.Evaluate(authenticationRequired: true, user: anonymous);

        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_AuthRequired_AuthenticatedPrincipal_Allows()
    {
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "Bearer"));

        var result = McpToolAuthorizationFilter.Evaluate(authenticationRequired: true, user: authenticated);

        result.Should().BeNull();
    }

    [Fact]
    public void Evaluate_AuthNotConfigured_NoPrincipal_Allows()
    {
        // Development posture: no auth configured, so the gate is inert and matches
        // the server's existing unauthenticated-development behavior.
        var result = McpToolAuthorizationFilter.Evaluate(authenticationRequired: false, user: null);

        result.Should().BeNull();
    }
}
