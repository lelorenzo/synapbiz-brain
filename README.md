# synapbiz-brain

Brain is the shared decision library of Synapbiz. This repository holds its first parts, the Decision Engine, the rule format, the rule workbench and the process interpreter, as five packages, all `net9.0`:

- `Synapbiz.Brain.Contracts`: the public surface (decision table model, typed values, kinds, outcome, trace and validation contracts); base class library only.
- `Synapbiz.Brain.Engine`: the pure compiler, evaluator, validator, overlap and coverage analysis and golden runner, with no persistence, no DI, no clock, IO or randomness; depends on Contracts only.
- `Synapbiz.Brain.Spec`: `brain.rule@1` documents and module contracts: schema, validation, loading into decision tables and evaluation with the rule version on the trace; depends on the two above and `JsonSchema.Net`.
- `Synapbiz.Brain.Workbench`: headless rule workbench for `brain.rule@1`: plain-language rule views in English and Spanish, simulation with a per-row explanation, test runs, revision diff and review documents, all on the engine; depends on the three above and adds no package.
- `Synapbiz.Brain.Process`: `brain.process@1` documents and the pure process interpreter: variables, steps, retries, events, timers, tasks and subprocesses as state transitions that return requests and history, with an embedded scenario runner; depends on `Synapbiz.Brain.Spec` and `JsonSchema.Net`.

## Build and test

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
bash tools/parity/check-source-drift.sh <source-checkout>
dotnet build Synapbiz.Brain.sln -c Release
dotnet test  Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"
NUVORA_ROOT=<source-checkout> dotnet test Synapbiz.Brain.Parity.sln -c Release
dotnet pack  Synapbiz.Brain.sln -c Release --no-build -o artifacts     # five packages
```

The drift guard and the live parity solution need a checkout of the source repository at the commit pinned in `tools/parity/SOURCE.lock`; the default solution and CI do not.

## Using it

- [Rule and contract formats](docs/reference/brain-rule-v1.md), with a complete synthetic example.
- [Rule workbench](docs/reference/rule-workbench.md): read, simulate, test, compare and review rules.
- [Migrating an interim rule file](docs/migration/interim-rule-shape.md).

## Design

See [docs/specs/b0-decision-engine-extraction/spec.md](docs/specs/b0-decision-engine-extraction/spec.md), [docs/specs/b0-brain-spec-rules/spec.md](docs/specs/b0-brain-spec-rules/spec.md), [docs/specs/rule-workbench/spec.md](docs/specs/rule-workbench/spec.md) and [docs/architecture/overview.md](docs/architecture/overview.md).
