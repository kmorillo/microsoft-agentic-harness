using Application.AI.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Observability.Persistence;

/// <summary>
/// Persists and retrieves observability data from PostgreSQL using Npgsql.
/// Designed for append-heavy workloads with fire-and-forget semantics
/// for non-critical writes (audit, safety) to avoid blocking agent turns.
/// Read methods return empty collections on failure to maintain resilience.
/// </summary>
public sealed partial class PostgresObservabilityStore : IObservabilityStore, IDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresObservabilityStore> _logger;

    public PostgresObservabilityStore(
        string connectionString,
        ILogger<PostgresObservabilityStore> logger)
    {
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _logger = logger;
    }

    private async Task ExecuteNonQuerySafe(
        string sql, CancellationToken cancellationToken, params object[] parameters)
    {
        try
        {
            await using var cmd = _dataSource.CreateCommand(sql);
            for (var i = 0; i < parameters.Length; i++)
                cmd.Parameters.AddWithValue(parameters[i]);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Observability store write failed: {Sql}", sql[..Math.Min(sql.Length, 80)]);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _dataSource.Dispose();
}
