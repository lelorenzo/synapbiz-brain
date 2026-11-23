# Tasks — W1-A, `brain.process@1` and the pure interpreter

- [x] Project `src/Synapbiz.Brain.Process`, solution entry, embedded schema `Schemas/brain.process.v1.json`
- [x] Loader: JSON, envelope, schema, static rules of spec section 2.4, diagnostics in English and Spanish
- [x] Model: variables and value validation, steps, durations, retry policy
- [x] Interpreter: start, run loop, every step kind, triggers, cancel, resume, suspension, limits
- [x] `ProcessState` JSON round trip, deterministic identifiers
- [x] Scenario runner with virtual clock
- [x] Tests: conformance file, interpreter branches, state round trip, determinism, reference outreach process, provenance
- [x] Architecture tests: purity scan includes the new library, reference coupling of the new library
- [x] Docs: `docs/reference/brain-process-v1.md`, architecture overview, changelog, README
- [x] Independent verification of every gate (verifier found 9 conformance defects; repaired and re-run green)
