using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Workbench;
using Synapbiz.Brain.Workbench.Flows;

namespace Synapbiz.Brain.Authoring;

/// <summary>The simulation of a made-up case on a draft: start it, send commands, read where it is.</summary>
internal static class SimulationTools
{
    private static readonly string[] CommandTypes = ["complete", "fail", "task", "event", "advance", "child", "childFail", "cancel", "resume"];

    internal static IEnumerable<AuthoringTool> Create(ToolContext context)
    {
        yield return new AuthoringTool(
            "flow_draft_simulate",
            "Plays a made-up case on a draft. Start one with `id` and `start` (an instant, the input values, all invented), then send `command`s with the returned `session`: complete an effect with an output, fail it, complete a person's task with an outcome, send an event, let time pass, finish a child, cancel or resume. "
            + "It answers where the case is, what it waits for and what happened, in words and as data. Sessions live in memory and expire. Real cases, real patients and live ports are never reached: every answer is typed by you or the person. "
            + ToolSchemas.NoPatientText,
            ToolSchemas.Object(
                [
                    ("id", ToolSchemas.Id("The draft to run the case on; needed with `start`.")),
                    ("start", new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "Starts a new case.",
                        ["properties"] = new JsonObject
                        {
                            ["at"] = ToolSchemas.Text("The instant the case starts, ISO 8601 with offset, for example 2027-01-04T14:00:00Z. Default: now on the host clock.", 40),
                            ["today"] = ToolSchemas.Text("The calendar date of the case, yyyy-MM-dd. Default: the date of `at`.", 10),
                            ["input"] = ToolSchemas.AnyObject("The input values of the process, made up. " + ToolSchemas.NoPatientText),
                            ["zones"] = ToolSchemas.AnyObject("Time zone ids the process uses mapped to a fixed offset like +01:00."),
                        },
                        ["additionalProperties"] = false,
                    }),
                    ("session", ToolSchemas.Text("The token of a running simulation.", 40)),
                    ("command", new JsonObject
                    {
                        ["type"] = "object",
                        ["description"] = "What happens next to the case.",
                        ["properties"] = new JsonObject
                        {
                            ["type"] = ToolSchemas.Enum("complete: an effect answered with `output`; fail: an effect failed with `code`; task: a person finished a task with `outcome`; event: an event arrived; advance: time passes by `duration`; child: a subprocess finished with `result`; childFail: it failed; cancel; resume.", CommandTypes),
                            ["effect"] = ToolSchemas.Text("For complete and fail: the effect id from `outstanding`, or the step id.", 200),
                            ["output"] = ToolSchemas.AnyObject("For complete: the output object of the port, made up."),
                            ["code"] = ToolSchemas.Text("For fail and childFail: a failure code, for example not_available.", 100),
                            ["permanent"] = ToolSchemas.Flag("For fail: true when the failure is permanent; default false."),
                            ["task"] = ToolSchemas.Text("For task: the task id from `outstanding`, or the step id.", 200),
                            ["outcome"] = ToolSchemas.Text("For task: the outcome the person chose.", 200),
                            ["data"] = ToolSchemas.AnyObject("For task: data the person entered, made up."),
                            ["name"] = ToolSchemas.Text("For event: the event name.", 200),
                            ["correlation"] = ToolSchemas.Text("For event: the correlation value, when the wait uses one.", 200),
                            ["payload"] = ToolSchemas.AnyObject("For event: the event data, made up."),
                            ["duration"] = ToolSchemas.Text("For advance: an ISO 8601 duration such as PT1H or P2D.", 40),
                            ["child"] = ToolSchemas.Text("For child and childFail: the child key from `outstanding`, or the step id.", 200),
                            ["result"] = ToolSchemas.Text("For child: the result the subprocess ended with.", 200),
                        },
                        ["required"] = AuthoringJson.Array(["type"]),
                        ["additionalProperties"] = false,
                    }),
                    ("close", ToolSchemas.Flag("True to end the session and free it.")),
                ]),
            context,
            Simulate);
    }

    private static JsonObject Simulate(ToolContext context, JsonObject input, WorkbenchLocale locale)
    {
        if (input["close"]?.GetValue<bool>() == true)
        {
            var token = AuthoringJson.Text(input["session"]);
            if (token is null)
            {
                return Results.Refuse(locale, "simulation.target", [], "Give the `session` to close.");
            }

            var removed = context.Sessions.Remove(token);
            return Results.Ok(AuthoringText.Text(locale, "say.sim.closed"), new JsonObject { ["closed"] = removed });
        }

        var started = input["start"] as JsonObject;
        var session = AuthoringJson.Text(input["session"]);
        if ((started is null) == (session is null))
        {
            return Results.Refuse(locale, "simulation.target", [], "Give `id` with `start` to begin a case, or `session` to continue one, not both.");
        }

        if (started is not null)
        {
            return Start(context, input, started, locale);
        }

        var live = context.Sessions.Touch(session!);
        if (live is null)
        {
            return Results.Refuse(locale, "simulation.not_found", [], "No simulation with this token is alive (it may have expired or been closed). Start a new one.");
        }

        var simulation = live.Simulation;
        IReadOnlyList<FlowSimulationEntry> entries = [];
        if (input["command"] is JsonObject command)
        {
            var run = Run(simulation, command, locale, out var refusal);
            if (refusal is not null)
            {
                return refusal;
            }

            entries = run;
        }

        return State(live, entries, locale);
    }

    private static JsonObject Start(ToolContext context, JsonObject input, JsonObject start, WorkbenchLocale locale)
    {
        var id = AuthoringJson.Text(input["id"]);
        if (id is null)
        {
            return Results.Refuse(locale, "simulation.target", [], "Give the draft `id` together with `start`.");
        }

        var catalog = context.Catalog(locale, out var refusal);
        if (catalog is null)
        {
            return refusal!;
        }

        var draft = context.Store.ReadDraft(DocumentKind.Flow, id);
        if (draft is null)
        {
            return Results.Refuse(locale, "draft.not_found", [("id", id)], "There is no draft with this id. Save one with flow_draft_save first.");
        }

        var loaded = BrainProcessLoader.Load(draft.Text, context.FlowContext(catalog, id));
        if (loaded.Process is not { } process)
        {
            return Results.Refuse(locale, "simulation.draft_not_loaded", [("id", id)], "The draft does not load, so no case can run on it. Fix the loader errors first.");
        }

        FlowSimulation simulation;
        FlowSimulationSetup setup;
        try
        {
            setup = Setup(context, catalog, start);
            simulation = FlowSimulation.Start(process, setup);
        }
        catch (AuthoringProblemException problem)
        {
            return Results.Refuse(locale, "simulation.start_invalid", [("what", AuthoringText.Text(locale, "start." + problem.Code, [.. problem.Values]))], "The start could not be read: " + problem.Code + ".", new JsonObject { ["detail"] = problem.Code });
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            return Results.Refuse(locale, "simulation.start_invalid", [("what", AuthoringText.Text(locale, "start.other"))], "The start could not be read: " + error.Message, new JsonObject { ["detail"] = error.Message });
        }

        if (!simulation.Started)
        {
            return Results.Refuse(locale, "simulation.start_invalid", [("what", NotStarted(process, setup, simulation.StartCode, locale))], "The interpreter rejected the start with " + simulation.StartCode + ".", new JsonObject { ["detail"] = simulation.StartCode });
        }

        var stored = context.Sessions.Add(id, AuthoringJson.Hash(DocumentKind.Flow, FlowAnalyzer.FormatOrText(draft.Text)), simulation);
        if (stored is null)
        {
            return Results.Refuse(locale, "simulation.limit", [("max", AuthoringText.Number(context.Options.MaxSimulations))], "Too many simulations are alive. Close one with `close`, or wait for them to expire.");
        }

        return State(stored, simulation.Log, locale);
    }

    private static FlowSimulationSetup Setup(ToolContext context, AuthoringCatalog catalog, JsonObject start)
    {
        DateTimeOffset at;
        if (AuthoringJson.Text(start["at"]) is { } text)
        {
            at = DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : throw new AuthoringProblemException("at", null, "at");
        }
        else
        {
            at = context.Options.Clock.GetUtcNow();
        }

        DateOnly today;
        if (AuthoringJson.Text(start["today"]) is { } day)
        {
            today = DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDay) ? parsedDay : throw new AuthoringProblemException("today", null, "today");
        }
        else
        {
            today = FlowSimulationSetup.TodayOf(at);
        }

        var zones = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);
        if (start["zones"] is JsonObject offsets)
        {
            foreach (var (zone, offset) in offsets)
            {
                var written = AuthoringJson.Text(offset) ?? string.Empty;
                var digits = written.TrimStart('+', '-');
                if (!TimeSpan.TryParseExact(digits, @"hh\:mm", CultureInfo.InvariantCulture, TimeSpanStyles.None, out var span) || written.Length - digits.Length > 1)
                {
                    throw new AuthoringProblemException("zone", null, "zone", ("zone", FlowChangeDescriber.Quote(zone)));
                }

                zones[zone] = span * (written.StartsWith('-') ? -1 : 1);
            }
        }

        return new FlowSimulationSetup
        {
            At = at,
            Today = today,
            InputJson = start["input"] is JsonObject input ? AuthoringJson.CompactText(input) : null,
            Zones = zones,
            ChildProcesses = [.. context.ApprovedFlows(catalog).Select(item => item.Process).OfType<BrainProcess>()],
        };
    }

    internal static string VarTypeKey(string type) => type switch
    {
        "Code" => "vartype.code",
        "Enum" => "vartype.enum",
        "Boolean" => "vartype.boolean",
        "Integer" => "vartype.integer",
        "Decimal" => "vartype.decimal",
        "Money" => "vartype.money",
        "Date" => "vartype.date",
        "Reference" => "vartype.reference",
        "Instant" => "vartype.instant",
        _ => "vartype.json",
    };

    // Which input made the interpreter refuse to start a case: a value for something the process does not take, or a value of the wrong kind.
    private static string NotStarted(BrainProcess process, FlowSimulationSetup setup, string? code, WorkbenchLocale locale)
    {
        var inputs = ProcessOutline.From(process).Variables.Where(variable => variable.IsInput).ToList();
        var given = (AuthoringJson.TryObject(setup.InputJson) ?? []).ToList();
        var extra = given.Where(pair => inputs.All(variable => variable.Name != pair.Key)).Select(pair => FlowChangeDescriber.Quote(WorkbenchText.Humanize(pair.Key))).ToList();
        var allowed = inputs.Count == 0
            ? AuthoringText.Text(locale, "start.noinputs")
            : AuthoringText.Join(locale, [.. inputs.Select(variable => FlowChangeDescriber.Quote(WorkbenchText.Humanize(variable.Name)))]);
        if (extra.Count > 0)
        {
            return AuthoringText.Text(locale, "start.extra", ("names", AuthoringText.Join(locale, extra)), ("allowed", allowed));
        }

        foreach (var (name, value) in given)
        {
            var alone = setup with { InputJson = new JsonObject { [name] = value?.DeepClone() }.ToJsonString() };
            if (!FlowSimulation.Start(process, alone).Started)
            {
                var variable = inputs.First(item => item.Name == name);
                return AuthoringText.Text(locale, "start.type", ("name", FlowChangeDescriber.Quote(WorkbenchText.Humanize(name))), ("kind", AuthoringText.Text(locale, VarTypeKey(variable.Type))));
            }
        }

        return PersonWords.Runtime(locale, code, null);
    }

    private static IReadOnlyList<FlowSimulationEntry> Run(FlowSimulation simulation, JsonObject command, WorkbenchLocale locale, out JsonObject? refusal)
    {
        refusal = null;
        var type = AuthoringJson.Text(command["type"])!;
        try
        {
            switch (type)
            {
                case "complete":
                    return simulation.CompleteEffect(Resolve<OutstandingEffect>(simulation, command["effect"]), Object(command["output"], "output"));
                case "fail":
                    return simulation.FailEffect(Resolve<OutstandingEffect>(simulation, command["effect"]), Required(command, "code"), command["permanent"]?.GetValue<bool>() == true);
                case "task":
                    return simulation.CompleteTask(Resolve<OutstandingTask>(simulation, command["task"]), Required(command, "outcome"), command["data"] is JsonObject data ? AuthoringJson.CompactText(data) : null);
                case "event":
                    return simulation.SendEvent(Required(command, "name"), AuthoringJson.Text(command["correlation"]), command["payload"] is JsonObject payload ? AuthoringJson.CompactText(payload) : null);
                case "advance":
                    return simulation.AdvanceBy(Duration(Required(command, "duration")));
                case "child":
                    return simulation.CompleteChild(Resolve<OutstandingChild>(simulation, command["child"]), Required(command, "result"));
                case "childFail":
                    return simulation.FailChild(Resolve<OutstandingChild>(simulation, command["child"]), Required(command, "code"));
                case "cancel":
                    return simulation.Cancel();
                default:
                    return simulation.Resume();
            }
        }
        catch (AuthoringProblemException problem)
        {
            refusal = Results.Refuse(locale, "simulation.command_failed", [("what", AuthoringText.Text(locale, "command." + problem.Code, problem.Values.Select(value => (value.Item1, value.Item1 == "waits" ? Waiting(simulation, locale) : value.Item2)).ToArray()))], "The command could not be carried out (" + problem.Code + ").", new JsonObject { ["detail"] = problem.Code });
            return [];
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            var code = error is FormatException or OverflowException ? "duration" : "other";
            refusal = Results.Refuse(locale, "simulation.command_failed", [("what", AuthoringText.Text(locale, "command." + code))], "The command could not be carried out: " + error.Message, new JsonObject { ["detail"] = error.Message });
            return [];
        }
    }

    private static TimeSpan Duration(string text)
    {
        try
        {
            return XmlConvert.ToTimeSpan(text);
        }
        catch (Exception error) when (error is FormatException or OverflowException)
        {
            throw new AuthoringProblemException("duration", null, "duration");
        }
    }

    private static string Resolve<T>(FlowSimulation simulation, JsonNode? reference)
        where T : FlowOutstanding
    {
        var wanted = AuthoringJson.Text(reference);
        if (wanted is null)
        {
            throw new AuthoringProblemException("needtarget", null, "needtarget");
        }

        var matches = simulation.Outstanding.OfType<T>().Where(item => item.Id == wanted || item.Step == wanted).ToList();
        if (matches.Count == 1)
        {
            return matches[0].Id;
        }

        if (matches.Count > 1)
        {
            throw new AuthoringProblemException("ambiguous", null, "ambiguous");
        }

        throw new AuthoringProblemException("notwaiting", null, "notwaiting", ("waits", string.Empty));
    }

    // What the case waits for, in words.
    private static string Waiting(FlowSimulation simulation, WorkbenchLocale locale)
    {
        var words = FlowWords.For(simulation.Process, locale);
        var waits = simulation.Outstanding.Select(item => Wait(item, words, locale)).ToList();
        return waits.Count == 0 ? AuthoringText.Text(locale, "say.sim.nothing") : AuthoringText.Join(locale, waits);
    }

    private static string Object(JsonNode? node, string name)
    {
        return node is JsonObject value ? AuthoringJson.CompactText(value) : throw new AuthoringProblemException("needobject", null, "needobject", ("member", FlowChangeDescriber.Quote(name)));
    }

    private static string Required(JsonObject command, string name)
    {
        return AuthoringJson.Text(command[name]) is { Length: > 0 } text ? text : throw new AuthoringProblemException("needmember", null, "needmember", ("member", FlowChangeDescriber.Quote(name)));
    }

    private static JsonObject State(SimulationSession session, IReadOnlyList<FlowSimulationEntry> entries, WorkbenchLocale locale)
    {
        var simulation = session.Simulation;
        var words = FlowWords.For(simulation.Process, locale);
        var happened = new JsonArray();
        var sentences = new List<string>();
        foreach (var entry in entries)
        {
            foreach (var step in entry.EnteredSteps)
            {
                var sentence = AuthoringText.Text(locale, "say.sim.entered", ("step", FlowChangeDescriber.Quote(words.Step(step))));
                sentences.Add(sentence);
                happened.Add(new JsonObject { ["kind"] = "entered", ["step"] = step, ["say"] = sentence });
            }

            if (entry.Disposition != AdvanceDisposition.Applied)
            {
                var sentence = AuthoringText.Text(locale, entry.Disposition == AdvanceDisposition.Ignored ? "say.sim.ignored" : "say.sim.refused");
                if (entry.Disposition != AdvanceDisposition.Ignored)
                {
                    sentence += " " + PersonWords.Runtime(locale, entry.Code, simulation.CurrentStep is { } here ? words.Step(here) : null);
                }

                sentences.Add(sentence);
                happened.Add(new JsonObject { ["kind"] = entry.Disposition == AdvanceDisposition.Ignored ? "ignored" : "refused", ["code"] = entry.Code, ["hint"] = entry.Code is null ? null : FixHints.For(entry.Code), ["say"] = sentence });
            }
        }

        var waits = simulation.Outstanding.Select(item => Wait(item, words, locale)).ToList();
        var outstanding = new JsonArray([.. simulation.Outstanding.Select((item, index) => (JsonNode?)Outstanding(item, waits[index]))]);
        var status = simulation.Status;
        var where = simulation.CurrentStep is { } current ? FlowChangeDescriber.Quote(words.Step(current)) : string.Empty;
        var resultWords = simulation.State.Result is { } ended ? FlowChangeDescriber.Quote(words.Result(ended)) : string.Empty;
        var waitWords = waits.Count == 0 ? AuthoringText.Text(locale, "say.sim.nothing") : AuthoringText.Join(locale, waits);
        var finish = status switch
        {
            ProcessStatus.Completed => AuthoringText.Text(locale, "say.sim.completed", ("result", resultWords)),
            ProcessStatus.Cancelled => AuthoringText.Text(locale, "say.sim.cancelled"),
            ProcessStatus.Suspended => AuthoringText.Text(locale, "say.sim.suspended", ("step", where)),
            _ => AuthoringText.Text(locale, "say.sim.waiting", ("step", where), ("waits", waitWords)),
        };

        var data = new JsonObject
        {
            ["session"] = session.Token,
            ["status"] = status.ToString(),
            ["currentStep"] = simulation.CurrentStep,
            ["result"] = simulation.State.Result,
            ["now"] = simulation.Now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["started"] = simulation.Started,
            ["happened"] = happened,
            ["outstanding"] = outstanding,
            ["variables"] = new JsonObject(simulation.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, AuthoringJson.TryParse(pair.Value)))),
            ["trace"] = Views.Strings(simulation.EnteredSteps),
        };
        var say = sentences.Count == 0 ? finish : string.Join(" ", sentences) + " " + finish;
        return Results.Ok(say, data);
    }

    private static JsonObject Outstanding(FlowOutstanding item, string wait)
    {
        var node = new JsonObject { ["id"] = item.Id, ["step"] = item.Step, ["say"] = wait };
        switch (item)
        {
            case OutstandingEffect effect:
                node["kind"] = "effect";
                node["port"] = effect.Port;
                node["input"] = AuthoringJson.TryParse(effect.InputJson);
                node["attempt"] = effect.Attempt;
                break;
            case OutstandingTask task:
                node["kind"] = "task";
                node["queue"] = task.Queue;
                node["outcomes"] = Views.Strings(task.Outcomes);
                break;
            case OutstandingWait wait2:
                node["kind"] = "event";
                node["event"] = wait2.Event;
                node["correlation"] = wait2.Correlation;
                break;
            case OutstandingTimer timer:
                node["kind"] = "timer";
                node["due"] = timer.Due.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                break;
            case OutstandingWindow window:
                node["kind"] = "window";
                node["opensAt"] = window.OpensAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                break;
            case OutstandingChild child:
                node["kind"] = "child";
                node["process"] = child.ProcessId;
                break;
            default:
                break;
        }

        return node;
    }

    private static string Wait(FlowOutstanding item, FlowWords words, WorkbenchLocale locale)
    {
        var step = FlowChangeDescriber.Quote(words.Step(item.Step));
        return item switch
        {
            OutstandingEffect effect => AuthoringText.Text(locale, "say.sim.out.effect", ("step", step), ("port", FlowChangeDescriber.Quote(effect.Port))),
            OutstandingTask task => AuthoringText.Text(locale, "say.sim.out.task", ("queue", FlowChangeDescriber.Quote(words.Queue(task.Queue))), ("outcomes", AuthoringText.Join(locale, [.. task.Outcomes.Select(outcome => FlowChangeDescriber.Quote(words.Outcome(outcome)))], "join.or"))),
            OutstandingWait wait => AuthoringText.Text(locale, "say.sim.out.event", ("event", FlowChangeDescriber.Quote(words.Event(wait.Event)))),
            OutstandingTimer timer => AuthoringText.Text(locale, "say.sim.out.timer", ("due", timer.Due.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))),
            OutstandingWindow window => AuthoringText.Text(locale, "say.sim.out.window", ("at", window.OpensAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))),
            OutstandingChild child => AuthoringText.Text(locale, "say.sim.out.child", ("process", FlowChangeDescriber.Quote(child.ProcessId))),
            _ => string.Empty,
        };
    }
}
