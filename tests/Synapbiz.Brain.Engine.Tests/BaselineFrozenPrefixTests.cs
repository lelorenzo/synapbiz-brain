using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Synapbiz.Brain.Engine.Tests;

/// <summary>
/// The baseline is append-only: the entries recorded when PR 2 added the structure documents (the first 629, the 364 of
/// PR 1 and the 265 of the rule scenarios) may not change when entries are appended.
/// </summary>
public sealed class BaselineFrozenPrefixTests
{
    private const int FrozenEntries = 629;
    private const string FrozenPrefixSha256 = "644c5a25d02b6c50e4c22df9ba9299a322be60bbd55bdfb612cad30adc1e5919";

    [Fact]
    public void TheFirstEntriesOfTheBaselineAreUnchanged()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Parity", "decisions.parity.json")));
        var entries = document.RootElement.GetProperty("scenarios").EnumerateArray().ToList();

        entries.Count.Should().BeGreaterThanOrEqualTo(FrozenEntries);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries.Take(FrozenEntries))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.GetProperty("name").GetString()!));
            hash.AppendData([0]);
            hash.AppendData(Encoding.UTF8.GetBytes(entry.GetProperty("result").GetString()!));
            hash.AppendData([0]);
        }

        Convert.ToHexStringLower(hash.GetHashAndReset()).Should().Be(FrozenPrefixSha256);
    }
}
