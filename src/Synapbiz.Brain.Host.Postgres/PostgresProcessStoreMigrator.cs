using System.Globalization;
using System.Reflection;
using Npgsql;
using NpgsqlTypes;

namespace Synapbiz.Brain.Host.Postgres;

/// <summary>
/// Applies the numbered SQL scripts embedded under <c>Migrations/</c>, each once and in order, inside one transaction guarded by
/// a transaction-scoped advisory lock. The scripts name their tables without a schema: the search path of the transaction is set to
/// the configured schema first.
/// </summary>
internal sealed class PostgresProcessStoreMigrator : IProcessStoreMigrator, IDisposable
{
    private const string ResourceMarker = ".Migrations.";

    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;
    private readonly TimeProvider time;

    internal PostgresProcessStoreMigrator(PostgresProcessStoreOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!PostgresProcessStoreOptionsValidator.IsValidSchema(options.Schema))
        {
            throw new ArgumentException("The schema name is not valid.", nameof(options));
        }

        schema = options.Schema;
        this.time = time;
        dataSource = NpgsqlDataSource.Create(options.ConnectionString);
    }

    /// <inheritdoc />
    public void Dispose() => dataSource.Dispose();

    /// <inheritdoc />
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        var scripts = ReadScripts();
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await RunAsync(connection, transaction, "select pg_advisory_xact_lock(hashtextextended(@key, 0))", [Text("key", "brain.process.migrator:" + schema)], cancellationToken).ConfigureAwait(false);
                await RunAsync(connection, transaction, "create schema if not exists \"" + schema + "\"", [], cancellationToken).ConfigureAwait(false);
                await RunAsync(connection, transaction, "select set_config('search_path', quote_ident(@schema), true)", [Text("schema", schema)], cancellationToken).ConfigureAwait(false);
                await RunAsync(connection, transaction, "create table if not exists process_schema_version (version int primary key, applied_at timestamptz not null)", [], cancellationToken).ConfigureAwait(false);
                var applied = await AppliedAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
                foreach (var script in scripts.Where(script => !applied.Contains(script.Version)))
                {
                    foreach (var statement in Statements(script.Text))
                    {
                        await RunAsync(connection, transaction, statement, [], cancellationToken).ConfigureAwait(false);
                    }

                    await RunAsync(
                        connection,
                        transaction,
                        "insert into process_schema_version (version, applied_at) values (@v, @at)",
                        [new NpgsqlParameter("v", NpgsqlDbType.Integer) { Value = script.Version }, new NpgsqlParameter("at", NpgsqlDbType.TimestampTz) { Value = time.GetUtcNow().ToUniversalTime() }],
                        cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static NpgsqlParameter Text(string name, string value) => new(name, NpgsqlDbType.Text) { Value = value };

    private static async Task RunAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, NpgsqlParameter[] parameters, CancellationToken cancellationToken)
    {
        var batch = new NpgsqlBatch(connection, transaction);
        await using (batch.ConfigureAwait(false))
        {
            var command = new NpgsqlBatchCommand(sql);
            command.Parameters.AddRange(parameters);
            batch.BatchCommands.Add(command);
            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<HashSet<int>> AppliedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        var batch = new NpgsqlBatch(connection, transaction);
        await using (batch.ConfigureAwait(false))
        {
            batch.BatchCommands.Add(new NpgsqlBatchCommand("select version from process_schema_version"));
            var reader = await batch.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                var versions = new HashSet<int>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    versions.Add(reader.GetInt32(0));
                }

                return versions;
            }
        }
    }

    /// <summary>
    /// Splits a script into statements: whole-line comments are dropped and the text is cut at each semicolon. A script is plain DDL, so
    /// it never holds a semicolon inside a literal or a function body (the migration tests would fail on one).
    /// </summary>
    internal static List<string> Statements(string script)
    {
        var lines = script.Split('\n').Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal));
        return [.. string.Join('\n', lines).Split(';').Select(statement => statement.Trim()).Where(statement => statement.Length > 0)];
    }

    private static List<(int Version, string Text)> ReadScripts()
    {
        var assembly = typeof(PostgresProcessStoreMigrator).Assembly;
        var scripts = new List<(int Version, string Text)>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var marker = name.IndexOf(ResourceMarker, StringComparison.Ordinal);
            if (marker < 0 || !name.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            var file = name[(marker + ResourceMarker.Length)..];
            var digits = file[..file.IndexOf('_', StringComparison.Ordinal)];
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The migration script " + file + " cannot be read.");
            using var reader = new StreamReader(stream);
            scripts.Add((int.Parse(digits, CultureInfo.InvariantCulture), reader.ReadToEnd()));
        }

        return [.. scripts.OrderBy(script => script.Version)];
    }
}
