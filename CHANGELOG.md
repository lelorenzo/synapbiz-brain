# Changelog

## 0.1.0 (unreleased)

- B0 PR 1: `brain-engine`. The Decision Engine cores move into this repository as `Synapbiz.Brain.Contracts` (28 files, `BrainException`, `ContentHash`) and `Synapbiz.Brain.Engine` (14 files), with the engine unit tests, architecture guards (BCL-only references, no package references, engine purity), the source drift guard and the live and baseline behaviour parity proofs, and CI.
