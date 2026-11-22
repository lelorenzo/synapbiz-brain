# synapbiz-brain

Brain is the shared decision library of Synapbiz. This repository holds its first part, the Decision Engine: `Synapbiz.Brain.Contracts` (the public surface: decision table model, typed values, kinds, outcome, trace and validation contracts) and `Synapbiz.Brain.Engine` (the pure compiler, evaluator, validator, overlap and coverage analysis and golden runner, with no persistence, no DI, no clock, IO or randomness). Both target `net9.0` and depend on the base class library only.

## Build and test

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
bash tools/parity/check-source-drift.sh <source-checkout>
dotnet build Synapbiz.Brain.sln -c Release
dotnet test  Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"
NUVORA_ROOT=<source-checkout> dotnet test Synapbiz.Brain.Parity.sln -c Release
dotnet pack  Synapbiz.Brain.sln -c Release --no-build -o artifacts
```

The drift guard and the live parity solution need a checkout of the source repository at the commit pinned in `tools/parity/SOURCE.lock`; the default solution and CI do not.

## Design

See [docs/specs/b0-decision-engine-extraction/spec.md](docs/specs/b0-decision-engine-extraction/spec.md) and [docs/architecture/overview.md](docs/architecture/overview.md).
