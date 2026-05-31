using System.Text.Json;
using Application.AI.Common.Interfaces;
using Application.AI.Common.Interfaces.Planner;
using Application.AI.Common.Prompts.Exceptions;
using Application.AI.Common.Prompts.Interfaces;
using Domain.AI.Planner;
using Domain.AI.Prompts;
using Domain.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.Planner;

/// <summary>
/// Converts a natural-language task description into a validated <see cref="PlanGraph"/> DAG
/// by sending structured generation requests to an LLM and mapping the JSON output to domain types.
/// The system prompt body is resolved from the versioned <see cref="IPromptRegistry"/>
/// (<c>plan-generator-system</c>) and rendered with <see cref="IPromptRenderer"/>; usage is
/// stamped via <see cref="IPromptUsageRecorder"/> for trace replay.
/// </summary>
public sealed class LlmPlanGeneratorService : IPlanGenerator
{
    private const string PromptName = "plan-generator-system";
    private const string MetricKey = "plan_generation";

    private readonly IChatClientFactory _chatClientFactory;
    private readonly IPromptRegistry _promptRegistry;
    private readonly IPromptRenderer _promptRenderer;
    private readonly IPromptUsageRecorder _usageRecorder;
    private readonly IPlanValidator _validator;
    private readonly IOptionsMonitor<PlannerOptions> _options;
    private readonly ILogger<LlmPlanGeneratorService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>Initializes a new instance.</summary>
    public LlmPlanGeneratorService(
        IChatClientFactory chatClientFactory,
        IPromptRegistry promptRegistry,
        IPromptRenderer promptRenderer,
        IPromptUsageRecorder usageRecorder,
        IPlanValidator validator,
        IOptionsMonitor<PlannerOptions> options,
        ILogger<LlmPlanGeneratorService> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClientFactory);
        ArgumentNullException.ThrowIfNull(promptRegistry);
        ArgumentNullException.ThrowIfNull(promptRenderer);
        ArgumentNullException.ThrowIfNull(usageRecorder);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _chatClientFactory = chatClientFactory;
        _promptRegistry = promptRegistry;
        _promptRenderer = promptRenderer;
        _usageRecorder = usageRecorder;
        _validator = validator;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<PlanGraph>> GenerateAsync(
        string taskDescription,
        PlanGenerationConstraints? constraints = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(taskDescription);

        var opts = _options.CurrentValue;

        PromptDescriptor descriptor;
        try
        {
            descriptor = await _promptRegistry.GetLatestAsync(PromptName, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or PromptRegistryUnavailableException)
        {
            _logger.LogError(ex, "Could not resolve plan-generator system prompt '{Prompt}'", PromptName);
            return Result<PlanGraph>.Fail($"Plan-generator prompt '{PromptName}' is unavailable: {ex.Message}");
        }

        try
        {
            var chatClient = await _chatClientFactory.GetChatClientAsync(
                opts.ClientType,
                opts.GenerationModel,
                ct);

            var rendered = await _promptRenderer.RenderAsync(
                descriptor,
                new Dictionary<string, object?> { ["constraints_block"] = BuildConstraintsBlock(constraints) },
                ct).ConfigureAwait(false);

            await _usageRecorder.RecordAsync(
                descriptor,
                new PromptUsageContext { MetricKey = MetricKey },
                ct).ConfigureAwait(false);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, rendered.Body),
                new(ChatRole.User, BuildUserPrompt(taskDescription, constraints))
            };
            var chatOptions = new ChatOptions
            {
                Temperature = (float)opts.GenerationTemperature,
                MaxOutputTokens = opts.GenerationMaxTokens
            };

            var response = await chatClient.GetResponseAsync(messages, chatOptions, ct);
            var responseText = SanitizeJsonResponse(response.Text ?? string.Empty);

            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogWarning("LLM returned empty response for plan generation");
                return Result<PlanGraph>.Fail("LLM returned an empty response.");
            }

            LlmPlanOutput? planOutput;
            try
            {
                planOutput = JsonSerializer.Deserialize<LlmPlanOutput>(responseText, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize LLM plan output as JSON");
                return Result<PlanGraph>.Fail($"LLM did not return valid JSON: {ex.Message}");
            }

            if (planOutput is null || planOutput.Steps.Count == 0)
                return Result<PlanGraph>.Fail("LLM returned an empty or null plan.");

            PlanGraph graph;
            try
            {
                graph = LlmPlanOutputMapper.MapToPlanGraph(planOutput);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to map LLM output to PlanGraph");
                return Result<PlanGraph>.Fail($"Failed to parse plan: {ex.Message}");
            }

            var validationResult = await _validator.ValidateAsync(graph, ct);
            if (!validationResult.IsSuccess)
                return Result<PlanGraph>.Fail(validationResult.Errors.ToArray());

            if (validationResult.Value is null)
                return Result<PlanGraph>.Fail("Validator returned null result.");

            if (!validationResult.Value.IsValid)
                return Result<PlanGraph>.Fail(validationResult.Value.Errors.ToArray());

            _logger.LogInformation(
                "Generated plan '{PlanName}' with {StepCount} steps and {EdgeCount} edges",
                graph.Name, graph.Steps.Count, graph.Edges.Count);

            return Result<PlanGraph>.Success(graph);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plan generation failed");
            return Result<PlanGraph>.Fail($"Plan generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the constraints block injected into the system prompt template's
    /// <c>{{ constraints_block }}</c> variable. Empty when no constraints are supplied.
    /// </summary>
    /// <remarks>
    /// Conditionals are excluded from the registry's Scriban renderer (variable-only by
    /// design), so the constraint composition stays in C# and surfaces in the template
    /// as one already-formatted block.
    /// </remarks>
    private static string BuildConstraintsBlock(PlanGenerationConstraints? constraints)
    {
        if (constraints is null) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Constraints:");
        if (constraints.MaxSteps.HasValue)
            sb.AppendLine($"- Maximum steps: {constraints.MaxSteps.Value}");
        if (constraints.AllowedStepTypes is { Count: > 0 })
            sb.AppendLine($"- Allowed step types: {string.Join(", ", constraints.AllowedStepTypes)}");
        if (constraints.MaxSubPlanDepth.HasValue)
            sb.AppendLine($"- Maximum sub-plan depth: {constraints.MaxSubPlanDepth.Value}");
        if (constraints.MaxTotalTimeout.HasValue)
            sb.AppendLine($"- Maximum total timeout: {constraints.MaxTotalTimeout.Value.TotalMinutes} minutes");
        return sb.ToString();
    }

    private static string BuildUserPrompt(string taskDescription, PlanGenerationConstraints? constraints)
    {
        if (!string.IsNullOrWhiteSpace(constraints?.AdditionalContext))
            return $"Task: {taskDescription}\n\nAdditional context: {constraints.AdditionalContext}";

        return $"Task: {taskDescription}";
    }

    /// <summary>
    /// Strips markdown code fences that LLMs frequently add despite instructions not to.
    /// </summary>
    private static string SanitizeJsonResponse(string response)
        => Application.AI.Common.Json.LlmJsonResponseParser.StripFences(response);
}
