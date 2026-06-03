namespace Application.AI.Common.Exceptions;

/// <summary>
/// Thrown by <see cref="Interfaces.IChatClientFactory"/> when the active AI provider cannot create a
/// chat client because required configuration — endpoint, API key, or a registered SDK client — is
/// missing.
/// </summary>
/// <remarks>
/// The message is actionable and secret-free: it names the settings to supply. Callers may surface it
/// to users in Development to explain why an agent turn failed; production paths should keep responses
/// generic to avoid leaking configuration detail.
/// Derives from <see cref="InvalidOperationException"/> because a not-configured provider is an invalid
/// operation — callers that already catch <see cref="InvalidOperationException"/> continue to work,
/// while callers that need the classification can match this more specific type.
/// </remarks>
public sealed class AiProviderNotConfiguredException : InvalidOperationException
{
    /// <summary>Initializes a new instance with an actionable, secret-free message.</summary>
    public AiProviderNotConfiguredException(string message) : base(message) { }
}
