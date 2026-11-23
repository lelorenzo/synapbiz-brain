using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Process.Hosting;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// One test's world: its own schema in the test database (dropped at the end), a fake clock, a catalog, fake ports, a capturing
/// logger and a service provider with the host registered. The hosted worker is never started: tests drive the pump.
/// </summary>
internal sealed class Rig : IAsyncDisposable
{
    internal static readonly DateTimeOffset Start = new(2027, 1, 4, 14, 0, 0, TimeSpan.Zero);

    private readonly NpgsqlDataSource data;
    private readonly List<ServiceProvider> providers = [];
    private readonly IPayloadProtector? protector;
    private readonly Action<ProcessHostOptions>? configureHost;

    private Rig(string connection, string schema, IPayloadProtector? protector, Action<ProcessHostOptions>? configureHost)
    {
        Connection = connection;
        Schema = schema;
        this.protector = protector;
        this.configureHost = configureHost;
        data = NpgsqlDataSource.Create(connection);
        Provider = Build();
    }

    internal string Connection { get; }

    internal string Schema { get; }

    internal string Tenant { get; } = "t-1";

    internal FakeTimeProvider Time { get; } = new(Start);

    internal TestCatalog Catalog { get; } = new();

    internal TestZoneResolver Zones { get; } = new();

    internal List<FakePort> Ports { get; } = [];

    internal CapturingLoggerProvider Logs { get; } = new();

    internal ServiceProvider Provider { get; }

    /// <summary>The engine; resolved on first use, so that ports and processes added before it count.</summary>
    internal IProcessEngine Engine => Provider.GetRequiredService<IProcessEngine>();

    /// <summary>The pump; resolved on first use, so that the ports added before it count.</summary>
    internal IProcessPump Pump => Provider.GetRequiredService<IProcessPump>();

    internal static async Task<Rig> CreateAsync(IPayloadProtector? protector = null, Action<ProcessHostOptions>? configureHost = null, bool migrate = true)
    {
        var connection = await TestDatabase.ConnectionStringAsync();
        var builder = new NpgsqlConnectionStringBuilder(connection) { MaxPoolSize = 8, Pooling = true };
        var rig = new Rig(builder.ConnectionString, "t_" + Guid.NewGuid().ToString("N")[..16], protector, configureHost);
        if (migrate)
        {
            await rig.Provider.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();
        }

        return rig;
    }

    /// <summary>
    /// A second host over the same schema, clock, catalog and ports: another process in a real deployment. A decorator wraps the
    /// store of that host, so that a test can look at, or interfere with, what the pump reads between two of its steps.
    /// </summary>
    internal IProcessPump NewPump(Func<IProcessStore, IProcessStore>? decorate = null)
    {
        var provider = Build(decorate);
        return provider.GetRequiredService<IProcessPump>();
    }

    /// <summary>Opens a transaction on the store of the rig and locks the row of an instance, as another session of a real deployment would.</summary>
    internal async Task<IProcessSession> HoldAsync(string instanceId, string? tenant = null)
    {
        var session = await Provider.GetRequiredService<IProcessStore>().BeginAsync(CancellationToken.None);
        try
        {
            (await session.LockInstanceAsync(new InstanceKey(tenant ?? Tenant, instanceId), skipLocked: false, CancellationToken.None)).Should().NotBeNull();
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>A second migrator over the same schema, as another process of the same deployment would have.</summary>
    internal IProcessStoreMigrator NewMigrator() => Build().GetRequiredService<IProcessStoreMigrator>();

    internal FakePort Port(string name)
    {
        var port = new FakePort(name);
        Ports.Add(port);
        return port;
    }

    /// <summary>Runs the pump until a pass handles nothing; returns the number of items handled in all.</summary>
    internal Task<int> DrainAsync() => DrainAsync(Pump);

    internal static async Task<int> DrainAsync(IProcessPump pump)
    {
        var total = 0;
        for (var pass = 0; pass < 500; pass++)
        {
            var handled = await pump.RunOnceAsync();
            if (handled == 0)
            {
                return total;
            }

            total += handled;
        }

        throw new InvalidOperationException("The pump did not become idle in 500 passes.");
    }

    /// <summary>Starts an instance and returns its id.</summary>
    internal async Task<string> StartAsync(string processId, string businessKey, string inputJson = "{\"case_ref\":\"c-1\"}", string? tenant = null)
    {
        var result = await Engine.StartAsync(new StartRequest(tenant ?? Tenant, processId, businessKey, inputJson));
        return result.InstanceId;
    }

    internal async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = data.CreateCommand(sql.Replace("{schema}", "\"" + Schema + "\"", StringComparison.Ordinal));
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var scalar = await command.ExecuteScalarAsync();
        return scalar switch
        {
            null or DBNull => default!,
            DateTime moment when typeof(T) == typeof(DateTimeOffset) => (T)(object)new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc)),
            _ => (T)scalar,
        };
    }

    internal async Task<long> CountAsync(string table, string where = "true", params (string Name, object? Value)[] parameters) =>
        await ScalarAsync<long>($"select count(*) from {{schema}}.{table} where {where}", parameters);

    internal async Task<List<T>> ColumnAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = data.CreateCommand(sql.Replace("{schema}", "\"" + Schema + "\"", StringComparison.Ordinal));
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<T>();
        while (await reader.ReadAsync())
        {
            values.Add(await reader.IsDBNullAsync(0) ? default! : await reader.GetFieldValueAsync<T>(0));
        }

        return values;
    }

    internal async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = data.CreateCommand(sql.Replace("{schema}", "\"" + Schema + "\"", StringComparison.Ordinal));
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The number of rows that stand for outstanding work of an instance: timers, waits, open tasks and pending or running effects.</summary>
    internal async Task<long> OutstandingWorkAsync(string instanceId)
    {
        var parameter = ("i", (object?)instanceId);
        return await CountAsync("process_timer", "instance_id = @i", parameter)
            + await CountAsync("process_wait", "instance_id = @i", parameter)
            + await CountAsync("process_task", "instance_id = @i and status = 1", parameter)
            + await CountAsync("process_effect", "instance_id = @i and status in (1, 2)", parameter);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ExecuteAsync("drop schema if exists {schema} cascade");
        }
        finally
        {
            foreach (var provider in providers)
            {
                await provider.DisposeAsync();
            }

            await data.DisposeAsync();
        }
    }

    private ServiceProvider Build(Func<IProcessStore, IProcessStore>? decorate = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IProcessCatalog>(Catalog);
        services.AddSingleton<IZoneResolver>(Zones);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddSingleton<IEnumerable<IProcessPort>>(_ => Ports);
        if (protector is null)
        {
            services.AddPlaintextProcessPayloads();
        }
        else
        {
            services.AddSingleton(protector);
        }

        services.AddBrainProcessHostPostgres(
            store =>
            {
                store.ConnectionString = Connection;
                store.Schema = Schema;
            },
            configureHost);
        if (decorate is not null)
        {
            services.AddSingleton<IProcessStore>(sp => decorate(sp.GetRequiredService<PostgresProcessStore>()));
        }

        var provider = services.BuildServiceProvider();
        providers.Add(provider);
        return provider;
    }
}
