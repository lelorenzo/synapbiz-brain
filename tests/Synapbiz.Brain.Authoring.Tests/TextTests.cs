using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>The texts people read: English and Spanish together, the owner's wording ("process", never "flow"), and a guide that cannot drift from the format.</summary>
public sealed partial class TextTests
{
    [GeneratedRegex(@"\{([a-zA-Z]+)\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\b(flows?|flujos?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForbiddenWord();

    [Fact]
    public void Every_key_has_both_languages_and_neither_table_has_a_key_the_other_lacks()
    {
        AuthoringText.Keys.Should().NotBeEmpty();
        AuthoringText.SpanishKeys.Should().BeEquivalentTo(AuthoringText.Keys);
    }

    [Fact]
    public void The_two_texts_of_a_key_use_the_same_placeholders_and_differ_unless_it_is_punctuation()
    {
        foreach (var key in AuthoringText.Keys)
        {
            var english = AuthoringText.Template(WorkbenchLocale.En, key);
            var spanish = AuthoringText.Template(WorkbenchLocale.Es, key);
            english.Should().NotBeNullOrEmpty(key);
            spanish.Should().NotBeNullOrEmpty(key);
            AuthoringText.Placeholders(spanish).Should().BeEquivalentTo(AuthoringText.Placeholders(english), key);
            var onlyPlaceholders = !Placeholder().Replace(english, string.Empty).Any(char.IsLetter);
            if (!AuthoringText.IntentionallyEqual.Contains(key) && !onlyPlaceholders)
            {
                spanish.Should().NotBe(english, key + " is not translated");
            }

            Placeholder().Replace(english, string.Empty).Should().NotContain("{").And.NotContain("}", key);
            Placeholder().Replace(spanish, string.Empty).Should().NotContain("{").And.NotContain("}", key);
        }
    }

    [Fact]
    public void No_text_in_the_table_says_flow_or_flujo()
    {
        foreach (var key in AuthoringText.Keys)
        {
            foreach (var locale in new[] { WorkbenchLocale.En, WorkbenchLocale.Es })
            {
                ForbiddenWord().IsMatch(Placeholder().Replace(AuthoringText.Template(locale, key), string.Empty)).Should().BeFalse(key + " (" + locale + ")");
            }
        }
    }

    [Fact]
    public void No_text_a_person_reads_from_the_differ_says_flow_or_flujo()
    {
        foreach (var file in new[] { "differ.en.txt", "differ.es.txt" })
        {
            var text = File.ReadAllText(Locate("tests/Synapbiz.Brain.Authoring.Tests/Golden/" + file));
            ForbiddenWord().Matches(text).Select(match => match.Value).Should().BeEmpty(file);
        }
    }

    [Fact]
    public void No_answer_a_person_may_read_says_flow_or_flujo()
    {
        foreach (var language in new[] { "en", "es" })
        {
            var (tools, store, _) = Rig();
            var spoken = new List<string>();

            void Collect(JsonObject answer, params string[] members)
            {
                spoken.Add(answer["say"]!.GetValue<string>());
                foreach (var member in members)
                {
                    spoken.AddRange(Strings(answer[member]));
                }
            }

            Collect(tools.Invoke("brain_catalog", Obj("{\"language\":\"" + language + "\"}")));
            Collect(tools.Invoke("flow_list", Obj("{\"language\":\"" + language + "\"}")));
            Collect(tools.Invoke("rule_list", Obj("{\"language\":\"" + language + "\"}")));
            Collect(tools.Invoke("flow_get", Obj("{\"id\":\"visit.note_wait\",\"language\":\"" + language + "\"}")), "description", "diagram");
            Collect(tools.Invoke("rule_get", Obj("{\"id\":\"visit.note_ready\",\"language\":\"" + language + "\"}")), "sentences");
            var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["version"] = 2), ["language"] = language });
            Collect(saved, "changes", "readiness", "scenarios", "coverage");
            Collect(tools.Invoke("flow_get", Obj("{\"id\":\"nothing.here\",\"language\":\"" + language + "\"}")));
            var proposed = tools.Invoke("flow_propose", Obj("{\"id\":\"visit.note_wait\",\"summary\":\"s\",\"requestedBy\":\"n\",\"language\":\"" + language + "\"}"));
            Collect(proposed);
            var read = tools.Invoke("proposal_get", new JsonObject { ["name"] = store.ProposalNames.Single(), ["language"] = language });
            Collect(read, "text");
            Collect(tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = NoteWaitChanged(document => document["body"]!["start"] = "nowhere"), ["language"] = language }), "errors", "readiness");

            foreach (var line in spoken)
            {
                ForbiddenWord().IsMatch(line).Should().BeFalse(language + ": " + line);
            }
        }
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        null => [],
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        JsonArray array => array.SelectMany(Strings),
        JsonObject obj => obj.SelectMany(pair => pair.Key is "id" or "kind" or "from" or "to" or "name" or "contentHash" ? [] : Strings(pair.Value)),
        _ => [],
    };

    // ---- The guide cannot drift from the format.

    [Fact]
    public void The_step_kinds_of_the_guide_are_the_kinds_of_the_outline_and_of_the_schema()
    {
        var outline = Enum.GetNames<OutlineStepKind>().Select(name => char.ToLowerInvariant(name[0]) + name[1..]);
        BrainGuide.StepKinds.Should().BeEquivalentTo(outline);

        var schema = JsonNode.Parse(Read("src/Synapbiz.Brain.Process/Schemas/brain.process.v1.json"))!;
        var lists = new List<string[]>();

        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonArray array:
                    var strings = array.OfType<JsonValue>().Select(value => value.TryGetValue<string>(out var text) ? text : null).ToArray();
                    if (strings.Length == array.Count && strings.Contains("humanTask"))
                    {
                        lists.Add(strings!);
                    }

                    foreach (var item in array)
                    {
                        Walk(item);
                    }

                    break;
                case JsonObject obj:
                    foreach (var pair in obj)
                    {
                        Walk(pair.Value);
                    }

                    break;
                default:
                    break;
            }
        }

        Walk(schema);
        lists.Should().NotBeEmpty();
        lists.Should().Contain(list => list.OrderBy(item => item, StringComparer.Ordinal).SequenceEqual(BrainGuide.StepKinds.OrderBy(item => item, StringComparer.Ordinal)));
    }

    [Fact]
    public void The_guide_describes_every_step_kind_and_every_concept_and_the_format_reference_names_each_concept()
    {
        var steps = BrainGuide.Find("steps")!.Text;
        foreach (var kind in BrainGuide.StepKinds)
        {
            steps.Should().Contain(kind);
        }

        var all = string.Join("\n", BrainGuide.Sections.Select(section => section.Text));
        var reference = Read("docs/reference/brain-process-v1.md");
        foreach (var concept in BrainGuide.Concepts)
        {
            all.Should().Contain(concept, "the guide is meant to name " + concept);
            reference.Should().Contain(concept, "the guide names " + concept + " but the reference of the format does not");
        }
    }

    [Fact]
    public void The_kinds_the_reference_documents_use_are_all_in_the_guide()
    {
        foreach (var id in ReferenceDocs.Ids)
        {
            var steps = (JsonObject)JsonNode.Parse(ReferenceDocs.Text(id))!["body"]!["steps"]!;
            foreach (var kind in steps.Select(step => step.Value!["kind"]!.GetValue<string>()).Distinct())
            {
                BrainGuide.StepKinds.Should().Contain(kind, id);
            }
        }
    }

    [Fact]
    public void The_worked_examples_are_the_reference_documents_and_they_load()
    {
        var examples = BrainGuide.ExampleDocuments.ToDictionary(item => item.Name, item => item.Text);
        examples["practice.daily_refresh"].Should().Be(ReferenceDocs.Text("practice.daily_refresh").Replace("\r\n", "\n", StringComparison.Ordinal));
        examples["visit.note_wait"].Should().Be(ReferenceDocs.Text("visit.note_wait").Replace("\r\n", "\n", StringComparison.Ordinal));
        examples["visit.note_ready"].Should().Be(NoteReady.Replace("\r\n", "\n", StringComparison.Ordinal));
        foreach (var id in new[] { "practice.daily_refresh", "visit.note_wait" })
        {
            var loaded = ReferenceDocs.TryLoad(id, examples[id]);
            loaded.Process.Should().NotBeNull(id + ": " + string.Join("; ", loaded.Errors.Select(error => error.Code + " " + error.Path)));
            loaded.Process!.RunScenarios().Results.Should().OnlyContain(result => result.Passed);
        }
    }

    [Fact]
    public void The_guide_tool_serves_every_section_and_the_examples_and_the_working_method_matches_the_spec()
    {
        var (tools, _, _) = Rig();
        foreach (var section in BrainGuide.Sections)
        {
            var answer = tools.Invoke("brain_guide", Obj("{\"section\":\"" + section.Id + "\"}"));
            IsOk(answer).Should().BeTrue(section.Id);
            answer.ToJsonString().Should().Contain(section.Title.Split(' ')[0]);
        }

        // The working method of the specification (section 7), step by step.
        var method = BrainGuide.WorkingMethod;
        foreach (var phrase in new[] { "Read before writing", "ambiguous", "smallest", "every error", "scenario", "simulate", "ready", "live" })
        {
            method.Contains(phrase, StringComparison.OrdinalIgnoreCase).Should().BeTrue("the working method says \"" + phrase + "\"");
        }
    }
}
