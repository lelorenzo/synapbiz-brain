using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Synapbiz.Brain.Host.Postgres.Tests;

public sealed class MigratorTests
{
    private static readonly string[] Tables =
    [
        "process_instance", "process_history", "process_timer", "process_wait", "process_task", "process_effect", "process_event", "process_inbox", "process_activation", "process_event_trigger", "process_schedule", "process_schema_version",
    ];

    [Fact]
    public async Task MigrateCreatesTheSchemaAndEveryTable()
    {
        await using var rig = await Rig.CreateAsync(migrate: false);
        (await rig.ScalarAsync<long>("select count(*) from information_schema.schemata where schema_name = @s", ("s", rig.Schema))).Should().Be(0);

        await rig.Provider.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();

        foreach (var table in Tables)
        {
            (await rig.ScalarAsync<long>("select count(*) from information_schema.tables where table_schema = @s and table_name = @t", ("s", rig.Schema), ("t", table)))
                .Should().Be(1, table + " must exist");
        }

        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version")).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task EveryTableHasTheTenantAsTheFirstColumnOfItsPrimaryKey()
    {
        await using var rig = await Rig.CreateAsync();

        foreach (var table in Tables.Where(name => name != "process_schema_version"))
        {
            var first = await rig.ColumnAsync<string>(
                "select kcu.column_name from information_schema.table_constraints tc join information_schema.key_column_usage kcu on kcu.constraint_name = tc.constraint_name and kcu.table_schema = tc.table_schema where tc.table_schema = @s and tc.table_name = @t and tc.constraint_type = 'PRIMARY KEY' order by kcu.ordinal_position limit 1",
                ("s", rig.Schema),
                ("t", table));
            first.Should().Equal("tenant_id");
        }
    }

    [Fact]
    public async Task ASecondRunChangesNothing()
    {
        await using var rig = await Rig.CreateAsync();
        var migrator = rig.Provider.GetRequiredService<IProcessStoreMigrator>();
        var before = await rig.ColumnAsync<DateTimeOffset>("select applied_at from {schema}.process_schema_version");
        rig.Time.Advance(TimeSpan.FromHours(1));

        await migrator.MigrateAsync();

        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version")).Should().Equal(1, 2, 3, 4);
        (await rig.ColumnAsync<DateTimeOffset>("select applied_at from {schema}.process_schema_version")).Should().Equal(before);
        (await rig.ScalarAsync<long>("select count(*) from information_schema.tables where table_schema = @s", ("s", rig.Schema))).Should().Be(Tables.Length);
    }

    [Fact]
    public async Task ASchemaThatHasOnlyTheFirstScriptIsUpgradedByTheSecondOne()
    {
        await using var rig = await Rig.CreateAsync();
        await rig.ExecuteAsync("drop table {schema}.process_activation; drop table {schema}.process_event_trigger; drop table {schema}.process_schedule; drop index {schema}.ix_process_instance_created; drop index {schema}.ix_process_instance_status; drop index {schema}.ix_process_instance_purge; drop index {schema}.ix_process_event_received; drop index {schema}.ix_process_inbox_instance; delete from {schema}.process_schema_version where version >= 2");
        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version")).Should().Equal(1);

        await rig.Provider.GetRequiredService<IProcessStoreMigrator>().MigrateAsync();

        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version order by version")).Should().Equal(1, 2, 3, 4);
        (await rig.CountAsync("process_schedule")).Should().Be(0);
        (await rig.CountAsync("process_activation")).Should().Be(0);
        (await rig.CountAsync("process_event_trigger")).Should().Be(0);
    }

    [Fact]
    public async Task AppliedAtComesFromTheInjectedClock()
    {
        await using var rig = await Rig.CreateAsync();

        (await rig.ColumnAsync<DateTimeOffset>("select applied_at from {schema}.process_schema_version")).Should().Equal(Rig.Start, Rig.Start, Rig.Start, Rig.Start);
    }

    [Fact]
    public async Task TwoConcurrentRunsBothSucceed()
    {
        await using var rig = await Rig.CreateAsync(migrate: false);
        var first = rig.Provider.GetRequiredService<IProcessStoreMigrator>();
        var second = rig.NewMigrator();

        await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).MigrateAsync()));

        (await rig.ColumnAsync<int>("select version from {schema}.process_schema_version")).Should().Equal(1, 2, 3, 4);
        (await rig.ScalarAsync<long>("select count(*) from information_schema.tables where table_schema = @s", ("s", rig.Schema))).Should().Be(Tables.Length);
    }

    [Fact]
    public async Task AnInvalidSchemaNameIsRefusedBeforeAnySqlRuns()
    {
        var act = () => new PostgresProcessStoreMigrator(new PostgresProcessStoreOptions { ConnectionString = "Host=localhost", Schema = "bad\"; drop table x; --" }, TimeProvider.System);

        act.Should().Throw<ArgumentException>();
        await Task.CompletedTask;
    }
}
