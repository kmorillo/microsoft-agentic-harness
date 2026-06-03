using Azure.AI.OpenAI;
using OpenAI;

namespace Infrastructure.AI.Helpers;

/// <summary>
/// Provides pre-configured client options for AI framework SDK clients.
/// Centralizes timeout, retry, telemetry, and user-agent settings.
/// </summary>
/// <remarks>
/// Lives in Infrastructure.AI because it depends on external SDK types
/// (<see cref="AzureOpenAIClientOptions"/>, <see cref="OpenAIClientOptions"/>).
/// Consumed by <see cref="Factories.ChatClientFactory"/> and DI registration.
/// </remarks>
public static class AgentFrameworkHelper
{
    private const string UserAgentValue = "AgenticHarness/1.0";
    private const int DefaultNetworkTimeoutSeconds = 300;

    /// <summary>
    /// Gets configured options for <see cref="AzureOpenAIClient"/>.
    /// </summary>
    /// <param name="networkTimeoutSeconds">Network timeout in seconds. Default: 300.</param>
    /// <returns>Configured <see cref="AzureOpenAIClientOptions"/>.</returns>
    public static AzureOpenAIClientOptions GetAzureOpenAIClientOptions(int networkTimeoutSeconds = DefaultNetworkTimeoutSeconds)
    {
        return new AzureOpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(networkTimeoutSeconds),
            UserAgentApplicationId = UserAgentValue
        };
    }

    /// <summary>
    /// Gets configured options for <see cref="OpenAIClient"/>.
    /// </summary>
    /// <param name="endpoint">
    /// Optional base endpoint for an OpenAI-compatible gateway (e.g. OpenRouter at
    /// <c>https://openrouter.ai/api/v1</c>). When null/blank/invalid, the SDK default
    /// (<c>https://api.openai.com/v1</c>) is used.
    /// </param>
    /// <param name="networkTimeoutSeconds">Network timeout in seconds. Default: 300.</param>
    /// <returns>Configured <see cref="OpenAIClientOptions"/>.</returns>
    public static OpenAIClientOptions GetOpenAIClientOptions(
        string? endpoint = null,
        int networkTimeoutSeconds = DefaultNetworkTimeoutSeconds)
    {
        var options = new OpenAIClientOptions
        {
            NetworkTimeout = TimeSpan.FromSeconds(networkTimeoutSeconds),
            UserAgentApplicationId = UserAgentValue
        };

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            // Fail loud on a malformed endpoint rather than silently falling back to the default
            // OpenAI endpoint — a dropped OpenRouter URL would otherwise send the OpenRouter key to
            // api.openai.com and 401 with no indication the endpoint was ignored. Leave blank for
            // the default OpenAI endpoint.
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            {
                throw new InvalidOperationException(
                    $"OpenAI-compatible endpoint '{endpoint}' is not a valid absolute URI. " +
                    "Use e.g. https://openrouter.ai/api/v1, or leave it blank for the default OpenAI endpoint.");
            }

            options.Endpoint = endpointUri;
        }

        return options;
    }
}
