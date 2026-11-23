# Architecture overview

Design: [B0 spec](../specs/b0-decision-engine-extraction/spec.md). This page records what PR 1 and PR 2 ship.

## Libraries and the dependency rule

| Approved name | Package / assembly | Content | Depends on |
| --- | --- | --- | --- |
| `brain-engine` | `Synapbiz.Brain.Contracts` | The source repository's `Nuvora.Decisions.Contracts` minus `Traces/` (28 files), plus `ContentHash` and `BrainException` | BCL only |
| `brain-engine` | `Synapbiz.Brain.Engine` | The source repository's `Nuvora.Decisions.Engine` (14 files) | `Synapbiz.Brain.Contracts` |
| `brain-spec` | `Synapbiz.Brain.Spec` | `brain.rule@1` schema, contract reader, rule loader and `BrainRule` (compile, evaluate with provenance, validate, run goldens); see [the PR 2 spec](../specs/b0-brain-spec-rules/spec.md) | the two above, `JsonSchema.Net` 8.0.5 |
| `brain-workbench` | `Synapbiz.Brain.Workbench` | Headless rule workbench for `brain.rule@1`: plain-language views, simulation with a per-row explanation, test runs, revision diff and review documents; see [the rule workbench spec](../specs/rule-workbench/spec.md) and [rule-workbench](../reference/rule-workbench.md) | the three above, base class library |
| `brain-workbench-blazor` | `Synapbiz.Brain.Workbench.Blazor` | Razor class library with one public component, `RuleWorkbench`: rule list, reader, simulator, test cases and review panels over `Synapbiz.Brain.Workbench`; see [rule-workbench-panels](../reference/rule-workbench-panels.md) | the four above, `Microsoft.AspNetCore.Components.Web` |
| `brain-host-efcore` | later | persistence, lifecycle, traces | not started |

Dependency rule: `Synapbiz.Brain.Contracts` and `Synapbiz.Brain.Engine` reference only the base class library and each other, and declare no package reference. `Synapbiz.Brain.Spec` references the base class library, those two and `JsonSchema.Net` with its transitive packages, nothing else. None of the four reads a clock, GUID generator, randomness, file or network; time and identity arrive as parameters. The purity scan covers methods, constructors and type initializers of all four assemblies. `Synapbiz.Brain.Workbench` references the base class library and the three libraries above, declares no package reference and is subject to the same purity scan; its restore graph holds only the build analyzers and the packages that flow from `Synapbiz.Brain.Spec`. `Synapbiz.Brain.Workbench.Blazor` references the four libraries and `Microsoft.AspNetCore.Components.Web` only, makes `RuleWorkbench` its only public component (internal helpers, no `_Imports.razor`), declares no `InternalsVisibleTo`, ships no script and is in the purity scan without exemptions. The demo host under `samples/` is not packable. These rules are enforced by `tests/Synapbiz.Brain.ArchitectureTests`.

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
- `tests/Synapbiz.Brain.Workbench.Tests`: labels and text, rule views and sentences in both locales, fact input, simulation, test runs, diff and behaviour comparison, the review document, null guards, on synthetic fixtures in `tests/fixtures/workbench`.
- `tests/Synapbiz.Brain.Workbench.Blazor.Tests`: bUnit tests of `RuleWorkbench` on the synthetic fixtures: list, tabs, Read, Simulate, Test cases, Review and export, refusal wording, the string table, the identifier guard and the stylesheet token contract.
- `tests/Synapbiz.Brain.ArchitectureTests`: BCL-only references for Contracts and Engine, no package references for them, purity of all five libraries, the reference set of `Synapbiz.Brain.Spec`, `Synapbiz.Brain.Workbench` and `Synapbiz.Brain.Workbench.Blazor`.
- `tests/parity`: scenarios, the two drivers and the live parity test.
