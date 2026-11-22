# Architecture overview

Design: [B0 spec](../specs/b0-decision-engine-extraction/spec.md). This page records what PR 1 ships.

## Libraries and the dependency rule

| Approved name | Package / assembly | Content | Depends on |
| --- | --- | --- | --- |
| `brain-engine` | `Synapbiz.Brain.Contracts` | The source repository's `Nuvora.Decisions.Contracts` minus `Traces/` (28 files), plus `ContentHash` and `BrainException` | BCL only |
| `brain-engine` | `Synapbiz.Brain.Engine` | The source repository's `Nuvora.Decisions.Engine` (14 files) | `Synapbiz.Brain.Contracts` |
| `brain-spec` (PR 2) | `Synapbiz.Brain.Spec` | `brain.rule@1` schema, loader, definition codec | the two above, `JsonSchema.Net` |
| `brain-host-efcore` | later | persistence, lifecycle, traces | not started |

Dependency rule: `Synapbiz.Brain.Contracts` and `Synapbiz.Brain.Engine` reference only the base class library and each other, and declare no package reference. The engine reads no clock, GUID generator, randomness, file or network; time and identity arrive as parameters. Both rules are enforced by `tests/Synapbiz.Brain.ArchitectureTests`.

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

   ```bash
   bash tools/parity/check-source-drift.sh <source-checkout>
   ```

2. Behaviour parity, live. One scenario source (`tests/parity/Scenarios`) is compiled twice, against this library (`Driver.Library`) and against the source repository's projects (`Driver.Nuvora`, from `NUVORA_ROOT`). The test asserts the two lists of (scenario, result) are identical. It fails loudly when `NUVORA_ROOT` is not set and is not part of the default solution.

   ```bash
   NUVORA_ROOT=<source-checkout> dotnet test Synapbiz.Brain.Parity.sln -c Release
   ```

3. Behaviour parity, baseline. The source side's run is committed as `tests/parity/baseline/decisions.parity.json` (with the commit). The default test run executes the same scenarios against this library and compares with the baseline, so CI needs no access to the source repository. To record a new baseline after re-importing source changes, run the live parity with `PARITY_WRITE_BASELINE=1`.

   ```bash
   dotnet test Synapbiz.Brain.sln -c Release --no-build
   ```

## Tests

- `tests/Synapbiz.Brain.Engine.Tests`: the engine unit tests moved unchanged from the source repository, tests for `ContentHash` and `BrainException`, and the baseline parity test.
- `tests/Synapbiz.Brain.ArchitectureTests`: BCL-only references, no package references, engine purity.
- `tests/parity`: scenarios, the two drivers and the live parity test.
