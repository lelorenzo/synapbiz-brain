# Spec — B0 PR 2, `brain-spec` for rules

Parent design: [B0 Decision Engine extraction](../b0-decision-engine-extraction/spec.md). Brief: [B0-PR2](../../briefs/B0-PR2-brain-spec-rules.md). Field-by-field guide: [brain-rule-v1](../../reference/brain-rule-v1.md).

## Scope

`Synapbiz.Brain.Spec` loads, validates and runs `brain.rule@1` documents on the engine PR 1 extracted. It answers four questions a consumer has:

1. What does a module offer? `BrainContract.Parse` reads a contract file (facts, decision kinds, outcome fields, permissions) into `DecisionKindDescriptor` values.
2. Is this rule document valid against those contracts? `BrainRuleLoader.Load` reports every error of the first failing step, with the diagnostic codes, paths, English and Spanish messages and fix hints of the source repository.
3. What table does it mean? A valid document is projected to a `DecisionTable` and its `GoldenCase` list, equal to what the source repository's validator, legacy bridge and definition codec produce.
4. What does it decide? `BrainRule` compiles the table, evaluates facts with the document version in the trace, validates the table and runs the rule's own goldens.

## Public API

Namespace `Synapbiz.Brain.Spec`, nothing else is public.

| Type | Members |
| --- | --- |
| `BrainSpecError` | record `(Code, Path, MessageEn, MessageEs, FixHint)` |
| `BrainContract` | `Module`, `SchemaVersion`, `Address` (`<module>@<schemaVersion>`), `Kinds`, `TryGetKind`, `static Parse(json)` (throws `DecisionEngineException("brain.contract.malformed")` with the JSON path in `Args["path"]`) |
| `BrainRule` | `Id`, `Version`, `Kind`, `Table`, `Goldens`, `RowIdsByKey` (engine row key to row id), `Meta` (deep clone), `Compile()` (cached), `Evaluate(facts, asOf, resolver?)`, `ValidateTable()`, `RunGoldens(resolver?)` |
| `BrainRuleLoadResult` | record `(Rule, Errors, DeferredToEngine)`, `IsValid` |
| `BrainRuleLoader` | `static Load(json, contracts, knownSectionRefs?)` |

## Loading, in order

Each step reports all of its errors and stops the pipeline when it reported any.

1. JSON and schema: parse, section envelope, registered format, then the embedded `brain.rule.v1.json` (JsonSchema.Net 8.0.5).
2. The extra structure check: a golden needs `expect.row` or `expect.outcome` or `noDecision` (`brain.rule.golden_outcome_missing`). Steps 1 and 2 are reported together.
3. Semantics: node checks (ids, `uses` references, blank text), then the contract semantics (owning contract required and available, kind declared, facts, operators, typed values, outcomes, goldens).
4. Projection to `DecisionTable` and `GoldenCase` list.

`DeferredToEngine` is `overlap`, `coverage`, `goldens`, `outcome-policy`: the loader does not decide them, `BrainRule.ValidateTable()` and `RunGoldens()` do, on the engine.

## Provenance

`Evaluate` returns the engine's `DecisionResult` whose trace carries `VersionNumber` equal to the document `version` (left null above `int.MaxValue`). The rule id is `BrainRule.Id`. The matched row keys, canonical facts JSON and its hash are the engine's own and do not depend on the insertion order of the facts.

## Purity and references

No clock, GUID generation, randomness, file or network access; no static mutable state but the lazily built schema. The library references the base class library, the two Brain libraries and `JsonSchema.Net` with its transitive packages, nothing else. Both are enforced by `tests/Synapbiz.Brain.ArchitectureTests`.

## Parity

The library is proved against the source repository three ways, all through the one shared scenario source in `tests/parity/Scenarios`:

- Conformance: the 77 `brain.rule@1` cases of the source suite (60 valid, 17 invalid) load as expected; the 3 `brain.flow@1` cases are filtered by `$spec` and their count asserted.
- Live parity (`NUVORA_ROOT`): for each case and for 8 hand-written invalid documents the library and the source path (`BrainValidator`, `LegacyBrainBridge.FromRule`, `DecisionDefinitionCodec`) must give the same sorted `code@path` errors, and for valid cases the same table, goldens and golden run.
- Baseline: the source side's results are committed in `tests/parity/baseline/decisions.parity.json`; the default test run compares the library with it.

The source repository's schema and conformance data are byte copies guarded by the second section of `tools/parity/check-source-drift.sh` (7 data files).

## Not in this PR

A public copy of the source repository's `DecisionDefinitionCodec`: the loader projects a `brain.rule@1` document straight to the engine's table and golden cases, and parity with the codec's output is proved by the scenarios; the parent design's "codec moves in PR 2" is met by that projection, not by copying the codec. Workflow (`brain.flow@1`) documents, the feed decision and publishing (PR 3), host persistence (`brain-host-efcore`), renaming the `Nuvora.Decisions.*` namespaces.
