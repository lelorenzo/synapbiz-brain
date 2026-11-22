# Plan — B0 Decision Engine extraction

Design: [spec.md](spec.md).

| Step | Deliverable | Owner | Depends on | Status |
| --- | --- | --- | --- | --- |
| PR 1 | `brain-engine`: `Synapbiz.Brain.Contracts` + `Synapbiz.Brain.Engine`, moved unit tests, architecture guards, drift guard, live and baseline parity, CI, docs | implementer per [brief](../../briefs/B0-PR1-brain-engine.md), independent verifier, then PR by the owning thread | `main` seeded | implemented and independently verified |
| PR 2 | `brain-spec` for rules: `brain.rule@1` schema, loader to `DecisionTable` and kind descriptor from a contract file, definition codec, rule conformance cases, provenance (rule id + version on the trace) | brief after PR 1 is verified | PR 1 | not started |
| PR 3 | Publish 0.1.0 and adopt in `synapbiz-health-mcp` (migrate the front-desk rule file to real `brain.rule@1`, retire the local evaluator) | this thread publishes; the SzH repo thread owns its PR | PR 2, feed decision | not started |
| Later | Workflow Engine (first real SzH need), `brain-host-efcore`, nuvora swap after 2027-01-18 (ERP plan thread) | — | — | not started |

Loop per PR: brief → implementer on the thread branch → independent verifier re-runs every gate and changes nothing → one repair attempt → PR → merge only with all checks green and the owner's go.

Target: first published Decision Engine package 2026-12-01.
