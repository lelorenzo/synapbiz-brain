# Plan — B0 PR 2, `brain-spec` for rules

Design: [spec.md](spec.md). Brief: [B0-PR2](../../briefs/B0-PR2-brain-spec-rules.md).

| Step | Deliverable | Proof |
| --- | --- | --- |
| 1 | Schema and conformance data as byte copies; second drift-guard section (7 data files) | `bash tools/parity/check-source-drift.sh <source-checkout>` prints `source drift: none (44 files, 3 patched, 2 excluded; 7 data files)` |
| 2 | `src/Synapbiz.Brain.Spec`: contract reader, loader, projection, `BrainRule` | build with 0 errors and 0 warnings in the new files |
| 3 | Unit tests (conformance, contract, structure errors, provenance, readiness example, documentation) and architecture tests (purity incl. constructors and type initializers, coupling of the Spec assembly), mutation-verified | `dotnet test Synapbiz.Brain.sln` |
| 4 | Parity: the `spec/` scenario group, `IRuleProjector`, both drivers, regenerated baseline | live parity identical; first 364 baseline entries unchanged |
| 5 | Docs, changelog, README, overview | the documentation test loads the examples the reference page includes |

Order of the gates: drift guard, build, test with coverage, live parity, pack (three packages), source repository untouched.

Next: PR 3 (feed decision, publish 0.1.0).
