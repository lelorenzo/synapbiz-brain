# Changelog

## 0.1.0 (2026-11-22)

- Release workflow publishing to the private GitHub Packages feed on a version tag, a manual live-parity workflow, and the consumer guide (`docs/consuming.md`).
- B0 PR 1: `brain-engine`. The Decision Engine cores move into this repository as `Synapbiz.Brain.Contracts` (28 files, `BrainException`, `ContentHash`) and `Synapbiz.Brain.Engine` (14 files), with the engine unit tests, architecture guards (BCL-only references, no package references, engine purity), the source drift guard and the live and baseline behaviour parity proofs, and CI.
- B0 PR 2: `brain-spec` for rules. `Synapbiz.Brain.Spec` reads module contracts, loads and validates `brain.rule@1` documents against them with the source repository's codes, paths and messages, projects them to a decision table and golden cases, and runs them on the engine with the document version on the trace. 77 rule conformance cases, a purity scan extended to constructors and type initializers and to the new library, a second drift-guard section for 7 data files, and the `spec/` parity scenarios (310 entries: the 77 rule cases and 38 structure documents; the 364 earlier entries are unchanged). Docs: the rule reference and the interim-shape migration guide. The solution now packs three packages.
