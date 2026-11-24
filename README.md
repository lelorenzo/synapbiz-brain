# synapbiz-brain

Brain is the shared decision library of Synapbiz. This repository holds its first parts, the Decision Engine, the rule format, the rule workbench with its panels, the process interpreter, its PostgreSQL host and the live view of its cases, the authoring tools for assistants and their MCP adapter, as eleven packages (ten libraries and one .NET tool), all `net9.0`:

- `Synapbiz.Brain.Contracts`: the public surface (decision table model, typed values, kinds, outcome, trace and validation contracts); base class library only.
- `Synapbiz.Brain.Engine`: the pure compiler, evaluator, validator, overlap and coverage analysis and golden runner, with no persistence, no DI, no clock, IO or randomness; depends on Contracts only.
- `Synapbiz.Brain.Spec`: `brain.rule@1` documents and module contracts: schema, validation, loading into decision tables and evaluation with the rule version on the trace; depends on the two above and `JsonSchema.Net`.
- `Synapbiz.Brain.Workbench`: headless rule workbench for `brain.rule@1`: plain-language rule views in English and Spanish, simulation with a per-row explanation, test runs, revision diff and review documents, all on the engine; depends on the three above and adds no package.
- `Synapbiz.Brain.Workbench.Blazor`: the workbench panels as two Razor components: `RuleWorkbench` (rule list, reader, simulator, test cases, review and export; since 0.8.0 also the sources and authority a rule states) and, since 0.6.0, `FlowWorkbench` (the process map, list, reader, diagram, try-out, tests and review, and, when the host gives a live source, the open cases over the map); draws what `Synapbiz.Brain.Workbench` computes, styled only by `--wb-*` tokens the host defines; adds no evaluation logic.
- `Synapbiz.Brain.Process`: `brain.process@1` documents and the pure process interpreter: variables, steps, retries, events, timers, tasks and subprocesses as state transitions that return requests and history, with an embedded scenario runner; depends on `Synapbiz.Brain.Spec` and `JsonSchema.Net`.
- `Synapbiz.Brain.Host.Postgres`: the durable PostgreSQL host of `brain.process@1` instances: store, history, timers, waits, inbox, effects with leases, worker and migrator; depends on `Synapbiz.Brain.Process`, `Npgsql` and four `Microsoft.Extensions.*` abstractions packages.
- `Synapbiz.Brain.Workbench.Postgres`: since 0.8.0, `PostgresFlowLiveSource`, the read-only `IFlowLiveSource` of `FlowWorkbench` over the cockpit queries of `IProcessEngine` (counts by step, pages of opaque case references, one case with its history); depends on `Synapbiz.Brain.Workbench` and `Synapbiz.Brain.Host.Postgres`, which do not reference each other.
- `Synapbiz.Brain.Authoring`: the headless authoring tools of Brain MCP (0.7.0): seventeen tools as plain functions with a JSON Schema each, to read, draft, check, simulate and propose changes to processes and rules; a workspace of approved documents, drafts and proposals, a process differ in English and Spanish, readiness checks and proposal documents. No model, no network, no MCP SDK; depends on `Synapbiz.Brain.Contracts`, `Engine`, `Spec`, `Process`, `Workbench` and `JsonSchema.Net`.
- `Synapbiz.Brain.Mcp`: the adapter of those tools to the Model Context Protocol with the official C# SDK (0.7.0): `services.AddMcpServer().WithBrainAuthoring(options)` adds the seventeen tools, the guide sections as `brain://guide/<section>` resources and the prompt `model_a_process_with_me` to a host's own MCP server, over its own store, catalog, clock and authorization. No model, no vendor, no network of its own; depends on `Synapbiz.Brain.Authoring` and `ModelContextProtocol`.
- `Synapbiz.Brain.Mcp.Tool`: the .NET tool `brain-mcp`, the same tools over standard input and output for a folder of documents on an author's machine (`brain-mcp --workspace <folder> [--catalog <file>] [--language en|es]`), which a desktop or coding assistant connects to.

## Build and test

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
bash tools/parity/check-source-drift.sh <source-checkout>
dotnet build Synapbiz.Brain.sln -c Release
dotnet test  Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"
NUVORA_ROOT=<source-checkout> dotnet test Synapbiz.Brain.Parity.sln -c Release
dotnet pack  Synapbiz.Brain.sln -c Release --no-build -o artifacts     # eleven packages
```

The drift guard and the live parity solution need a checkout of the source repository at the commit pinned in `tools/parity/SOURCE.lock`; the default solution and CI do not.

## Using it

- [Rule and contract formats](docs/reference/brain-rule-v1.md), with a complete synthetic example.
- [Rule workbench](docs/reference/rule-workbench.md): read, simulate, test, compare and review rules.
- [Rule workbench panels](docs/reference/rule-workbench-panels.md): mount the `RuleWorkbench` component, tokens, string keys, what the host does.
- [Running processes: the consumer guide](docs/guides/process-engine.md): what to implement, how to register the host, start cases, complete tasks and operate it; with [six reference process documents](samples/process-reference/README.md) to copy.
- [Authoring tools for assistants](docs/reference/brain-authoring.md): the seventeen tools, the workspace, the catalog, readiness and proposals.
- [Brain MCP](docs/reference/brain-mcp.md): mount the tools in a host's MCP server, run `brain-mcp`, connect an assistant, what the host must supply and enforce.
- [Process documents](docs/reference/brain-process-v1.md) and [the PostgreSQL process host](docs/reference/process-host-postgres.md).
- [Migrating an interim rule file](docs/migration/interim-rule-shape.md).

## Design

See [docs/specs/b0-decision-engine-extraction/spec.md](docs/specs/b0-decision-engine-extraction/spec.md), [docs/specs/b0-brain-spec-rules/spec.md](docs/specs/b0-brain-spec-rules/spec.md), [docs/specs/rule-workbench/spec.md](docs/specs/rule-workbench/spec.md) and [docs/architecture/overview.md](docs/architecture/overview.md).
