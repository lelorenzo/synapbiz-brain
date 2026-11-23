namespace Synapbiz.Brain.Process.Hosting;

/// <summary>The default calendar: the day in UTC.</summary>
internal sealed class UtcProcessCalendar : IProcessCalendar
{
    /// <inheritdoc />
    public DateOnly Today(string tenantId, DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);
}

/// <summary>A protector that stores texts as they are. For development and tests; the consumer registers it explicitly.</summary>
internal sealed class PlaintextPayloadProtector : IPayloadProtector
{
    /// <inheritdoc />
    public string Protect(string tenantId, string plaintext) => plaintext;

    /// <inheritdoc />
    public string Unprotect(string tenantId, string protectedText) => protectedText;
}
