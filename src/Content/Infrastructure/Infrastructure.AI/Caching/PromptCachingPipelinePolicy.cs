using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;

namespace Infrastructure.AI.Caching;

/// <summary>
/// A <see cref="PipelinePolicy"/> for the OpenAI SDK client that stamps an Anthropic prompt-cache
/// breakpoint onto the system message of every outgoing chat-completions request, enabling
/// provider-side prompt caching through an OpenAI-compatible gateway (e.g. OpenRouter).
/// </summary>
/// <remarks>
/// <para>
/// Registered only on the OpenAI client path and only when
/// <c>AppConfig:AI:AgentFramework:EnablePromptCaching</c> is true. It targets
/// <c>/chat/completions</c> requests exclusively — embeddings, model listing, and any other
/// operation pass through untouched. The body rewrite itself is delegated to the pure, defensive
/// <see cref="PromptCacheInjector"/>, which returns the body unchanged if anything is unexpected.
/// </para>
/// <para>
/// Runs at <see cref="PipelinePosition.PerCall"/> — once per logical request, after the body has
/// been serialized and before transport — so it operates on the final JSON and is not re-run per
/// retry.
/// </para>
/// </remarks>
public sealed class PromptCachingPipelinePolicy : PipelinePolicy
{
    private const string ChatCompletionsPath = "/chat/completions";

    /// <inheritdoc />
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        TryInjectCacheControl(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    /// <inheritdoc />
    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        TryInjectCacheControl(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private static void TryInjectCacheControl(PipelineMessage message)
    {
        var request = message.Request;
        if (request?.Uri is null || request.Content is null)
            return;

        if (!request.Uri.AbsolutePath.EndsWith(ChatCompletionsPath, StringComparison.OrdinalIgnoreCase))
            return;

        string body;
        using (var buffer = new MemoryStream())
        {
            request.Content.WriteTo(buffer, message.CancellationToken);
            body = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }

        var rewritten = PromptCacheInjector.InjectSystemCacheControl(body);
        if (!string.Equals(rewritten, body, StringComparison.Ordinal))
            request.Content = BinaryContent.Create(BinaryData.FromString(rewritten));
    }
}
