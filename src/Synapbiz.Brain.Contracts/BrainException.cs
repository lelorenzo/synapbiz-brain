using System.Diagnostics.CodeAnalysis;

namespace Synapbiz.Brain;

/// <summary>
/// Base type for all Brain engine exceptions.
/// Carries a stable machine-readable <see cref="ErrorCode"/> and an
/// <see cref="Args"/> bag for parameterised, localisable error messages.
/// The English <see cref="Exception.Message"/> remains for logs and developers;
/// clients localise using <c>ErrorCode</c> + <c>Args</c>.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Brain exceptions are intentionally minimal: a single (code, message, args, innerException) constructor forces every throw site to supply a stable error code.")]
public abstract class BrainException : Exception
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyArgs =
        new Dictionary<string, object?>(0);

    /// <summary>
    /// Stable, machine-readable error code in <c>{namespace}.{snake_case_reason}</c> form.
    /// MUST be stable across releases — clients key translations off this value.
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Named arguments for message interpolation on the client side.
    /// Values MUST be JSON-serialisable primitives (string, number, bool, Guid, DateTime, null).
    /// Empty dictionary by default.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Args { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BrainException"/> class.
    /// </summary>
    /// <param name="errorCode">Stable error code. MUST be non-empty.</param>
    /// <param name="message">English developer/log message.</param>
    /// <param name="args">Named arguments for client-side interpolation. <c>null</c> is treated as empty.</param>
    /// <param name="innerException">Optional inner exception.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="errorCode"/> is null/empty/whitespace.</exception>
    protected BrainException(
        string errorCode,
        string message,
        IReadOnlyDictionary<string, object?>? args = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new ArgumentException("ErrorCode must be a non-empty string.", nameof(errorCode));
        }

        ErrorCode = errorCode;
        Args = args ?? EmptyArgs;
    }
}
