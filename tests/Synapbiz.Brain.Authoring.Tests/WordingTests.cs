using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>What a person reads in the real answers of the tools, on every reference process, in both languages.</summary>
public sealed partial class WordingTests
{
    [GeneratedRegex(@"\b(flows?|flujos?)\b", RegexOptions.IgnoreCase)]
    internal static partial Regex ForbiddenWord();

    internal static IEnumerable<string> Spoken(JsonNode? node, params string[] members) =>
        node is JsonObject answer ? members.SelectMany(member => Texts(answer[member])).Prepend(answer["say"]?.GetValue<string>() ?? string.Empty) : [];

    // Every string below a member that a person may read; ids, pointers, hashes and raw data are not read by a person.
    private static IEnumerable<string> Texts(JsonNode? node) => node switch
    {
        null => [],
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        JsonArray array => array.SelectMany(Texts),
        JsonObject obj => obj.SelectMany(pair => pair.Key is "id" or "kind" or "from" or "to" or "name" or "contentHash" or "target" or "stage" or "port" or "path" or "code" or "hint" or "detail" ? [] : Texts(pair.Value)),
        _ => [],
    };

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Real_answers_on_every_reference_process_never_say_flow_or_flujo_and_have_no_code_marks(string language)
    {
        var (tools, store) = ReferenceWorkspace.Rig();
        var spoken = new List<(string Where, string Line)>();

        void Collect(string where, JsonObject answer, params string[] members) =>
            spoken.AddRange(Spoken(answer, members).Select(line => (where, line)));

        foreach (var id in ReferenceDocs.Ids)
        {
            var get = tools.Invoke("flow_get", new JsonObject { ["id"] = id, ["language"] = language });
            IsOk(get).Should().BeTrue(id + " is read: " + get["say"]);
            Collect("flow_get " + id, get, "description", "diagram", "scenarios", "coverage", "readiness");

            var document = (JsonObject)get["document"]!.DeepClone();
            document["version"] = document["version"]!.GetValue<int>() + 1;
            var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = id, ["document"] = document, ["language"] = language });
            IsOk(saved).Should().BeTrue(id + " is saved: " + saved["say"]);
            Collect("flow_draft_save " + id, saved, "changes", "readiness", "scenarios", "coverage", "description");

            var proposed = tools.Invoke("flow_propose", new JsonObject { ["id"] = id, ["summary"] = "A longer reason for the change", ["requestedBy"] = "Nurse Ann", ["language"] = language });
            Collect("flow_propose " + id, proposed, "readiness");
            if (IsOk(proposed))
            {
                var name = proposed["proposal"]!["name"]!.GetValue<string>();
                var read = tools.Invoke("proposal_get", new JsonObject { ["name"] = name, ["language"] = language });
                IsOk(read).Should().BeTrue(read["say"]?.ToString());
                Collect("proposal_get " + id, read, "text");
            }
        }

        spoken.Should().NotBeEmpty();
        foreach (var (where, line) in spoken)
        {
            ForbiddenWord().IsMatch(line).Should().BeFalse(where + " (" + language + "): " + line);
            line.Should().NotContain("`", where + " (" + language + "): " + line);
        }

        _ = store;
    }
}
