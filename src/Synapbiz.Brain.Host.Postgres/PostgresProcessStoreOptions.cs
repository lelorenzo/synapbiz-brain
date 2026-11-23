using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Synapbiz.Brain.Host.Postgres;

/// <summary>The settings of the PostgreSQL store.</summary>
public sealed class PostgresProcessStoreOptions
{
    /// <summary>The Npgsql connection string. Required. It is never logged.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>The schema that holds the tables of the host: lower-case letters, digits and underscores, starting with a letter or an underscore, at most 63 characters.</summary>
    public string Schema { get; set; } = "brain";
}

/// <summary>Validates <see cref="PostgresProcessStoreOptions"/> when they are first resolved.</summary>
internal sealed partial class PostgresProcessStoreOptionsValidator : IValidateOptions<PostgresProcessStoreOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, PostgresProcessStoreOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("PostgresProcessStoreOptions.ConnectionString is required.");
        }

        if (!IsValidSchema(options.Schema))
        {
            failures.Add("PostgresProcessStoreOptions.Schema must match ^[a-z_][a-z0-9_]{0,62}$.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether a schema name is acceptable: the only identifier of the host's SQL that comes from configuration.</summary>
    internal static bool IsValidSchema(string? schema) => schema is not null && SchemaPattern().IsMatch(schema);

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaPattern();
}
