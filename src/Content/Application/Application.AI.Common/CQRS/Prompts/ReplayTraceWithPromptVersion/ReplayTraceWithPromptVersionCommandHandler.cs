using Application.AI.Common.Interfaces;
using Application.AI.Common.Prompts.Exceptions;
using Application.AI.Common.Prompts.Interfaces;
using Application.AI.Common.Prompts.Models;
using Domain.AI.Prompts;
using Domain.Common;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Application.AI.Common.CQRS.Prompts.ReplayTraceWithPromptVersion;

/// <summary>
/// Handles <see cref="ReplayTraceWithPromptVersionCommand"/> by locating the original
/// prompt-usage row, resolving both descriptors from the registry, rendering both with
/// the caller's variables, and replaying the LLM call against the target version at
/// temperature zero. Returns a <see cref="PromptReplayResult"/> wrapped in
/// <see cref="Result{T}"/>.
/// </summary>
/// <remarks>
/// Defaults to 4096 max output tokens when the command leaves <see cref="ReplayTraceWithPromptVersionCommand.MaxOutputTokens"/> null.
/// </remarks>
public sealed class ReplayTraceWithPromptVersionCommandHandler
    : IRequestHandler<ReplayTraceWithPromptVersionCommand, Result<PromptReplayResult>>
{
    /// <summary>Temperature locked to zero for trace replays per the Sub-phase 5.3 plan.</summary>
    public const float ReplayTemperature = 0.0f;

    /// <summary>Default replay max output tokens when caller leaves it null.</summary>
    public const int DefaultMaxOutputTokens = 4096;

    private readonly IPromptUsageStore _usageStore;
    private readonly IPromptRegistry _promptRegistry;
    private readonly IPromptRenderer _promptRenderer;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<ReplayTraceWithPromptVersionCommandHandler> _logger;

    /// <summary>Initializes a new instance.</summary>
    public ReplayTraceWithPromptVersionCommandHandler(
        IPromptUsageStore usageStore,
        IPromptRegistry promptRegistry,
        IPromptRenderer promptRenderer,
        IChatClientFactory chatClientFactory,
        ILogger<ReplayTraceWithPromptVersionCommandHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(usageStore);
        ArgumentNullException.ThrowIfNull(promptRegistry);
        ArgumentNullException.ThrowIfNull(promptRenderer);
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _usageStore = usageStore;
        _promptRegistry = promptRegistry;
        _promptRenderer = promptRenderer;
        _chatClientFactory = chatClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<PromptReplayResult>> Handle(
        ReplayTraceWithPromptVersionCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Locate the original (TraceId, PromptName) usage row so we know which version
        // was historically in play. There may be more than one row for the same trace if
        // the case re-used the prompt; replay against the most recent (highest version)
        // so a re-rendered metric reflects the most-relevant prior body.
        IReadOnlyList<PromptUsageRecord> traceRows;
        try
        {
            traceRows = await _usageStore.QueryByTraceIdAsync(request.TraceId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query prompt-usage store for trace {TraceId}.", request.TraceId);
            return Result<PromptReplayResult>.Fail($"Failed to query usage store: {ex.Message}");
        }

        var originalRecord = traceRows
            .Where(r => string.Equals(r.Descriptor.Name, request.PromptName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Descriptor.Version)
            .ThenByDescending(r => r.RecordedAtUtc)
            .FirstOrDefault();

        if (originalRecord is null)
        {
            return Result<PromptReplayResult>.NotFound(
                $"No prompt-usage row for trace '{request.TraceId}' on prompt '{request.PromptName}'.");
        }

        // Resolve the original descriptor from the registry. We can't recover the body
        // from the persisted row (it stores only the hash); the registry is the source of
        // truth. If the file has since been deleted, the diff baseline is unrecoverable —
        // surface that explicitly rather than guessing.
        PromptDescriptor originalDescriptor;
        try
        {
            originalDescriptor = await _promptRegistry
                .GetAsync(request.PromptName, originalRecord.Descriptor.Version, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Result<PromptReplayResult>.Fail(
                $"Original prompt '{request.PromptName}' version {originalRecord.Descriptor.Version} " +
                "is no longer present in the registry — replay diff baseline cannot be recovered.");
        }
        catch (PromptRegistryUnavailableException ex)
        {
            _logger.LogWarning(ex,
                "Registry unavailable while resolving original prompt '{Prompt}' version {Version}.",
                request.PromptName, originalRecord.Descriptor.Version);
            return Result<PromptReplayResult>.Fail($"Prompt registry unavailable: {ex.Message}");
        }

        // Resolve the target version. NotFound on the target is a user-input miss, not
        // an unrecoverable state — distinct error.
        PromptDescriptor targetDescriptor;
        try
        {
            targetDescriptor = await _promptRegistry
                .GetAsync(request.PromptName, request.TargetVersion, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return Result<PromptReplayResult>.NotFound(
                $"Target prompt '{request.PromptName}' version {request.TargetVersion} not found in the registry.");
        }
        catch (PromptRegistryUnavailableException ex)
        {
            _logger.LogWarning(ex,
                "Registry unavailable while resolving target prompt '{Prompt}' version {Version}.",
                request.PromptName, request.TargetVersion);
            return Result<PromptReplayResult>.Fail($"Prompt registry unavailable: {ex.Message}");
        }

        // Render both with the caller-supplied variables. Renderer failures (Scriban
        // parse errors, missing required vars) surface as Fail rather than throw.
        RenderedPrompt originalRendered, targetRendered;
        try
        {
            originalRendered = await _promptRenderer.RenderAsync(originalDescriptor, request.Variables, cancellationToken).ConfigureAwait(false);
            targetRendered = await _promptRenderer.RenderAsync(targetDescriptor, request.Variables, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to render prompt(s) for replay of trace {TraceId}.", request.TraceId);
            return Result<PromptReplayResult>.Fail($"Failed to render prompts: {ex.Message}");
        }

        // Replay the LLM call. Temperature is forced to 0 so the only delta is the
        // prompt body change; any non-zero would inject sampling noise into the diff.
        string targetOutput;
        try
        {
            var client = await _chatClientFactory
                .GetChatClientAsync(request.ChatClientType, request.Deployment, cancellationToken)
                .ConfigureAwait(false);

            var chatOptions = new ChatOptions
            {
                Temperature = ReplayTemperature,
                MaxOutputTokens = request.MaxOutputTokens ?? DefaultMaxOutputTokens,
            };

            var messages = new List<ChatMessage> { new(ChatRole.User, targetRendered.Body) };
            var response = await client.GetResponseAsync(messages, chatOptions, cancellationToken).ConfigureAwait(false);
            targetOutput = response.Text ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Replay LLM call failed for trace {TraceId}.", request.TraceId);
            return Result<PromptReplayResult>.Fail($"Replay LLM call failed: {ex.Message}");
        }

        var result = new PromptReplayResult
        {
            TraceId = request.TraceId,
            PromptName = request.PromptName,
            OriginalDescriptor = originalDescriptor,
            TargetDescriptor = targetDescriptor,
            OriginalRenderedPrompt = originalRendered,
            TargetRenderedPrompt = targetRendered,
            TargetOutput = targetOutput,
        };

        _logger.LogInformation(
            "Replayed trace {TraceId} on prompt {Prompt}: {Original} → {Target} (hash changed: {Changed}).",
            request.TraceId,
            request.PromptName,
            originalDescriptor.Version,
            targetDescriptor.Version,
            result.ContentHashChanged);

        return Result<PromptReplayResult>.Success(result);
    }
}
