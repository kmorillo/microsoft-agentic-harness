namespace Application.AI.Common.Prompts.Exceptions;

/// <summary>
/// Signals that the prompt registry could not satisfy a request because of a
/// backend-level failure (transient IO, permission flip, malformed body,
/// network unreachable). Distinct from <see cref="KeyNotFoundException"/>, which
/// means the prompt name is genuinely unknown to the registry.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>IPromptRegistry</c> implementation MUST wrap its backend-specific
/// transient exceptions in this type so consumers can write one catch clause
/// regardless of whether the backend is file-system, Redis, HTTP, or S3. This
/// keeps caller-side soft-fail logic from coupling to file-system exception
/// surfaces.
/// </para>
/// <para>
/// Consumers that want to surface the failure to a user, retry, or degrade
/// should catch <see cref="PromptRegistryUnavailableException"/>. Consumers that
/// just want "the prompt didn't resolve" should catch this together with
/// <see cref="KeyNotFoundException"/>.
/// </para>
/// </remarks>
public sealed class PromptRegistryUnavailableException : Exception
{
    /// <summary>Registry name that was being resolved when the failure occurred.</summary>
    public string PromptName { get; }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="promptName">The registry name under resolution.</param>
    /// <param name="message">Diagnostic message.</param>
    /// <param name="innerException">The backend-specific exception being wrapped.</param>
    public PromptRegistryUnavailableException(string promptName, string message, Exception innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
        ArgumentNullException.ThrowIfNull(innerException);
        PromptName = promptName;
    }
}
