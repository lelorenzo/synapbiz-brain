using System.Reflection;
using System.Text.Json.Nodes;

namespace Synapbiz.Brain.Authoring;

/// <summary>One section of the guide for assistants.</summary>
/// <param name="Id">The id to ask for.</param>
/// <param name="Title">The title.</param>
/// <param name="Text">The text, in Markdown.</param>
public sealed record GuideSection(string Id, string Title, string Text);

/// <summary>
/// The format reference the assistant reads before it writes anything: the sections of the <c>brain.process@1</c> and <c>brain.rule@1</c> formats
/// that matter when modelling, what a good process has, the working method and two worked examples taken from the reference documents of the
/// repository. It is written for the assistant (in English); it tells the assistant to say "process" to people, never "flow".
/// </summary>
public static class BrainGuide
{
    /// <summary>The id of the section that holds the working method.</summary>
    public const string MethodId = "method";

    /// <summary>The id of the section that holds the worked examples.</summary>
    public const string ExamplesId = "examples";

    /// <summary>The step kinds the guide documents: exactly the kinds of the format.</summary>
    public static IReadOnlyList<string> StepKinds { get; } = ["read", "action", "ai", "poll", "decide", "humanTask", "wait", "timer", "loop", "subprocess", "end"];

    /// <summary>
    /// The members and values of the format the guide names, each of which the reference of the format (<c>docs/reference/brain-process-v1.md</c>) also names,
    /// so that the guide cannot describe a thing the format does not have.
    /// </summary>
    public static IReadOnlyList<string> Concepts { get; } =
    [
        "atLeastOnce", "atMostOnce", "backoff", "businessKey", "collectInto", "contact", "correlation", "cron", "display", "forEach", "interruptBy",
        "limits", "maxAttempts", "maxDuration", "maxIterations", "misfire", "onError", "onExhausted", "onExpired", "onInvalid", "onNoDecision",
        "onNotAvailable", "onNotEntitled", "onTimeout", "onUnknown", "outcomes", "outputSchema", "inputSchema", "retry", "scenarios", "schedule",
        "stages", "subject", "timeout", "triggers", "until", "uses", "variables", "windows", "zone",
    ];

    /// <summary>The sections, in reading order.</summary>
    public static IReadOnlyList<GuideSection> Sections { get; } =
    [
        new("overview", "What a process document is", Overview),
        new("steps", "Step kinds", Steps),
        new("routes", "Routes: where a case goes next", Routes),
        new("timeouts", "Waiting, timeouts and delivery windows", Timeouts),
        new("loops", "Loops and polling", Loops),
        new("triggers", "Triggers: what starts a process", Triggers),
        new("scenarios", "Scenarios: tests inside the document", Scenarios),
        new("labels", "Labels people read", Labels),
        new("good", "What a good process has", Good),
        new("rules", "Rules: brain.rule@1 and rule_draft_edit", RulesText),
        new(MethodId, "Working method", Method),
        new(ExamplesId, "Two worked examples", Examples),
    ];

    /// <summary>The section with the id; null when there is none.</summary>
    /// <param name="id">The id.</param>
    public static GuideSection? Find(string id) => Sections.FirstOrDefault(section => string.Equals(section.Id, id, StringComparison.Ordinal));

    /// <summary>The text of the working method, which the prompt "model a process with me" also carries.</summary>
    public static string WorkingMethod => Find(MethodId)!.Text;

    /// <summary>The two worked examples as documents: the id, a title and the text of each document.</summary>
    public static IReadOnlyList<(string Name, string Title, string Text)> ExampleDocuments { get; } =
    [
        ("practice.daily_refresh", "A scheduled process that reads the day's case list every morning and hands a failure to a person", Resource("Guide.daily_refresh.process.json")),
        ("visit.note_wait", "A process that polls a port until a note is signed, then hands over to a person; and the rule it uses", Resource("Guide.note_wait.process.json")),
        ("visit.note_ready", "The rule that process uses", Resource("Guide.note_wait.rule.json")),
    ];

    private static string Resource(string name)
    {
        using var stream = typeof(BrainGuide).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("The guide resource " + name + " is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The guide as JSON: the list of sections, or one section, or the examples with their documents.</summary>
    /// <param name="section">The id of a section; null for the list.</param>
    internal static JsonObject ToJson(string? section)
    {
        if (section is null)
        {
            return new JsonObject
            {
                ["sections"] = new JsonArray([.. Sections.Select(item => (JsonNode?)new JsonObject { ["id"] = item.Id, ["title"] = item.Title })]),
                ["method"] = WorkingMethod,
            };
        }

        var found = Find(section)!;
        var node = new JsonObject { ["id"] = found.Id, ["title"] = found.Title, ["text"] = found.Text };
        if (found.Id == ExamplesId)
        {
            node["examples"] = new JsonArray([.. ExampleDocuments.Select(example => (JsonNode?)new JsonObject { ["name"] = example.Name, ["title"] = example.Title, ["document"] = AuthoringJson.TryParse(example.Text) })]);
        }

        return node;
    }

    private const string Overview = """
        A **process** is a graph of steps over typed variables that a host runs one case at a time. To a person you always say "process" (in Spanish "proceso"), never "flow"; tool names such as `flow_get` keep that word only because they are names.

        A `brain.process@1` document has these members, all required: `$spec` (`brain.process@1`), `id`, `version` (an integer; a changed process needs a higher version), `uses` (the rules and subprocesses it names, as `{"$ref": "rule/<id>"}` or `{"$ref": "process/<id>"}`), `requires` (module contracts such as `visit@1`), `meta` (a free object; the labels people read are under `meta.display`) and `body`.

        The `body` has `subject` (the kind of case), `stages` (names that group steps, for example `visit`), `variables` (name to `{type, input?, initial?, allowedValues?}`; types Code, Enum, Boolean, Integer, Decimal, Money, Date, Reference, Instant, Json), `start` (the first step), `steps`, and optionally `accepts`, `limits`, `triggers`, `windows` and `scenarios`.

        The server never activates anything. Everything you write is a draft until a person applies a proposal elsewhere.
        """;

    private const string Steps = """
        Every step has a `kind` and may have a `stage`. The kinds:

        - `read`: asks a port for data. Members: `port`, `input` (port parameter to variable), `output` (answer member to variable), `retry`, `next`, and the error routes.
        - `action`: asks a port to do something. As `read`, plus `delivery`: `atLeastOnce` or `atMostOnce`. `atMostOnce` needs `onUnknown` (the call may or may not have happened; send it to a person). `contact: true` marks a step that reaches a person; it needs a `window`.
        - `ai`: asks a model through a port. Needs `model`, `prompt` (`{id, version}`), `inputSchema`, `outputSchema`, `maxAttempts`, `onInvalid` and a `next` that is a `decide` step: an AI answer never moves a case by itself, a rule decides. Always give a route that works without AI.
        - `poll`: reads a port again and again, evaluates a rule on the answer and stops when the rule's `field` takes one of the `until` values. Members: `port`, `rule`, `facts`, `field`, `until`, `every`, `backoff`, `max`, `maxDuration`, `next`, `onExpired`.
        - `decide`: evaluates a rule. Members: `rule`, `facts` (fact name to variable), `output`, then either `next`, or `route` (an outcome field) with `on` (value to step), or `collectInto` for a collecting rule; and `onNoDecision`.
        - `humanTask`: a task for a person in a `queue` with `outcomes`, `show` (variables to display), `on` (outcome to step), and optionally `timeout` with `onTimeout`.
        - `wait`: waits for an `event` (optionally with `correlation`), with `next` and optionally `timeout` with `onTimeout`.
        - `timer`: waits `after` a duration, or `until` an Instant variable with an `offset`; then `next`.
        - `loop`: a bounded gate: `maxIterations`, `body` (the only edge that may go back), `onExhausted`; with `forEach` over a Json list and `item`.
        - `subprocess`: starts another process as a child with mapped `input`; `next` or `on` (child result to step), `onError`.
        - `end`: ends the case with a `result`.

        A duration is ISO 8601 such as `PT30S`, `PT24H` or `P3D`. `retry` is `{maxAttempts, backoff: fixed|exponential, initial, max?}`.
        """;

    private const string Routes = """
        Where a case goes is written in the step. A finished step goes to `next`, or to `on[value]`. When a call fails and is not retried: `onNotAvailable` (the port is not available to this practice), `onNotEntitled`, `onUnknown` (an `atMostOnce` call whose result is not known), else `onError`; with no route the case is suspended for an operator. An `ai` answer that stays invalid goes to `onInvalid`. A rule that decides nothing goes to `onNoDecision`. A wait or task that times out goes to `onTimeout`. A loop that is used up goes to `onExhausted`. A poll whose time is up goes to `onExpired`.

        Every step must be reachable from `start`. The graph without the `body` edges of loops must have no cycle: that is how every case is bounded.
        """;

    private const string Timeouts = """
        A `humanTask` or a `wait` may carry `timeout` (a duration, or `{until: <Instant variable>, offset?}` to leave by an instant) and then must carry `onTimeout`. A wait with no timeout waits for ever: say why in the draft's known gaps, or add one.

        A delivery window is declared under `body.windows` by name: `{zone, days, from, to}` where `zone` is an IANA id or `{variable: <Code variable>}`, `days` are `mon` to `sun`, and `from` and `to` are `HH:mm`. An `action` that names a `window` is requested only when the window is open. An action may also name `interruptBy` (an event that ends the wait for the window) with its `to` step.
        """;

    private const string Loops = """
        A `loop` is the only way back in the graph and it is bounded by `maxIterations`. `forEach` walks a Json list held in a variable, one item per round, then goes to `next`. A `poll` is bounded by `maxDuration`; with `backoff: exponential` the interval grows up to `max`. The `limits.maxSteps` member is a second guard.
        """;

    private const string Triggers = """
        `triggers` says what starts a case. An event trigger: `{kind: "event", event, businessKey, input?}`. A schedule trigger: `{kind: "schedule", id, cron, zone?, misfire: "skip"|"fireOnce", input?}` with a five-field cron expression. The library validates them; the host does the starting.
        """;

    private const string Scenarios = """
        A scenario is a test stored in `body.scenarios`: `{name, start: {at, today, input}, events: [...], expect: {status, result?, trace?, variables?}}`. Events answer what the case waits for: `{complete: <step>, output: {...}}`, `{fail: <step>, error, code}`, `{advance: "PT1H"}`, `{event, correlation?, payload?}`, `{task: <step>, outcome, data?}`, `{child: <step>, result, output?}`, `{childFailed: <step>, code}`, `{cancel}`, `{resume: true}`. `trace` is the exact ordered list of steps entered.

        You rarely write a scenario by hand: simulate the case with `flow_draft_simulate`, then call `flow_draft_add_scenario` with `fromSimulation`. Give each test a name that says the situation in plain words. Never put a real patient's name or data in a scenario.
        """;

    private const string Labels = """
        People read labels, not ids. Under `meta.display.en` and `meta.display.es` give: `title`, and groups of id to text for `stages`, `steps`, `results`, `queues`, `outcomes`, `events` and `windows`. Every step and stage needs a label in both languages before a draft is ready.
        """;

    private const string Good = """
        A draft is ready to propose only when Brain says so (`ready: true`): the loader accepts it, every scenario passes, every step and stage has labels in English and Spanish, every route is taken by some scenario (or listed in `knownGaps` with a reason), every step that contacts a person is `atMostOnce`, sits behind a delivery window and has routes for a failure and for an unknown result, every `ai` step has a route that needs no AI, every wait has a timeout (or a known gap), and nothing named is outside the catalog (`brain_catalog` lists what is available). You cannot mark a draft ready yourself; Brain computes it.
        """;

    private const string RulesText = """
        A rule is a `brain.rule@1` document: a decision table over the facts of a kind. Do not write one by hand: read it with `rule_get`, change it with `rule_draft_edit` (set or clear a condition, set an outcome, add, remove or move a row, set the default outcome, change or add a test case, name a row), then read the answer: rows as sentences, what changed, how the test cases now decide, and the test results. `rule_simulate` evaluates made-up facts on the real engine. A rule proposal that stays within the facts the rule reads and the outcomes it gives does not need review by our team; any other does.
        """;

    private const string Method = """
        1. Read before writing: call `brain_guide`, `brain_catalog`, `flow_list` and `flow_get` (or `rule_get`) for what exists.
        2. Restate the request in one sentence and ask the person about anything ambiguous before you change a thing.
        3. Change the smallest thing that does what was asked.
        4. Save the draft and read Brain's answer; fix every error before anything else, using each error's hint.
        5. Add or adjust a scenario for each new behaviour.
        6. Simulate the case the person described and tell them, in their language, what the process did. Use their words: process, not flow.
        7. Propose only when Brain says the draft is ready.
        8. Never claim a change is live: a proposal changes nothing until a person applies it elsewhere, and a process proposal is reviewed by our team first.
        Work with definitions and made-up cases only; never type or ask for anything that identifies a patient.
        """;

    private const string Examples = """
        The two examples are the reference documents of the repository, so they always load. `practice.daily_refresh` is a scheduled process (a `schedule` trigger) that reads a list into a variable and sends a failed read to a person. `visit.note_wait` polls a port with exponential backoff until a rule says a note is signed, then asks a person in a queue when two days pass; its rule `visit.note_ready` is a small unique-hit table with test cases. Read them as patterns for steps, routes, labels in both languages and scenarios.
        """;
}
