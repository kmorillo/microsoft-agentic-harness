namespace Domain.AI.Iac;

/// <summary>
/// Parses the configured <c>BlockingSeverity</c> string into the shared
/// <see cref="IacScanSeverity"/> scale, and decides scan pass/fail against it.
/// Lives in the Domain so both the Application-layer config validator and the
/// Infrastructure-layer generators/startup validator agree on what counts as a
/// valid severity and what blocks a proposal.
/// </summary>
public static class IacScanSeverityParser
{
    /// <summary>
    /// Parses a blocking-severity string (case-insensitive) into an
    /// <see cref="IacScanSeverity"/>.
    /// </summary>
    /// <param name="value">The configured severity, e.g. <c>"High"</c>.</param>
    /// <param name="severity">The parsed severity when recognised.</param>
    /// <returns><see langword="true"/> when the value maps to a known severity; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? value, out IacScanSeverity severity)
        => Enum.TryParse(value?.Trim(), ignoreCase: true, out severity)
           && Enum.IsDefined(severity);

    /// <summary>
    /// Decides whether a scan passes the gate: it passes when no finding is at or
    /// above the configured blocking severity.
    /// </summary>
    /// <param name="findings">The normalised findings across the scanners that ran.</param>
    /// <param name="blocking">The minimum severity that blocks a proposal.</param>
    /// <returns><see langword="true"/> when no finding meets or exceeds <paramref name="blocking"/>.</returns>
    public static bool Passes(IEnumerable<IacScanFinding> findings, IacScanSeverity blocking)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return !findings.Any(f => f.Severity >= blocking);
    }
}
