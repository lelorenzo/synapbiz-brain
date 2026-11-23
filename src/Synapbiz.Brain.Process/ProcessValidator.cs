using System.Globalization;
using System.Text.Json.Nodes;
using Nuvora.Decisions.Contracts.Kinds;
using Nuvora.Decisions.Contracts.Tables;
using Synapbiz.Brain.Spec;

namespace Synapbiz.Brain.Process;

/// <summary>The static rules of section 2.4 of the specification, applied to a document that passed the schema.</summary>
internal sealed class ProcessValidator(ProcessDefinition definition, ProcessLoadContext context, List<BrainSpecError> errors)
{
    private readonly Dictionary<string, BrainRule> rules = IndexRules(context.Rules);

    internal void Validate()
    {
        CheckReference(definition.Start, "/body/start");
        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in definition.Steps)
        {
            var path = JsonText.Pointer("/body/steps", step.Id);
            CheckStep(step, path, needed);
        }

        CheckUses(needed);
        CheckReachability();
        CheckCycles();
        new ScenarioValidator(definition, errors).Validate();
    }

    internal static Dictionary<string, BrainRule> IndexRules(IReadOnlyCollection<BrainRule> source)
    {
        var result = new Dictionary<string, BrainRule>(StringComparer.Ordinal);
        foreach (var rule in source)
        {
            result.TryAdd(rule.Id, rule);
        }

        return result;
    }

    private void CheckReference(string target, string path)
    {
        if (!definition.StepsById.ContainsKey(target))
        {
            errors.Add(Diagnostics.UnknownStep(path));
        }
    }

    private void CheckStep(StepDef step, string path, HashSet<string> needed)
    {
        if (step.Stage is not null && !definition.Stages.Contains(step.Stage, StringComparer.Ordinal))
        {
            errors.Add(Diagnostics.UnknownStage(path + "/stage"));
        }

        foreach (var reference in step.References())
        {
            CheckReference(reference.Target, path + reference.Member);
        }

        foreach (var (variable, member) in step.VariableUses())
        {
            if (!definition.Variables.ContainsKey(variable))
            {
                errors.Add(Diagnostics.UnknownVariable(path + member));
            }
        }

        switch (step)
        {
            case DecideStep decide:
                needed.Add("rule/" + decide.Rule);
                CheckDecide(decide, path);
                break;
            case SubprocessStep subprocess:
                needed.Add("process/" + subprocess.Process);
                CheckSubprocess(subprocess, path);
                break;
            case AiStep ai:
                CheckAi(ai, path);
                break;
            case HumanTaskStep task:
                CheckTask(task, path);
                break;
            case WaitStep wait:
                CheckVariableType(wait.Correlation, path + "/correlation", VariableType.Code, VariableType.Reference);
                break;
            case TimerStep timer:
                CheckVariableType(timer.Until, path + "/until", VariableType.Instant);
                break;
            case LoopStep loop:
                CheckVariableType(loop.ForEach, path + "/forEach", VariableType.Json);
                CheckVariableType(loop.Item, path + "/item", VariableType.Json);
                break;
            default:
                break;
        }
    }

    private void CheckVariableType(string? variable, string path, params VariableType[] allowed)
    {
        if (variable is not null && definition.Variables.TryGetValue(variable, out var declared) && !allowed.Contains(declared.Type))
        {
            errors.Add(Diagnostics.WrongVariableType(path));
        }
    }

    private void CheckSubprocess(SubprocessStep step, string path)
    {
        if (context.KnownProcessIds is { } known && !known.Contains(step.Process, StringComparer.Ordinal))
        {
            errors.Add(Diagnostics.UnavailableSubprocess(path + "/process"));
        }
    }

    private void CheckAi(AiStep step, string path)
    {
        if (definition.StepsById.TryGetValue(step.Next, out var next) && next.Kind != StepKind.Decide)
        {
            errors.Add(Diagnostics.AiNextNotDecision(path + "/next"));
        }
    }

    private void CheckTask(HumanTaskStep task, string path)
    {
        var routed = task.On.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in routed.Where(key => !task.Outcomes.Contains(key, StringComparer.Ordinal)))
        {
            errors.Add(Diagnostics.UnknownRouteValue(JsonText.Pointer(path + "/on", key)));
        }

        if (task.Outcomes.Any(outcome => !routed.Contains(outcome)))
        {
            errors.Add(Diagnostics.IncompleteRoute(path + "/on"));
        }
    }

    private void CheckDecide(DecideStep step, string path)
    {
        if (!rules.TryGetValue(step.Rule, out var rule))
        {
            errors.Add(Diagnostics.UnavailableRule(path + "/rule"));
            return;
        }

        var kind = rule.Kind;
        foreach (var (name, variable) in step.Facts.Entries)
        {
            var member = JsonText.Pointer(path + "/facts", name);
            var fact = kind.Facts.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (fact is null)
            {
                errors.Add(Diagnostics.UnknownFact(member));
            }
            else if (TypeDiffers(variable, fact.Type))
            {
                errors.Add(Diagnostics.WrongVariableType(member));
            }
        }

        foreach (var (name, variable) in step.Output.Entries)
        {
            var member = JsonText.Pointer(path + "/output", name);
            var field = FindOutcome(kind, name);
            if (field is null)
            {
                errors.Add(Diagnostics.UnknownOutcome(member));
            }
            else if (TypeDiffers(variable, field.Type))
            {
                errors.Add(Diagnostics.WrongVariableType(member));
            }
        }

        CheckHitPolicy(step, rule, path);
        CheckCollect(step, path);
        CheckRoute(step, rule, path);
    }

    private void CheckHitPolicy(DecideStep step, BrainRule rule, string path)
    {
        var matches = rule.Table.HitPolicy == HitPolicy.Collect
            ? step.CollectInto is not null && step.Next is not null && step.Output.IsEmpty && step.Route is null && step.On is null
            : step.CollectInto is null;
        if (!matches)
        {
            errors.Add(Diagnostics.WrongHitPolicy(path));
        }
    }

    private void CheckCollect(DecideStep step, string path)
    {
        if (step.CollectInto is not null)
        {
            CheckVariableType(step.CollectInto, path + "/collectInto", VariableType.Json);
        }
    }

    private void CheckRoute(DecideStep step, BrainRule rule, string path)
    {
        if (step.Route is null || step.On is null)
        {
            return;
        }

        var field = FindOutcome(rule.Kind, step.Route);
        if (field is null || field.Type is not (FactType.Enum or FactType.Boolean or FactType.Code))
        {
            errors.Add(Diagnostics.UnknownOutcome(path + "/route"));
            return;
        }

        var keys = step.On.Select(entry => entry.Key).ToList();
        if (field.Type == FactType.Boolean)
        {
            if (!keys.Contains("true", StringComparer.Ordinal) || !keys.Contains("false", StringComparer.Ordinal))
            {
                errors.Add(Diagnostics.IncompleteRoute(path + "/on"));
            }

            return;
        }

        var produced = ProducedValues(rule, step.Route);
        if (produced.Any(value => !keys.Contains(value, StringComparer.Ordinal)))
        {
            errors.Add(Diagnostics.IncompleteRoute(path + "/on"));
        }

        foreach (var key in keys.Where(key => !produced.Contains(key)))
        {
            errors.Add(Diagnostics.UnknownRouteValue(JsonText.Pointer(path + "/on", key)));
        }
    }

    // The values that the rows and the default row of the rule can produce for an outcome field: the domain of a route.
    private static HashSet<string> ProducedValues(BrainRule rule, string field)
    {
        var rows = rule.Table.DefaultRow is null ? rule.Table.Rows : [.. rule.Table.Rows, rule.Table.DefaultRow];
        return rows
            .Select(row => JsonText.Text(JsonText.ParseObject(row.Outcome)?[field]))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private bool TypeDiffers(string variable, FactType type) =>
        definition.Variables.TryGetValue(variable, out var declared) && VariableValues.FactTypeOf(declared.Type) != type;

    private static OutcomeFieldDescriptor? FindOutcome(DecisionKindDescriptor kind, string name) =>
        kind.OutcomeFields.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    private void CheckUses(HashSet<string> needed)
    {
        var listed = definition.Uses.Select(use => use.Reference).ToHashSet(StringComparer.Ordinal);
        foreach (var step in definition.Steps)
        {
            var path = JsonText.Pointer("/body/steps", step.Id);
            if (step is DecideStep decide && !listed.Contains("rule/" + decide.Rule))
            {
                errors.Add(Diagnostics.MissingUse(path + "/rule"));
            }
            else if (step is SubprocessStep subprocess && !listed.Contains("process/" + subprocess.Process))
            {
                errors.Add(Diagnostics.MissingUse(path + "/process"));
            }
        }

        foreach (var (_, index) in definition.Uses.Where(use => !needed.Contains(use.Reference)))
        {
            errors.Add(Diagnostics.UnusedUse(JsonText.Pointer("/uses", index)));
        }
    }

    private void CheckReachability()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        if (definition.StepsById.ContainsKey(definition.Start))
        {
            pending.Push(definition.Start);
            seen.Add(definition.Start);
        }

        while (pending.TryPop(out var id))
        {
            foreach (var reference in definition.StepsById[id].References().Where(reference => definition.StepsById.ContainsKey(reference.Target) && seen.Add(reference.Target)))
            {
                pending.Push(reference.Target);
            }
        }

        foreach (var step in definition.Steps.Where(step => !seen.Contains(step.Id)))
        {
            errors.Add(Diagnostics.UnreachableStep(JsonText.Pointer("/body/steps", step.Id)));
        }
    }

    // The graph without the body edges of loop steps must be acyclic: the body edge is the one bounded edge.
    private void CheckCycles()
    {
        var nodes = definition.Steps.Select(step => step.Id).ToList();
        var edges = definition.Steps.ToDictionary(
            step => step.Id,
            step => step.References()
                .Where(reference => !(step.Kind == StepKind.Loop && reference.Member == "/body"))
                .Select(reference => reference.Target)
                .Where(target => definition.StepsById.ContainsKey(target))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            StringComparer.Ordinal);
        foreach (var component in StronglyConnected(nodes, edges).Where(component => component.Count > 1 || edges[component[0]].Contains(component[0], StringComparer.Ordinal)))
        {
            var first = nodes.First(id => component.Contains(id, StringComparer.Ordinal));
            errors.Add(Diagnostics.UnboundedCycle(JsonText.Pointer("/body/steps", first)));
        }
    }

    /// <summary>The strongly connected components of a graph (Tarjan, iterative so that long chains cannot exhaust the stack).</summary>
    private static List<List<string>> StronglyConnected(List<string> nodes, Dictionary<string, List<string>> edges)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var result = new List<List<string>>();
        var counter = 0;

        foreach (var root in nodes.Where(node => !index.ContainsKey(node)))
        {
            var work = new Stack<(string Node, int Next)>();
            work.Push((root, 0));
            index[root] = low[root] = counter++;
            stack.Push(root);
            onStack.Add(root);
            while (work.Count > 0)
            {
                var (node, next) = work.Pop();
                if (next < edges[node].Count)
                {
                    work.Push((node, next + 1));
                    var target = edges[node][next];
                    if (!index.TryGetValue(target, out var targetIndex))
                    {
                        index[target] = low[target] = counter++;
                        stack.Push(target);
                        onStack.Add(target);
                        work.Push((target, 0));
                    }
                    else if (onStack.Contains(target))
                    {
                        low[node] = Math.Min(low[node], targetIndex);
                    }

                    continue;
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }

                if (low[node] == index[node])
                {
                    var component = new List<string>();
                    string member;
                    do
                    {
                        member = stack.Pop();
                        onStack.Remove(member);
                        component.Add(member);
                    }
                    while (!string.Equals(member, node, StringComparison.Ordinal));
                    result.Add(component);
                }
            }
        }

        return result;
    }
}

/// <summary>The checks of the scenarios embedded in a document that the schema cannot express.</summary>
internal sealed class ScenarioValidator(ProcessDefinition definition, List<BrainSpecError> errors)
{
    internal void Validate()
    {
        for (var index = 0; index < definition.Scenarios.Count; index++)
        {
            var scenario = definition.Scenarios[index]!.AsObject();
            var path = JsonText.Pointer("/body/scenarios", index);
            CheckStart(scenario["start"]!.AsObject(), path + "/start");
            var events = scenario["events"]!.AsArray();
            for (var position = 0; position < events.Count; position++)
            {
                CheckEvent(events[position]!.AsObject(), JsonText.Pointer(path + "/events", position));
            }

            CheckExpect(scenario["expect"]!.AsObject(), path + "/expect");
        }
    }

    private void CheckStart(JsonObject start, string path)
    {
        if (!VariableValues.TryInstant(JsonText.Text(start["at"])!, out _))
        {
            errors.Add(Diagnostics.BadScenario(path + "/at"));
        }

        if (!VariableValues.TryDate(JsonText.Text(start["today"])!, out _))
        {
            errors.Add(Diagnostics.BadScenario(path + "/today"));
        }

        if (start["input"] is JsonObject input)
        {
            foreach (var name in input.Select(member => member.Key).Where(name => !definition.Variables.ContainsKey(name)))
            {
                errors.Add(Diagnostics.BadScenario(JsonText.Pointer(path + "/input", name)));
            }
        }
    }

    private void CheckEvent(JsonObject item, string path)
    {
        foreach (var key in new[] { "complete", "fail" })
        {
            CheckStepOfKind(item, key, path, StepKind.Read, StepKind.Action, StepKind.Ai);
        }

        CheckStepOfKind(item, "task", path, StepKind.HumanTask);
        CheckStepOfKind(item, "child", path, StepKind.Subprocess);
        CheckStepOfKind(item, "childFailed", path, StepKind.Subprocess);
        CheckDuration(item, "advance", path);
        CheckDuration(item, "retryAfter", path);
        if (JsonText.Text(item["event"]) is { } name && !IsKnownEvent(name))
        {
            errors.Add(Diagnostics.BadScenario(path + "/event"));
        }

        if (JsonText.Text(item["task"]) is { } task && JsonText.Text(item["outcome"]) is { } outcome
            && definition.StepsById.TryGetValue(task, out var step) && step is HumanTaskStep human
            && !human.Outcomes.Contains(outcome, StringComparer.Ordinal))
        {
            errors.Add(Diagnostics.BadScenario(path + "/outcome"));
        }
    }

    private void CheckStepOfKind(JsonObject item, string key, string path, params StepKind[] kinds)
    {
        if (JsonText.Text(item[key]) is { } id && (!definition.StepsById.TryGetValue(id, out var step) || !kinds.Contains(step.Kind)))
        {
            errors.Add(Diagnostics.BadScenario(JsonText.Pointer(path, key)));
        }
    }

    private void CheckDuration(JsonObject item, string key, string path)
    {
        if (JsonText.Text(item[key]) is { } text && !Durations.TryParse(text, false, out _))
        {
            errors.Add(Diagnostics.BadDuration(JsonText.Pointer(path, key)));
        }
    }

    private bool IsKnownEvent(string name) =>
        definition.Accepts.Contains(name) || definition.Steps.OfType<WaitStep>().Any(step => string.Equals(step.Event, name, StringComparison.Ordinal));

    private void CheckExpect(JsonObject expect, string path)
    {
        if (expect["variables"] is JsonObject variables)
        {
            foreach (var name in variables.Select(member => member.Key).Where(name => !definition.Variables.ContainsKey(name)))
            {
                errors.Add(Diagnostics.BadScenario(JsonText.Pointer(path + "/variables", name)));
            }
        }

        if (expect["trace"] is JsonArray trace)
        {
            for (var position = 0; position < trace.Count; position++)
            {
                if (!definition.StepsById.ContainsKey(JsonText.Text(trace[position])!))
                {
                    errors.Add(Diagnostics.BadScenario(JsonText.Pointer(path + "/trace", position)));
                }
            }
        }
    }
}
