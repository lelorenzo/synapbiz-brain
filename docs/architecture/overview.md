# Architecture overview

Design: [B0 spec](../specs/b0-decision-engine-extraction/spec.md). This page records what the library PRs ship.

## Libraries and the dependency rule

| Approved name | Package / assembly | Content | Depends on |
| --- | --- | --- | --- |
| `brain-engine` | `Synapbiz.Brain.Contracts` | The source repository's `Nuvora.Decisions.Contracts` minus `Traces/` (28 files), plus `ContentHash` and `BrainException` | BCL only |
| `brain-engine` | `Synapbiz.Brain.Engine` | The source repository's `Nuvora.Decisions.Engine` (14 files) | `Synapbiz.Brain.Contracts` |
| `brain-spec` | `Synapbiz.Brain.Spec` | `brain.rule@1` schema, contract reader, rule loader and `BrainRule` (compile, evaluate with provenance, validate, run goldens); see [the PR 2 spec](../specs/b0-brain-spec-rules/spec.md) | the two above, `JsonSchema.Net` 8.0.5 |
| `brain-workbench` | `Synapbiz.Brain.Workbench` | Headless rule workbench for `brain.rule@1`: plain-language views, simulation with a per-row explanation, test runs, revision diff, review documents, and guided editing (typed edits applied to a rule text to give a draft revision, the choices for a form, the change view, proposal documents); see [the rule workbench spec](../specs/rule-workbench/spec.md) and [rule-workbench](../reference/rule-workbench.md) | the three above, base class library |
| `brain-workbench-blazor` | `Synapbiz.Brain.Workbench.Blazor` | Razor class library with one public component, `RuleWorkbench`: rule list, reader, simulator, test cases and review panels over `Synapbiz.Brain.Workbench`; see [rule-workbench-panels](../reference/rule-workbench-panels.md) | the four above, `Microsoft.AspNetCore.Components.Web` |
| `brain-process` | `Synapbiz.Brain.Process` | `brain.process@1` schema, loader and `BrainProcess`: the pure process interpreter (start, advance with one trigger, state, requests, history) and the scenario runner; see [the W1-A spec](../specs/w1-process-engine/spec.md) | `Synapbiz.Brain.Spec`, `JsonSchema.Net` 8.0.5 (and through it Contracts and Engine) |
| `brain-host-postgres` | `Synapbiz.Brain.Host.Postgres` | The durable PostgreSQL host of `brain.process@1` instances: store, history, timers, waits, inbox, effects, activation with event and schedule triggers, delivery-window timers, read-only cockpit queries (instance list with keyset paging, counts, case view with descendants, children), purge commands (one case removed completely in one transaction, finished cases before a date, old event ids), pump, hosted worker and migrator; see [the W1-B spec](../specs/w1-process-engine/spec-b-postgres-host.md) and [process-host-postgres](../reference/process-host-postgres.md) | `Synapbiz.Brain.Process` (and through it Spec, Contracts, Engine and `JsonSchema.Net`), `Npgsql` 9.0.4, four `Microsoft.Extensions.*` 9.0.0 abstractions packages |
| `brain-host-efcore` | later | persistence, lifecycle, traces | not started |

Dependency rule: `Synapbiz.Brain.Contracts` and `Synapbiz.Brain.Engine` reference only the base class library and each other, and declare no package reference. `Synapbiz.Brain.Spec` references the base class library, those two and `JsonSchema.Net` with its transitive packages, nothing else. `Synapbiz.Brain.Process` references the same and `Synapbiz.Brain.Spec`, nothing else. None of the six pure libraries reads a clock, GUID generator, randomness, file or network; time and identity arrive as parameters. The purity scan covers methods, constructors and type initializers of all six assemblies. `Synapbiz.Brain.Workbench` references the base class library and the three decision libraries (Contracts, Engine, Spec), declares no package reference and is subject to the same purity scan; its restore graph holds only the build analyzers and the packages that flow from `Synapbiz.Brain.Spec`. These rules are enforced by `tests/Synapbiz.Brain.ArchitectureTests`. `Synapbiz.Brain.Workbench.Blazor` references the four libraries and `Microsoft.AspNetCore.Components.Web` only, makes `RuleWorkbench` its only public component (internal helpers, no `_Imports.razor`), declares no `InternalsVisibleTo`, ships no script and is in the purity scan without exemptions. `Synapbiz.Brain.Host.Postgres` is not one of the pure libraries and is not in the purity scan: it owns the side effects (database, time through `TimeProvider`, tasks). Its own rule: it references the base class library, `Synapbiz.Brain.Process` (project), `Npgsql` and `Microsoft.Extensions.Hosting.Abstractions`, `Logging.Abstractions`, `DependencyInjection.Abstractions` and `Options`, and nothing else; only the PostgreSQL store class and the migrator use the `Npgsql` namespace, behind the internal `IProcessStore`.

Using the process library: [brain-process-v1](../reference/brain-process-v1.md) (how to write a process document, how a host runs it). Running it durably: [process-host-postgres](../reference/process-host-postgres.md) (registration, ports, migrator, activation and triggers, schedules and their misfire policy, delivery windows, cockpit queries, purging cases and what remains after a purge, delivery guarantees, tables, logging).

## Reference flows and the consumer guide

`samples/process-reference` holds six synthetic `brain.process@1` documents, with their rules and contracts, that together run one durable case from before a visit until the money is collected: four stage processes (`previsit.outreach`, `visit.note_wait`, `postvisit.coding_review`, `collection.followup`), the root `case.lifecycle` that starts one child per stage when the event `case.opened` arrives, and `practice.daily_refresh`, a schedule that reads the day's case list. Nothing in them names a person, an organisation or a product. They use only what the format already has; they add no engine feature and no file under `src/`.

They are checked from both sides. `tests/Synapbiz.Brain.Process.Tests` (`ReferenceFlowTests`) loads every document with zero diagnostics, runs every scenario and rule golden, replays the scenarios on a second, independent player, and fails when a route of a step with more than one route is taken by no scenario. `tests/Synapbiz.Brain.Host.Postgres.Tests` (`LifecycleFlowTests`) runs `case.lifecycle` on PostgreSQL with fake ports and a fake clock: the whole case to `collected`, one path per other result, the schedule, two tenants with one case reference, and the log. The files are data, not code: the architecture tests, the purity scan and the source drift guard do not look at them.

[The consumer guide](../guides/process-engine.md) is the path through the three references for somebody who runs these processes: packages, writing a process, registering the host, the four ports to implement (with C# examples that compile in `GuideExamples.cs` and are compared with the guide by `GuideTests`), activation, starting cases by event, human tasks, a cockpit, delivery guarantees, data protection, operations and versioning.

Using the rule library: [brain-rule-v1](../reference/brain-rule-v1.md) (rule and contract formats) and [interim-rule-shape](../migration/interim-rule-shape.md) (moving an interim rule file to `brain.rule@1`).

Namespaces are kept (`Nuvora.Decisions.*`) so the move is mechanical; new types live in `Synapbiz.Brain`. Renaming the namespaces is a later step.

## The declared patch

Moved files are copied byte-for-byte except this patch, committed as `tools/parity/expected.patch`:

| File | Change |
| --- | --- |
| `DecisionEngineException.cs` | base class `DomainException` becomes `Synapbiz.Brain.BrainException` (same constructor, `ErrorCode`, `Args`) |
| `Facts/FactSet.cs`, `Facts/CanonicalFacts.cs` | `Checksum` becomes `Synapbiz.Brain.ContentHash` (SHA-256 of the same bytes, lowercase hex, same `.Value`) |
| `Traces/DecisionTraceRecord.cs`, `Traces/IDecisionTraceSource.cs` | not moved: the host's tenant-scoped persistence entity |

## Parity while both copies exist

Three independent proofs, none of which changes the source repository (its `bin/` and `obj/` folders are the only build side effect):

1. Source drift guard. Every moved file equals its original at the commit pinned in `tools/parity/SOURCE.lock`, the three patched files differ by exactly `tools/parity/expected.patch`, and no extra file exists here.

   The same guard has a second section: seven data files (the `brain.rule@1` schema, the shared conformance suite and the five module contracts) must be byte-identical to the source repository's, since the Spec tests and the parity scenarios read them.

   ```bash
   bash tools/parity/check-source-drift.sh <source-checkout>
   ```

2. Behaviour parity, live. One scenario source (`tests/parity/Scenarios`) is compiled twice, against this library (`Driver.Library`) and against the source repository's projects (`Driver.Nuvora`, from `NUVORA_ROOT`). The test asserts the two lists of (scenario, result) are identical. The scenario source has two groups: the engine scenarios of PR 1 and, for PR 2, the `spec/` group, which projects each `brain.rule@1` case to a table, goldens and golden run through an `IRuleProjector`: `Driver.Library` with `BrainRuleLoader`, `Driver.Nuvora` with the source repository's `BrainValidator`, `LegacyBrainBridge.FromRule` and `DecisionDefinitionCodec`. The source repository's Brain Spec sources are compiled into `Driver.Nuvora` (not project-referenced) because their assembly has the same name as `Synapbiz.Brain.Spec`. It fails loudly when `NUVORA_ROOT` is not set and is not part of the default solution.

   ```bash
   NUVORA_ROOT=<source-checkout> dotnet test Synapbiz.Brain.Parity.sln -c Release
   ```

3. Behaviour parity, baseline. The source side's run is committed as `tests/parity/baseline/decisions.parity.json` (with the commit). The default test run executes the same scenarios against this library and compares with the baseline, so CI needs no access to the source repository. To record a new baseline after re-importing source changes, run the live parity with `PARITY_WRITE_BASELINE=1`.

   ```bash
   dotnet test Synapbiz.Brain.sln -c Release --no-build
   ```

## Tests

- `tests/Synapbiz.Brain.Engine.Tests`: the engine unit tests moved unchanged from the source repository, tests for `ContentHash` and `BrainException`, and the baseline parity test (it compiles the shared scenarios, which now include the `spec/` group, so it references `Synapbiz.Brain.Spec`).
- `tests/Synapbiz.Brain.Spec.Tests`: the 77 rule conformance cases, contract reader, loader structure errors, provenance, the synthetic readiness example and the documentation example test.
- `tests/Synapbiz.Brain.Workbench.Tests`: labels and text, rule views and sentences in both locales, fact input, simulation, test runs, diff and behaviour comparison, the review document, draft edits and the drafter (round trip through the loader), the choice catalogue, the change view, the proposal document, the error locator, null guards, on synthetic fixtures in `tests/fixtures/workbench`.
- `tests/Synapbiz.Brain.Workbench.Blazor.Tests`: bUnit tests of `RuleWorkbench` on the synthetic fixtures: list, tabs, Read, Simulate, Test cases, Review and export, refusal wording, the string table, the identifier guard and the stylesheet token contract.
- `tests/Synapbiz.Brain.Process.Tests`: the process conformance file (valid and invalid documents, one case per diagnostic code), interpreter tests for every branch of start, the run loop and each step kind, the state round trip, determinism, the reference outreach process with its five scenarios, the scenario runner and the documentation example test.
- `tests/Synapbiz.Brain.Host.Postgres.Tests`: integration tests against a real PostgreSQL (`BRAIN_TEST_POSTGRES`, else a Testcontainers `postgres:16-alpine`): migrator, start idempotency, events, effects with retry and lease expiry for both delivery modes, tasks, child processes, cancel and resume, the reference outreach process in its five scenario paths, two concurrency tests (parallel pumps; task completion against a timeout), the cockpit queries (filters, keyset paging over equal creation instants, attention, descendants to depth 16, no read of the state, the third migration on a database with data), the purge commands (a tree of any depth removed whole with exact counts, refusals, all or nothing with an injected failure, a purge against running pumps with no deadlock, a result that comes back after its case was purged, the fourth migration), activation, event triggers, schedules (misfire policies, grace, both daylight saving transitions, four pumps in parallel), delivery windows and the follow-up flows against the interpreter's oracle, with zones injected through `IZoneResolver`, tenant isolation, payload protection, definition unavailable, options, worker, the lock discipline of the store and logging hygiene; fixtures in `tests/fixtures/brain-host`.
- The reference flows add `ReferenceFlowTests` and `ReferenceTimingTests` to `tests/Synapbiz.Brain.Process.Tests` and `LifecycleFlowTests` and `GuideTests` to `tests/Synapbiz.Brain.Host.Postgres.Tests`; see the section above.
- `tests/Synapbiz.Brain.ArchitectureTests`: BCL-only references for Contracts and Engine, no package references for them, purity of all six libraries, the reference sets of `Synapbiz.Brain.Spec`, `Synapbiz.Brain.Workbench`, `Synapbiz.Brain.Workbench.Blazor` and `Synapbiz.Brain.Process`, and the reference set, project file, restore graph and `Npgsql` confinement of `Synapbiz.Brain.Host.Postgres`.
- `tests/parity`: scenarios, the two drivers and the live parity test.
