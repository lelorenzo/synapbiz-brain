using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;
using Synapbiz.Brain.Spec;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>Settings of a set of tools that a host may change; every member has a working default.</summary>
public sealed record AuthoringOptions
{
    /// <summary>The clock for session expiry and for the time a proposal carries. The library never reads the machine clock itself.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How long a simulation lives without being used.</summary>
    public TimeSpan SimulationLifetime { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How many simulations may live at once.</summary>
    public int MaxSimulations { get; init; } = 20;

    /// <summary>The largest document text a tool accepts, in characters.</summary>
    public int MaxDocumentLength { get; init; } = 1_000_000;
}

/// <summary>An approved or draft rule as loaded: its text, the rule and the loader errors.</summary>
/// <param name="Id">The id.</param>
/// <param name="Text">The document text.</param>
/// <param name="Rule">The loaded rule; null when the loader refused the document.</param>
/// <param name="Errors">The loader errors.</param>
internal sealed record LoadedRule(string Id, string Text, BrainRule? Rule, IReadOnlyList<BrainSpecError> Errors);

/// <summary>An approved flow as loaded.</summary>
/// <param name="Id">The id.</param>
/// <param name="Text">The document text.</param>
/// <param name="Process">The loaded process; null when the loader refused the document.</param>
/// <param name="Errors">The loader errors.</param>
internal sealed record LoadedFlow(string Id, string Text, BrainProcess? Process, IReadOnlyList<BrainSpecError> Errors);

/// <summary>What every tool of one set shares: the workspace, the options and the simulation sessions.</summary>
internal sealed class ToolContext
{
    internal ToolContext(IAuthoringStore store, AuthoringOptions options)
    {
        Store = store;
        Options = options;
        Sessions = new SimulationSessions(options.Clock, options.SimulationLifetime, options.MaxSimulations);
    }

    internal IAuthoringStore Store { get; }

    internal AuthoringOptions Options { get; }

    internal SimulationSessions Sessions { get; }

    /// <summary>The catalog of the workspace: unrestricted when it has none; null with a refusal when its file is not valid.</summary>
    internal AuthoringCatalog? Catalog(WorkbenchLocale locale, out JsonObject? refusal)
    {
        refusal = null;
        var text = Store.ReadCatalog();
        if (text is null)
        {
            return AuthoringCatalog.Unrestricted;
        }

        var parsed = CatalogJson.Parse(text);
        if (parsed.Catalog is { } catalog)
        {
            return catalog;
        }

        var first = parsed.Errors[0];
        refusal = Results.Refuse(locale, "catalog.invalid", [("detail", first.Message)], "The catalog file of the workspace is not valid (" + first.Code + " at " + first.Path + "): " + first.Message + " The host must fix it.");
        return null;
    }

    internal IReadOnlyList<LoadedRule> ApprovedRules(AuthoringCatalog catalog)
    {
        var contracts = catalog.Contracts;
        return [.. Store.ListApproved(DocumentKind.Rule).Select(id => LoadRule(id, Store.ReadApproved(DocumentKind.Rule, id) ?? string.Empty, contracts))];
    }

    internal static LoadedRule LoadRule(string id, string text, IReadOnlyCollection<BrainContract> contracts)
    {
        var loaded = BrainRuleLoader.Load(text, contracts);
        return new LoadedRule(id, text, loaded.Rule, loaded.Errors);
    }

    /// <summary>The load context a flow is judged in: the approved rules that load, and the ids of every approved and draft flow as known children.</summary>
    internal ProcessLoadContext FlowContext(AuthoringCatalog catalog, params string[] extraIds)
    {
        var rules = ApprovedRules(catalog).Select(item => item.Rule).OfType<BrainRule>().ToList();
        var ids = Store.ListApproved(DocumentKind.Flow).Concat(Store.ListDrafts(DocumentKind.Flow)).Concat(extraIds).Distinct(StringComparer.Ordinal).ToList();
        return new ProcessLoadContext(rules, ids);
    }

    internal IReadOnlyList<LoadedFlow> ApprovedFlows(AuthoringCatalog catalog)
    {
        var context = FlowContext(catalog);
        return [.. Store.ListApproved(DocumentKind.Flow).Select(id =>
        {
            var text = Store.ReadApproved(DocumentKind.Flow, id) ?? string.Empty;
            var loaded = BrainProcessLoader.Load(text, context);
            return new LoadedFlow(id, text, loaded.Process, loaded.Errors);
        })];
    }

    internal LoadedFlow? ApprovedFlow(AuthoringCatalog catalog, string id)
    {
        var text = Store.ReadApproved(DocumentKind.Flow, id);
        if (text is null)
        {
            return null;
        }

        var loaded = BrainProcessLoader.Load(text, FlowContext(catalog));
        return new LoadedFlow(id, text, loaded.Process, loaded.Errors);
    }

    internal LoadedRule? ApprovedRule(AuthoringCatalog catalog, string id)
    {
        var text = Store.ReadApproved(DocumentKind.Rule, id);
        return text is null ? null : LoadRule(id, text, catalog.Contracts);
    }
}
