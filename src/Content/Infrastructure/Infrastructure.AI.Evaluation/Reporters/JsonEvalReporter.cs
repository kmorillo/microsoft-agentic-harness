using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.AI.Common.Evaluation.Interfaces;
using Domain.AI.Evaluation;

namespace Infrastructure.AI.Evaluation.Reporters;

/// <summary>
/// Serializes an <see cref="EvalRunReport"/> as indented JSON with snake_case property
/// names. Suitable for dashboard ingestion and for golden-file regression tests.
/// </summary>
public sealed class JsonEvalReporter : IEvalReporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
        }
    };

    /// <inheritdoc />
    public string FormatKey => "json";

    /// <inheritdoc />
    public async Task WriteAsync(
        EvalRunReport report,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);

        await JsonSerializer.SerializeAsync(output, report, SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
