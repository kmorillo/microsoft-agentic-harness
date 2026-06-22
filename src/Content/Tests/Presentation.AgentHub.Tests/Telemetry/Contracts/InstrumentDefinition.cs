namespace Presentation.AgentHub.Tests.Telemetry.Contracts;

public enum InstrumentType
{
    Counter,
    UpDownCounter,
    Histogram,
    ObservableGauge
}

public sealed record InstrumentDefinition(
    string Name,
    InstrumentType Type,
    string? Unit = null)
{
    public string ToPrometheusName(string @namespace)
    {
        var core = Name.Replace('.', '_') + GetUnitSuffix();

        return Type == InstrumentType.Counter
            ? $"{@namespace}_{WithCounterTotal(core)}"
            : $"{@namespace}_{core}";
    }

    public IReadOnlyList<string> ToAllPrometheusNames(string @namespace)
    {
        var baseName = Name.Replace('.', '_');
        var unitSuffix = GetUnitSuffix();

        return Type switch
        {
            InstrumentType.Histogram => new[]
            {
                $"{@namespace}_{baseName}{unitSuffix}_sum",
                $"{@namespace}_{baseName}{unitSuffix}_count",
                $"{@namespace}_{baseName}{unitSuffix}_bucket"
            },
            InstrumentType.Counter => new[]
            {
                // Exporter appends _total but does not double it when already present.
                $"{@namespace}_{WithCounterTotal(baseName + unitSuffix)}"
            },
            InstrumentType.UpDownCounter => new[]
            {
                $"{@namespace}_{baseName}{unitSuffix}"
            },
            InstrumentType.ObservableGauge => new[]
            {
                $"{@namespace}_{baseName}{unitSuffix}"
            },
            _ => new[] { $"{@namespace}_{baseName}{unitSuffix}" }
        };
    }

    private string GetUnitSuffix()
    {
        if (string.IsNullOrEmpty(Unit)) return string.Empty;

        // Curly-brace units are annotations only — no suffix in Prometheus
        if (Unit.StartsWith('{') && Unit.EndsWith('}')) return string.Empty;

        // Bare units get appended by the Prometheus exporter
        return Unit switch
        {
            "ms" => "_milliseconds",
            "s" => "_seconds",
            "ratio" => "_ratio",
            _ => $"_{Unit}"
        };
    }

    /// <summary>
    /// Appends the Prometheus counter <c>_total</c> suffix, matching the OTel Prometheus
    /// exporter which does NOT double the suffix when the instrument name already ends in
    /// <c>_total</c> (e.g. <c>agent.orchestration.turns_total</c> → <c>..._turns_total</c>).
    /// </summary>
    private static string WithCounterTotal(string core) =>
        core.EndsWith("_total", StringComparison.Ordinal) ? core : $"{core}_total";
}
