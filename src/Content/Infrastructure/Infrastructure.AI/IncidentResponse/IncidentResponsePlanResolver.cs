using Application.AI.Common.Interfaces.IncidentResponse;
using Domain.Common.Config;
using Domain.Common.Config.AI.IncidentResponse;
using Microsoft.Extensions.Options;

namespace Infrastructure.AI.IncidentResponse;

/// <summary>
/// Default <see cref="IIncidentResponsePlanResolver"/> backed by
/// <see cref="IOptionsMonitor{TOptions}"/> so hot-reloaded config takes effect
/// on the next resolve.
/// </summary>
/// <remarks>
/// <para>
/// Lookup walks
/// <see cref="IncidentResponsePlanConfig.Plans"/> case-insensitively. If no
/// plan matches and <see cref="IncidentResponsePlanConfig.DefaultPlanName"/>
/// is set, the resolver returns the plan whose
/// <see cref="IncidentResponsePlan.Name"/> matches the default; if the
/// default name does not match any plan, the resolver returns <c>null</c> and
/// the misconfigured-default case is surfaced by the boot validator instead.
/// </para>
/// </remarks>
public sealed class IncidentResponsePlanResolver : IIncidentResponsePlanResolver
{
    private readonly IOptionsMonitor<AppConfig> _config;

    /// <summary>
    /// Initializes a new <see cref="IncidentResponsePlanResolver"/>.
    /// </summary>
    /// <param name="config">The application configuration monitor.</param>
    public IncidentResponsePlanResolver(IOptionsMonitor<AppConfig> config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
    }

    /// <inheritdoc />
    public IncidentResponsePlan? ResolveFor(string? incidentType)
    {
        var cfg = _config.CurrentValue.AI.IncidentResponse;

        if (!string.IsNullOrWhiteSpace(incidentType))
        {
            for (var i = 0; i < cfg.Plans.Count; i++)
            {
                var plan = cfg.Plans[i];
                if (string.Equals(plan.IncidentType, incidentType, StringComparison.OrdinalIgnoreCase))
                {
                    return plan;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(cfg.DefaultPlanName))
        {
            for (var i = 0; i < cfg.Plans.Count; i++)
            {
                var plan = cfg.Plans[i];
                if (string.Equals(plan.Name, cfg.DefaultPlanName, StringComparison.OrdinalIgnoreCase))
                {
                    return plan;
                }
            }
        }

        return null;
    }
}
