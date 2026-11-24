# Consuming the packages

The ten packages (`Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine`, `Synapbiz.Brain.Spec`, `Synapbiz.Brain.Workbench`, `Synapbiz.Brain.Workbench.Blazor`, `Synapbiz.Brain.Process`, `Synapbiz.Brain.Host.Postgres` and, since 0.7.0, `Synapbiz.Brain.Authoring`, `Synapbiz.Brain.Mcp` and the .NET tool `Synapbiz.Brain.Mcp.Tool`) carry one version, 0.7.0 at the time of writing (0.1.0 held the first three only; the two workbench packages, the process library and the PostgreSQL host first appear in 0.2.0), and are published to the private GitHub Packages NuGet feed of the repository owner when a tag `vX.Y.Z` is pushed (`.github/workflows/release.yml`).

## Feed

Add a `nuget.config` next to the consumer's solution. It holds no secret; the credentials come from the environment.

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="synapbiz-brain" value="https://nuget.pkg.github.com/lelorenzo/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="synapbiz-brain">
      <package pattern="Synapbiz.Brain.*" />
    </packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <synapbiz-brain>
      <add key="Username" value="%NUGET_FEED_USER%" />
      <add key="ClearTextPassword" value="%NUGET_FEED_TOKEN%" />
    </synapbiz-brain>
  </packageSourceCredentials>
</configuration>
```

- In GitHub Actions of a consumer repository: grant that repository read access in each package's settings (Package settings › Manage Actions access), then set `NUGET_FEED_USER: ${{ github.actor }}` and `NUGET_FEED_TOKEN: ${{ secrets.GITHUB_TOKEN }}` with `permissions: packages: read`.
- On a developer machine: a personal access token with the `read:packages` scope in `NUGET_FEED_TOKEN`. Never commit it.

## Use

```csharp
var contract = BrainContract.Parse(File.ReadAllText("contract-module.json"));
var loaded = BrainRuleLoader.Load(File.ReadAllText("some.rule.json"), [contract]);
if (!loaded.IsValid) { /* loaded.Errors: code, path, EN/ES message, fix hint */ }

var result = loaded.Rule!.Evaluate(facts, asOf);
// result.Outcome: Decided (OutcomeJson, MatchedRowKeys) or NoDecision
// result.Trace:   VersionNumber, MatchedRowKeys, FactsJson, FactsHash
```

The document and contract formats are in [reference/brain-rule-v1.md](reference/brain-rule-v1.md). Running processes durably on PostgreSQL: start with [the consumer guide](guides/process-engine.md) (packages, registration, the ports to implement, activation, tasks, operations); the host reference is [reference/process-host-postgres.md](reference/process-host-postgres.md).

Giving an assistant tools to model processes and rules (since 0.7.0): `Synapbiz.Brain.Authoring` has no model and no network; construct `new AuthoringTools(new FileAuthoringStore(folder), new AuthoringOptions { Clock = timeProvider })` and call `tools.Invoke(name, input)` for any of the seventeen tools, or list `tools.All` (name, description, input schema) to expose them through your own transport. The tools propose and never activate; see [reference/brain-authoring.md](reference/brain-authoring.md). To put them in front of an assistant through the Model Context Protocol, reference `Synapbiz.Brain.Mcp` (it brings the official C# SDK, `ModelContextProtocol` 2.4.0) and add them to the MCP server you already build with `services.AddMcpServer().WithBrainAuthoring(new BrainMcpOptions { Store = store, Authoring = new AuthoringOptions { Clock = timeProvider }, Authorize = ... })`: each tool becomes one MCP tool, the guide sections become `brain://guide/<section>` resources and `model_a_process_with_me` becomes a prompt; you supply the store, the catalog, the clock and the authorization, and you apply proposals yourself (a rule proposal with `needsReview` false on its owner's approval, anything else held for review). For an author's machine install the tool `Synapbiz.Brain.Mcp.Tool` (`dotnet tool install --global Synapbiz.Brain.Mcp.Tool`) and point an MCP client at `brain-mcp --workspace <folder> [--catalog <file>] [--language en|es]`. See [reference/brain-mcp.md](reference/brain-mcp.md).

Hosts of the panels, upgrading to 0.8.0 (no code has to change, but two things look different):

- `FlowWorkbench` now opens on the **process map** (`Session.View` is `FlowWorkbenchView.Map`). A host that wants the old first screen sets `session.View = FlowWorkbenchView.List`; the reader can switch with the Map and List buttons either way. The list is now grouped by kind (Main processes, Subprocesses, Scheduled tasks) and the host's `Group` is a second level under each kind, so the process selected by default is the first one the list shows.
- The screens say **process** where they said flow ("Another process", "Processes"). The default texts changed in both languages; a host that overrides a `flow.*` string by key keeps its own text, and a host test that pins a default sentence has to be updated.
- New optional parameters of `FlowWorkbench`: `MapLabels` (the names of rules and connectors on the map, by `ProcessMap.LabelKey`), `OnOpenRule` (raised with a rule id when the reader chooses a rule chip; without it the chip is plain text), `RuleAuthorities` (rule id to authority code, from `RuleClaims.From(rule.Meta).Authority`) and `AuthorityLabels` (code to label). `RuleWorkbench` gains `SourceLabels` and `AuthorityLabels`. Hosts must define the sixteen `--wb-kind-*` tokens (a colour and a soft background for main, sub, scheduled, event, rule, connector, person and ai) to colour the kinds; without them the icons take the colour of the text around them. See [reference/flow-workbench-panels.md](reference/flow-workbench-panels.md).
- The diagram draws a symbol on each step and never cuts a route label below 8 characters; a label that does not fit anywhere becomes a numbered mark with its text listed under the diagram. `rule_list` of the authoring tools takes the filters `source` and `authority`, and `rule_get` and `rule_list` report them ([reference/brain-authoring.md](reference/brain-authoring.md)).

## Releasing

1. Update `CHANGELOG.md` (move `Unreleased` under the new version) and set `<Version>` in `Directory.Build.props` to the same number, so that a local `dotnet pack` agrees with the release.
2. Push a tag `vX.Y.Z` on `main`. The workflow builds, tests, packs with the version of the tag (`-p:Version`, taken from the tag name, not from the file) and publishes every package of the solution.
A published version is immutable: a fix is a new version.
