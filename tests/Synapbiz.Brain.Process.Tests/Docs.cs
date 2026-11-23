using System.Globalization;
using System.Text.Json.Nodes;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process.Tests;

/// <summary>Builds synthetic process documents and rules for the tests.</summary>
internal static class Docs
{
    internal static readonly DateTimeOffset T0 = new(2027, 1, 4, 14, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<BrainContract> Contract = new(() => BrainContract.Parse(TestFiles.Read(TestFiles.Fixtures + "conformance.contract.json")));

    /// <summary>The rule of kind <c>conf.flag</c> (hit policy first): ten or more is big, zero to nine is small, below zero decides nothing.</summary>
    internal static BrainRule FlagRule { get; } = LoadRule("conformance.flag.rule.json");

    /// <summary>The collect rule of kind <c>conf.list</c>.</summary>
    internal static BrainRule ListRule { get; } = LoadRule("conformance.list.rule.json");

    internal static ProcessClock Clock(DateTimeOffset? now = null)
    {
        var instant = now ?? T0;
        return new ProcessClock(instant, DateOnly.FromDateTime(instant.UtcDateTime));
    }

    /// <summary>The text of a process document with the given variables, steps and extra body members.</summary>
    internal static string Text(string variables, string steps, string extra = "", string uses = "[]", string start = "s") =>
        $$"""
        {
          "$spec": "brain.process@1", "id": "t.proc", "version": 1, "uses": {{uses}}, "requires": [], "meta": {},
          "body": {
            "subject": "case", "stages": ["one", "two"], "variables": {{variables}}, "start": "{{start}}", "steps": {{steps}}{{(extra.Length == 0 ? "" : ", " + extra)}}
          }
        }
        """;

    /// <summary>Loads a document; the test fails with the list of errors when it does not load.</summary>
    internal static BrainProcess Load(string variables, string steps, string extra = "", string uses = "[]", string start = "s", params BrainRule[] rules)
    {
        var text = Text(variables, steps, extra, uses, start);
        var loaded = BrainProcessLoader.Load(text, new ProcessLoadContext(rules));
        return loaded.Process ?? throw new InvalidOperationException("The test document does not load: " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }

    internal static JsonObject Input(string json) => JsonNode.Parse(json)!.AsObject();

    private static BrainRule LoadRule(string file)
    {
        var loaded = BrainRuleLoader.Load(TestFiles.Read(TestFiles.Fixtures + file), [Contract.Value]);
        return loaded.Rule ?? throw new InvalidOperationException(file + ": " + string.Join("; ", loaded.Errors.Select(error => error.Code + "@" + error.Path)));
    }
}

/// <summary>Drives one instance: keeps the state and everything the interpreter asked for and wrote.</summary>
internal sealed class Driver(BrainProcess process)
{
    internal DateTimeOffset Now { get; set; } = Docs.T0;

    internal ProcessState State { get; private set; } = null!;

    internal AdvanceResult Last { get; private set; } = null!;

    internal List<ProcessRequest> Requests { get; } = [];

    internal List<ProcessHistoryEntry> History { get; } = [];

    internal AdvanceResult Start(string input = "{}", string id = "i-1")
    {
        Last = process.Start(id, Docs.Input(input), Docs.Clock(Now));
        return Record();
    }

    internal AdvanceResult Send(ProcessTrigger trigger)
    {
        Last = process.Advance(State, trigger, Docs.Clock(Now));
        return Record();
    }

    internal T Single<T>() where T : ProcessRequest => Last.Requests.OfType<T>().Single();

    internal IEnumerable<ProcessHistoryKind> Kinds => Last.History.Select(entry => entry.Kind);

    internal IEnumerable<string?> Trace => History.Where(entry => entry.Kind == ProcessHistoryKind.StepEntered).Select(entry => entry.Step);

    private AdvanceResult Record()
    {
        State = Last.State;
        Requests.AddRange(Last.Requests);
        History.AddRange(Last.History);
        return Last;
    }

    internal string Variable(string name) => State.Variables[name];

    internal static string Iso(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
