# Tasks — B0 Decision Engine extraction

## PR 1 — brain-engine
- [x] Repository skeleton (props, pinned packages, solution, README)
- [x] `Synapbiz.Brain.Contracts` (28 moved files, `BrainException`, `ContentHash`, 1 patched file)
- [x] `Synapbiz.Brain.Engine` (14 moved files, 2 patched files)
- [x] Drift guard (`SOURCE.lock`, `expected.patch`, `check-source-drift.sh`)
- [x] Moved unit tests (9 files) + new tests for `ContentHash` and `BrainException`
- [x] Architecture tests (BCL-only, no package references, engine purity), mutation-verified
- [x] Parity scenarios, two drivers, live parity test, committed baseline, baseline test
- [x] CI workflow, `docs/architecture/overview.md`, `CHANGELOG.md`
- [ ] Independent verification of every gate

## PR 2 — brain-spec (rules)
- [ ] Brief
- [ ] Schema, loader, codec, conformance, provenance

## PR 3 — publish and adopt
- [ ] Feed decision, publish 0.1.0
- [ ] SzH adoption PR (owned by the SzH repo thread)
