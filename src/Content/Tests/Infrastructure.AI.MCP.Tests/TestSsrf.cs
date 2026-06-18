using Domain.Common.Config;
using Infrastructure.AI.Egress;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.MCP.Tests;

/// <summary>
/// Builds a real <see cref="AntiSsrfHandlerFactory"/> for tests that construct an
/// <see cref="Infrastructure.AI.MCP.Services.McpConnectionManager"/>. Non-HTTP transport
/// tests (stdio, dispose, error paths) never invoke the handler; the SSRF-blocking
/// behavior itself is proven end-to-end in <c>Infrastructure.AI.Tests</c>'
/// <c>McpSsrfProtectionTests</c>.
/// </summary>
internal static class TestSsrf
{
    /// <summary>Creates an <see cref="AntiSsrfHandlerFactory"/> bound to a default config.</summary>
    public static AntiSsrfHandlerFactory HandlerFactory()
        => new(new StaticOptionsMonitor(new AppConfig()));

    private sealed class StaticOptionsMonitor(AppConfig value) : IOptionsMonitor<AppConfig>
    {
        public AppConfig CurrentValue { get; } = value;
        public AppConfig Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AppConfig, string?> listener) => null;
    }
}
