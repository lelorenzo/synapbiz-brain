using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Spec.Tests;

/// <summary>Finds and reads the shared fixture files above the test assembly.</summary>
internal static class TestFiles
{
    internal static string Locate(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("Could not locate the fixture file above the test assembly.", relativePath);
    }

    internal static string Read(string relativePath) => File.ReadAllText(Locate(relativePath));

    internal static JsonNode ReadJson(string relativePath) => JsonNode.Parse(Read(relativePath))!;

    /// <summary>The contract a structure document names: a shared fixture contract by <c>contract</c>, or a repository file by <c>contractFile</c>.</summary>
    internal static BrainContract ContractOf(JsonNode fixture) =>
        fixture["contractFile"] is { } file
            ? BrainContract.Parse(Read(file.GetValue<string>()))
            : Contract(fixture["contract"]!.GetValue<string>());

    internal static BrainContract Contract(string name) =>
        BrainContract.Parse(Read($"tests/fixtures/spec/brain/contracts/contract-{name}.json"));
}
