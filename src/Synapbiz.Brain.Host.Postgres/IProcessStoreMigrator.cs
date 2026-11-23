namespace Synapbiz.Brain.Host.Postgres;

/// <summary>Creates or upgrades the schema of the host. The consumer calls it at start-up; the worker never does.</summary>
public interface IProcessStoreMigrator
{
    /// <summary>
    /// Creates the schema and the tables when they are missing and applies the numbered scripts that have not been applied, in order,
    /// under a transaction-scoped advisory lock. Running it twice changes nothing; two concurrent runs both succeed.
    /// </summary>
    /// <param name="cancellationToken">Cancels the migration; the transaction rolls back.</param>
    /// <returns>A task that completes when the schema is up to date.</returns>
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
