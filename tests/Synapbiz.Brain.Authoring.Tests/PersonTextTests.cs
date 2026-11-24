using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Synapbiz.Brain.Workbench;
using static Synapbiz.Brain.Authoring.Tests.Fixtures;

namespace Synapbiz.Brain.Authoring.Tests;

/// <summary>What a person hears is a sentence of the string table: no code, pointer, schema or platform text, and the language asked for.</summary>
public sealed partial class PersonTextTests
{
    [GeneratedRegex(@"(^|[\s(])/[A-Za-z_]+(/[A-Za-z0-9_\-]+)*")]
    private static partial Regex Pointer();

    [GeneratedRegex(@"\b[a-z][a-z_]*(\.[a-z_][a-z_0-9]*){2,}\b")]
    private static partial Regex DottedCode();

    [GeneratedRegex("“[^”]*”")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\b(the|is|was|are|were|not|and|with|must|cannot|has|have|does|which|from|than)\b")]
    private static partial Regex EnglishWord();

    [GeneratedRegex(@"\{[a-zA-Z]+\}")]
    private static partial Regex Placeholder();

    /// <summary>Fragments the engine, the schema validator, the JSON parser or the platform write; none may reach a person.</summary>
    internal static readonly string[] RawFragments =
    [
        "`", "JSON", "json", "schema", "Schema", "pointer", "Exception", "System.", "Json.", "at path", "property", "Property", "keyword", "deserializ", "token '",
        "is not valid under", "additionalProperties", "required property", "Expected", "Unexpected", "null reference", "Object reference", "Index was", "Sequence contains", "authoring.", "workbench.", ".json", "$spec", "$ref", "{", "}",
    ];

    internal static void CheckSentence(string sentence, string language, string context)
    {
        sentence.Should().NotBeNullOrWhiteSpace(context);
        foreach (var fragment in RawFragments)
        {
            sentence.Should().NotContain(fragment, context + ": " + sentence);
        }

        Pointer().IsMatch(sentence).Should().BeFalse(context + " has a JSON pointer: " + sentence);
        DottedCode().IsMatch(Quoted().Replace(sentence, string.Empty)).Should().BeFalse(context + " has a dotted code: " + sentence);
        WordingTests.ForbiddenWord().IsMatch(sentence).Should().BeFalse(context + ": " + sentence);
        if (language == "es")
        {
            EnglishWord().IsMatch(Quoted().Replace(sentence, string.Empty)).Should().BeFalse(context + " has English in Spanish: " + sentence);
        }
    }

    // The sentence has to be built from a template of the table: the literal parts of some template, in order.
    internal static void ComesFromTheTable(string sentence, string language, string context)
    {
        var locale = language == "es" ? WorkbenchLocale.Es : WorkbenchLocale.En;
        var matched = AuthoringText.Keys.Any(key =>
        {
            var pattern = "^" + string.Join(".*?", Placeholder().Split(AuthoringText.Template(locale, key)).Select(Regex.Escape)) + "$";
            return Regex.IsMatch(sentence, pattern, RegexOptions.Singleline);
        });
        matched.Should().BeTrue(context + " is not a sentence of the table: " + sentence);
    }

    [Fact]
    public void Every_sentence_of_the_error_table_is_plain_in_both_languages()
    {
        foreach (var key in AuthoringText.Keys.Where(key => key.StartsWith(AuthoringText.ErrorPrefix, StringComparison.Ordinal)))
        {
            foreach (var (locale, language) in new[] { (WorkbenchLocale.En, "en"), (WorkbenchLocale.Es, "es") })
            {
                var sentence = Placeholder().Replace(AuthoringText.Template(locale, key), "“x”");
                CheckSentence(sentence, language, key);
            }
        }
    }

    [Fact]
    public void Every_code_a_loader_or_a_check_can_give_has_its_own_sentence_and_not_the_generic_one()
    {
        var generic = AuthoringText.Template(WorkbenchLocale.En, "err.generic");
        var missing = FixHints.Codes
            .Where(code => !code.StartsWith("authoring.", StringComparison.Ordinal) && !code.StartsWith("mcp.", StringComparison.Ordinal))
            .Where(code => !AuthoringText.Has(AuthoringText.ErrorPrefix + code))
            .ToList();
        missing.Should().BeEmpty("these codes would get the generic sentence: " + string.Join(", ", missing));
        _ = generic;
    }

    [Fact]
    public void A_code_nobody_wrote_a_sentence_for_gets_the_one_generic_sentence_in_each_language()
    {
        foreach (var (locale, language) in new[] { (WorkbenchLocale.En, "en"), (WorkbenchLocale.Es, "es") })
        {
            var sentence = PersonWords.Error(locale, "some.future.code", language == "es" ? "en el paso “Send”" : "in the step “Send”");
            CheckSentence(sentence, language, "generic");
            sentence.Should().NotContain("some.future.code");
        }
    }

    [Fact]
    public void Every_failure_a_tool_reports_with_a_normal_answer_is_said_plainly_in_both_languages()
    {
        var said = new List<(string Where, string Language, string Sentence)>();

        void Collect(string where, string language, JsonNode? node)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var (name, value) in obj)
                    {
                        if (name is "say" or "requirement" && value is JsonValue text && text.TryGetValue<string>(out var sentence))
                        {
                            said.Add((where + "." + name, language, sentence));
                        }
                        else if (name is "reasons" && value is JsonArray reasons)
                        {
                            said.AddRange(reasons.Select(reason => (where + ".reasons", language, reason!.GetValue<string>())));
                        }
                        else
                        {
                            Collect(where, language, value);
                        }
                    }

                    break;
                case JsonArray array:
                    foreach (var item in array)
                    {
                        Collect(where, language, item);
                    }

                    break;
                default:
                    break;
            }
        }

        foreach (var language in new[] { "en", "es" })
        {
            foreach (var (name, patch, noTests) in new[]
            {
                ("loader", """[{"op":"replace","path":"/body/steps/await_note/onExpired","value":"nowhere"}]""", false),
                ("scenario", """[{"op":"replace","path":"/body/steps/chase_note/on/signed","value":"cancelled"}]""", false),
                ("labels", """[{"op":"remove","path":"/meta/display/es/steps/confirm_note"}]""", false),
                ("untested", """[{"op":"add","path":"/body/steps/chase_note/timeout","value":"PT24H"},{"op":"add","path":"/body/steps/chase_note/onTimeout","value":"cancelled"}]""", false),
                ("port", """[{"op":"replace","path":"/body/steps/await_note/port","value":"records.nothing_here"}]""", false),
                ("no tests", "[]", true),
            })
            {
                var (tools, _) = ReferenceWorkspace.Rig();
                var document = Parsed(ReferenceDocs.Changed("visit.note_wait", patch, noTests));
                document["version"] = 2;
                var saved = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = document, ["language"] = language });
                Collect("save " + name, language, saved);
                Collect("propose " + name, language, tools.Invoke("flow_propose", new JsonObject { ["id"] = "visit.note_wait", ["summary"] = "A reason that is long enough", ["requestedBy"] = "Nurse Ann", ["language"] = language }));
            }

            var (rig, _) = ReferenceWorkspace.Rig();
            _ = rig.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = Parsed(ReferenceDocs.Text("visit.note_wait")), ["language"] = language });
            foreach (var request in new[]
            {
                """{"id":"visit.note_wait","start":{"input":{"colour":"red"}}}""",
                """{"id":"visit.note_wait","start":{"input":{"case_ref":5}}}""",
                """{"id":"visit.note_wait","start":{"zone":"Mars/Base"}}""",
                """{"id":"visit.note_wait","start":{"at":"yesterday-ish"}}""",
                """{"session":"sim-404","command":{"type":"advance","duration":"PT1H"}}""",
                """{"id":"visit.note_wait","start":{"at":"2027-01-04T14:00:00Z","input":{"case_ref":"c-1"}}}""",
            })
            {
                var asked = (JsonObject)JsonNode.Parse(request)!;
                asked["language"] = language;
                Collect("simulate " + request, language, rig.Invoke("flow_draft_simulate", asked));
            }

            foreach (var command in new[]
            {
                """{"type":"complete","effect":"no_such_effect","output":{}}""",
                """{"type":"advance","by":"PT1H"}""",
                """{"type":"advance","duration":"soon"}""",
                """{"type":"task","task":"none","outcome":"x"}""",
                """{"type":"event","name":"nothing.here"}""",
                """{"type":"fail","effect":"none","code":"x"}""",
            })
            {
                var (sim, _) = ReferenceWorkspace.Rig();
                _ = sim.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = Parsed(ReferenceDocs.Text("visit.note_wait")) });
                var started = sim.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"at":"2027-01-04T14:00:00Z","input":{"case_ref":"c-1"}}}"""));
                var asked = new JsonObject { ["session"] = started["session"]!.GetValue<string>(), ["command"] = JsonNode.Parse(command), ["language"] = language };
                Collect("command " + command, language, sim.Invoke("flow_draft_simulate", asked));
            }

            foreach (var request in new[]
            {
                """{"id":"visit.note_ready","facts":{"colour":"red"}}""",
                """{"id":"visit.note_ready","facts":{"status":"signed"},"asOf":"tomorrow"}""",
            })
            {
                var (tools, _, _) = Rig();
                var asked = (JsonObject)JsonNode.Parse(request)!;
                asked["language"] = language;
                Collect("rule_simulate " + request, language, tools.Invoke("rule_simulate", asked));
            }
        }

        said.Count.Should().BeGreaterThan(60, "the walk reaches the refusals and the findings of both languages");
        foreach (var (where, language, sentence) in said)
        {
            CheckSentence(sentence, language, where);
        }
    }

    private static JsonObject Parsed(string text) => (JsonObject)JsonNode.Parse(text)!;

    [Fact]
    public void An_input_the_schema_refuses_names_the_wrong_and_the_right_member_in_words()
    {
        var (tools, _, _) = Rig();
        _ = tools.Invoke("flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = Parsed(NoteWait) });
        var started = tools.Invoke("flow_draft_simulate", Obj("""{"id":"visit.note_wait","start":{"input":{"case_ref":"c-1"}}}"""));
        var token = started["session"]!.GetValue<string>();
        var en = tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = token, ["command"] = Obj("""{"type":"advance","by":"PT1H"}""") })["say"]!.GetValue<string>();
        en.Should().Contain("“by”").And.Contain("“duration”");
        var es = tools.Invoke("flow_draft_simulate", new JsonObject { ["session"] = token, ["language"] = "es", ["command"] = Obj("""{"type":"advance","by":"PT1H"}""") })["say"]!.GetValue<string>();
        es.Should().Contain("“by”").And.Contain("“duration”");
        CheckSentence(en, "en", "input.unknown");
        CheckSentence(es, "es", "input.unknown");
    }
}
