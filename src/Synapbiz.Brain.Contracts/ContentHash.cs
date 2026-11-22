using System.Security.Cryptography;

namespace Synapbiz.Brain;

/// <summary>
/// SHA-256 content hash value: a lowercase 64-character hexadecimal string.
/// Equality is ordinal on <see cref="Value"/>.
/// </summary>
public sealed class ContentHash : IEquatable<ContentHash>
{
    /// <summary>Gets the canonical (lowercase, 64-char) SHA-256 hex digest.</summary>
    public string Value { get; }

    private ContentHash(string value)
    {
        Value = value;
    }

    /// <summary>Computes the SHA-256 of the supplied bytes and returns the resulting <see cref="ContentHash"/>.</summary>
    public static ContentHash FromBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return new ContentHash(Convert.ToHexStringLower(hash));
    }

    /// <inheritdoc />
    public bool Equals(ContentHash? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as ContentHash);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>Value equality.</summary>
    public static bool operator ==(ContentHash? left, ContentHash? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Value inequality.</summary>
    public static bool operator !=(ContentHash? left, ContentHash? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => Value;
}
