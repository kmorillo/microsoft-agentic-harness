using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.AI.Evaluation;

namespace Presentation.EvalRunner;

/// <summary>
/// Minimal HTTP client that posts a completed <see cref="EvalRunReport"/> to the
/// dashboard's ingest endpoint (<c>POST /api/evals/ingest</c>). Designed to be
/// fire-and-forget: failures are surfaced on stderr but never alter the CLI's
/// exit code — the run itself succeeded, ingest is a follow-on side-effect.
/// </summary>
/// <remarks>
/// <para>
/// Lives in the CLI host (not Application/Infrastructure) because it's a
/// presentation-layer concern: how the CLI surfaces its run report to a
/// neighbouring web host. Other CLIs / runners can copy this pattern with their
/// own auth and shape.
/// </para>
/// </remarks>
internal static class EvalRunIngestClient
{
    /// <summary>
    /// JSON shape matches <c>IngestEvalRunCommand { Report }</c> exactly so the
    /// controller's MediatR dispatch deserialises it without manual mapping.
    /// </summary>
    private sealed class IngestRequestPayload
    {
        public required EvalRunReport Report { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// POSTs the supplied report to the supplied URL. Writes failure messages to
    /// <see cref="Console.Error"/> and returns <c>false</c> on any non-success
    /// outcome (network error, non-2xx response, cancellation). Never throws —
    /// the CLI's exit code is governed by the run's own verdict, not by ingest.
    /// </summary>
    /// <param name="ingestUrl">Absolute http/https URL of the ingest endpoint.</param>
    /// <param name="report">The completed run report to persist.</param>
    /// <param name="bearerToken">Optional bearer token, sourced from <c>EVAL_INGEST_TOKEN</c>.</param>
    /// <param name="timeout">HTTP timeout. Defaults to 30 seconds.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public static async Task<bool> PostReportAsync(
        Uri ingestUrl,
        EvalRunReport report,
        string? bearerToken,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ingestUrl);
        ArgumentNullException.ThrowIfNull(report);

        using var client = new HttpClient { Timeout = timeout };
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        try
        {
            var payload = new IngestRequestPayload { Report = report };
            using var response = await client.PostAsJsonAsync(
                ingestUrl, payload, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Console.Error.WriteLine(
                    $"Ingest POST {ingestUrl} returned {(int)response.StatusCode} {response.ReasonPhrase}. Body: {Truncate(body, 256)}");
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Re-throw genuine cancellation so the caller (Main) can return the standard
            // 130 SIGINT exit code instead of swallowing it as a "soft" ingest failure.
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ingest POST to {ingestUrl} failed: {ex.Message}");
            return false;
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
