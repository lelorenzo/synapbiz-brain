using Testcontainers.PostgreSql;

namespace Synapbiz.Brain.Host.Postgres.Tests;

/// <summary>
/// Finds the PostgreSQL the tests run against: the connection string of <c>BRAIN_TEST_POSTGRES</c> when it is set, otherwise one
/// <c>postgres:16-alpine</c> container for the whole test run. When neither is possible the tests fail with a message that says how
/// to provide a database; they never skip.
/// </summary>
internal static class TestDatabase
{
    internal const string Variable = "BRAIN_TEST_POSTGRES";

    private static readonly Lazy<Task<string>> Connection = new(FindAsync);

    internal static Task<string> ConnectionStringAsync() => Connection.Value;

    private static async Task<string> FindAsync()
    {
        var configured = Environment.GetEnvironmentVariable(Variable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var start = StartContainerAsync();
        await Task.WhenAny(start);
        if (start.IsFaulted)
        {
            throw new InvalidOperationException(
                $"These tests need a PostgreSQL 14 or later. Set the environment variable {Variable} to an Npgsql connection string of a database the tests may create schemas in, or make Docker available so that a postgres:16-alpine container can be started.",
                start.Exception!.GetBaseException());
        }

        return await start;
    }

    private static async Task<string> StartContainerAsync()
    {
        var container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await container.StartAsync();
        return container.GetConnectionString() + ";Include Error Detail=true";
    }
}
