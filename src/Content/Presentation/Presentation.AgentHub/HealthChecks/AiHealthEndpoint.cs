using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Presentation.AgentHub.HealthChecks;

/// <summary>
/// Self-contained JSON writer for the <c>/health/ai</c> endpoint. Emits each check's status,
/// description, and data (including the missing configuration keys) so the response is actionable
/// without pulling in the HealthChecks UI client package.
/// </summary>
public static class AiHealthEndpoint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Writes the aggregated health report for the AI-tagged checks as JSON.</summary>
    public static Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                data = entry.Value.Data,
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}
