using Application.AI.Common.Interfaces.Iac;
using Domain.Common;
using Domain.Common.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Tools.Iac;

/// <summary>
/// Shared backend-selection helper for the three IaC tools. Reads the optional
/// <c>backend</c> parameter, falls back to the first <c>EnabledBackends</c> entry
/// in <c>AppConfig.AI.Iac</c>, and resolves the matching keyed
/// <see cref="IIacGenerator"/>.
/// </summary>
public static class IacToolBackendResolver
{
    /// <summary>
    /// Resolves the <see cref="IIacGenerator"/> a tool call targets.
    /// </summary>
    /// <param name="services">Service provider for keyed-DI resolution.</param>
    /// <param name="config">Application configuration monitor — supplies the default backend.</param>
    /// <param name="parameters">The tool parameters; an optional <c>backend</c> entry overrides the default.</param>
    /// <returns><see cref="Result{T}.Success"/> with the resolved generator, or <see cref="Result{T}.Fail(string[])"/> with a stable code.</returns>
    public static Result<IIacGenerator> Resolve(
        IServiceProvider services,
        IOptionsMonitor<AppConfig> config,
        IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(parameters);

        var backendKey = ReadBackendKey(parameters, config);
        if (string.IsNullOrWhiteSpace(backendKey))
        {
            return Result<IIacGenerator>.Fail("iac.no_backend_configured");
        }

        var generator = services.GetKeyedService<IIacGenerator>(backendKey);
        return generator is null
            ? Result<IIacGenerator>.Fail($"iac.backend_not_registered: '{backendKey}'")
            : Result<IIacGenerator>.Success(generator);
    }

    private static string ReadBackendKey(IReadOnlyDictionary<string, object?> parameters, IOptionsMonitor<AppConfig> config)
    {
        if (parameters.TryGetValue("backend", out var value) && value is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s.Trim().ToLowerInvariant();
        }

        var enabled = config.CurrentValue.AI.Iac.EnabledBackends;
        return enabled is { Count: > 0 } ? enabled[0].Trim().ToLowerInvariant() : string.Empty;
    }
}
