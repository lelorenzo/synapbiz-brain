# Brief B0-PR2 — `brain-spec` for rules: load, validate and run `brain.rule@1` on the extracted engine

Design: `docs/specs/b0-decision-engine-extraction/spec.md`. Read it and `docs/architecture/overview.md` first. This brief is self-contained and deterministic. You never choose between options and never widen scope. If anything differs from what is written here, stop and report.

Owner decision in force: this library is written against the engine's own value types and does **not** copy or re-implement `Synapbiz.Spec.Core`. No `brain.flow@1`, no v0 bridge, no `$t`, no patches.

## Repositories and branch

- Target: `/home/user/synapbiz-brain`, local branch `b0-pr2` (already checked out, stacked on PR 1). Commit there. Do not push. Do not touch GitHub.
- Source: `/home/user/nuvora`, **read-only**, pinned at `054828b115de569b98b220f10b83c4b35f83a9ea` (same checks as PR 1: HEAD equals the pin, `git status --porcelain -- src tests` empty, before and after).
- Environment: as PR 1 (`DOTNET_ROLL_FORWARD=Major`, SDK 10, target `net9.0`). Run nuvora builds from a working directory outside `/home/user/nuvora` (its `global.json` pins SDK 9).

## What nuvora does today (the behaviour to match)

- Schema: `src/Platform/Spec/Synapbiz.Spec.Brain/Schemas/brain.rule.v1.json`.
- Validation: `src/Platform/Spec/Synapbiz.Spec.Brain/BrainValidator.cs` (structure through `Synapbiz.Spec.Core.SpecValidator`, then `ValidateStructure`, `ValidateSemantics`, `ValidateRule`, `ValidateCondition`, `ValidateOutcome`, `ValidateGolden`, and the `Codes` constants).
- Projection to the engine: `LegacyBrainBridge.FromRule(document)` gives v0 definition and goldens JSON; `Nuvora.Decisions.Application.Serialization.DecisionDefinitionCodec.ParseDefinition` / `ParseGoldens` (file `src/Shared/Nuvora.Decisions/Application/Serialization/DecisionDefinitionCodec.cs`) give the `DecisionTable` and `GoldenCase` list. See `ProjectRule(JsonObject)` in `tests/Nuvora.ArchitectureTests/Brain/BrainEngineParityTests.cs`.
- Fixtures: `tests/fixtures/spec/brain/conformance.json` (80 cases; 77 are `brain.rule@1`: 60 valid, 17 with expected errors) and `tests/fixtures/spec/brain/contracts/contract-*.json`.

## Step 1 — Fixtures and schema (data, byte copies, drift-guarded)

- Copy byte-for-byte: the schema to `src/Synapbiz.Brain.Spec/Schemas/brain.rule.v1.json`; `conformance.json` and the five `contracts/contract-*.json` to `tests/fixtures/spec/brain/` (same relative layout).
- Extend `tools/parity/check-source-drift.sh` with a second section that requires these seven files to be byte-identical to nuvora. New final line: `source drift: none (44 files, 3 patched, 2 excluded; 7 data files)`. Update the places that quote the old line.

## Step 2 — `src/Synapbiz.Brain.Spec`

`Synapbiz.Brain.Spec.csproj`: `net9.0`, `RootNamespace`, `AssemblyName` and `PackageId` `Synapbiz.Brain.Spec`, `IsPackable` true, description "brain.rule@1 documents for the Brain decision engine: schema, validation, loading into decision tables and evaluation with provenance.", project references to the two libraries, one package reference `JsonSchema.Net` 8.0.5 (add to `Directory.Packages.props`), the schema as `EmbeddedResource`. Add the project to `Synapbiz.Brain.sln`.

Public API, namespace `Synapbiz.Brain.Spec`, exactly this and nothing else public:

```csharp
public sealed record BrainSpecError(string Code, string Path, string MessageEn, string MessageEs, string FixHint);

public sealed class BrainContract
{
    public string Module { get; }
    public int SchemaVersion { get; }
    public string Address { get; }                                  // "<module>@<schemaVersion>"
    public IReadOnlyList<DecisionKindDescriptor> Kinds { get; }
    public bool TryGetKind(string kindId, [NotNullWhen(true)] out DecisionKindDescriptor? kind);
    public static BrainContract Parse(string json);                 // throws DecisionEngineException("brain.contract.malformed") naming the JSON path
}

public sealed class BrainRule
{
    public string Id { get; }
    public long Version { get; }
    public DecisionKindDescriptor Kind { get; }
    public DecisionTable Table { get; }
    public IReadOnlyList<GoldenCase> Goldens { get; }
    public IReadOnlyDictionary<string, string> RowIdsByKey { get; } // engine row key (legacyKey) -> row id
    public JsonObject Meta { get; }                                 // deep clone of "meta"
    public CompiledTable Compile();                                 // TableCompiler.Compile(Table, Kind), cached
    public DecisionResult Evaluate(IReadOnlyDictionary<string, DecisionValue> facts, DateOnly asOf, IComposedFactResolver? resolver = null);
    public ValidationReport ValidateTable();                        // TableValidator.Validate(Table, Kind)
    public GoldenRunResult RunGoldens(IComposedFactResolver? resolver = null);
}

public sealed record BrainRuleLoadResult(BrainRule? Rule, IReadOnlyList<BrainSpecError> Errors, IReadOnlyList<string> DeferredToEngine)
{
    public bool IsValid => Errors.Count == 0;
}

public static class BrainRuleLoader
{
    public static BrainRuleLoadResult Load(string json, IReadOnlyCollection<BrainContract> contracts, IReadOnlyCollection<string>? knownSectionRefs = null);
}
```

Behaviour:

- `BrainContract.Parse` reads the contract format of the fixtures: `module`, `schemaVersion`, `facts[]` (`kind`, `name`, `type`, `nullable`, `allowedValues`, `labelSource`, `catalogFactId`), `kinds[]` (`id`, `schemaVersion`, `evaluationMode`, `permissions.read/author/approve/readTrace`, `outcomes[]` with `name`, `type`, `nullable`, `labelSource`). It builds one `DecisionKindDescriptor` per kind (`Key`, `SchemaVersion`, `Facts` in file order, `OutcomeFields`, `EvaluationMode`, the four permissions; an empty `allowedValues` array maps to `null`; other descriptor members keep their defaults). Other top-level members are ignored. Read the outcome member names from the fixtures; if a kind in any of the five fixtures cannot be represented, stop.
- `BrainRuleLoader.Load`, in this order, each step reporting all its errors and stopping the pipeline if it reported any:
  1. JSON parse and schema (JsonSchema.Net against the embedded schema). Error codes and paths must equal what nuvora's `BrainValidator` reports for the same document (it delegates to `SpecValidator`; read it and mirror its code strings and path form).
  2. The extra structure check of `BrainValidator.ValidateStructure` (`brain.rule.golden_outcome_missing`).
  3. Semantics, mirroring `ValidateSemantics` → `ValidateRule` and its helpers, with the **same code strings, paths, EN/ES messages and fix hints** (copy the texts; they are data). `uses` references resolve against `knownSectionRefs` exactly as nuvora resolves them against its context (mirror the code and path it reports, see the conformance case that expects `spec.semantics.reference`).
  4. Projection to `DecisionTable` and `GoldenCase` list. The result must be equal to nuvora's `LegacyBrainBridge.FromRule` + `DecisionDefinitionCodec.ParseDefinition/ParseGoldens` for every valid document (proved in step 4 below): row key = `legacyKey`, rows in document order, `defaultRow`, hit policy, every operator and operand shape, typed values (Integer and Decimal from their string lexemes with invariant culture, Money amount + currency, Date, Reference GUID, Boolean, Code, Enum), `Row.Outcome` as the JSON string the nuvora path produces, goldens with `expect.row` mapped from row id to row key.
  - `DeferredToEngine` is the same list nuvora returns for rules (`overlap`, `coverage`, `goldens`, `outcome-policy`).
- `BrainRule.Evaluate` builds the `FactSet` for `Kind`, evaluates the compiled table and returns a `DecisionResult` whose trace is the engine trace `with { VersionNumber = checked((int)Version) }`; a version above `int.MaxValue` leaves `VersionNumber` null. The rule id is `BrainRule.Id`; the document is not mutated.
- No clock, GUID generation, randomness, file or network access anywhere in this library. No static mutable state other than the lazily built schema.

## Step 3 — Unit tests (`tests/Synapbiz.Brain.Spec.Tests`, xunit + FluentAssertions + coverlet.collector)

- Conformance: one theory over the 77 `brain.rule@1` cases of `conformance.json` (skip nothing; the 3 `brain.flow@1` cases are filtered by `$spec` in the data source and their count asserted to be exactly 3). Contracts come from `context.contracts` → `contract-<name>.json`. `expect: "valid"` → `IsValid` and a non-null rule; `expect.errors` → the set of `(code, path)` equals the expected set.
- For every valid case: `ValidateTable()` does not throw; `RunGoldens()` result is asserted against the document's own goldens only when `ValidateTable().IsValid` (count how many cases that is and assert the number, so the check cannot silently run on zero cases).
- `BrainContract`: the five fixtures parse; kind and fact counts per fixture pinned; malformed inputs (missing `module`, unknown fact type, fact pointing to an undeclared kind) throw `brain.contract.malformed`.
- Loader structure errors: at least 8 hand-written invalid documents (missing `uses`, extra top-level key, `revision` instead of `version`, lowercase type tag, Integer as JSON number, unknown operator, row without `legacyKey`, golden with neither `row` nor `outcome`).
- Provenance: `Evaluate` returns the matched row key, `VersionNumber` equal to the document version, facts JSON and hash; the same facts in a different insertion order give the same hash.
- A synthetic clinical-style document using only synthetic names (kind `synthetic.readiness`, collect policy, Enum and Integer facts with a `range`), with its own contract, proving the collect + range path end to end. No real names, no patient data.

Architecture tests (add to `tests/Synapbiz.Brain.ArchitectureTests`): `Synapbiz.Brain.Spec` references only the BCL, the two libraries and `JsonSchema.Net` with its transitive packages (list them explicitly as found in the restore graph); the purity scan of PR 1 also covers `Synapbiz.Brain.Spec` **and now includes constructors and static constructors** (`GetConstructors` plus type initializers) for all three assemblies. Mutation-verify both, including a `static readonly DateTime X = DateTime.UtcNow;` field, and record the RED outputs.

## Step 4 — Parity with nuvora's path (extend `tests/parity/`)

- Shared scenario source gains group `spec/<case name>/...`, built on an interface declared in the shared source: `IRuleProjector { ProjectedRule? Project(string documentJson, IReadOnlyList<string> contractJsons, out IReadOnlyList<(string Code, string Path)> errors); }` where `ProjectedRule` carries the `DecisionTable`, the goldens and the `DecisionKindDescriptor`.
  - `Driver.Library` implements it with `BrainRuleLoader.Load`.
  - `Driver.Nuvora` implements it with nuvora's `BrainValidator` + `LegacyBrainBridge.FromRule` + `DecisionDefinitionCodec` (project reference to `$(NUVORA_ROOT)/src/Platform/Spec/Synapbiz.Spec.Brain/Synapbiz.Brain.Spec.csproj`; the codec and its two error files compile-linked exactly as `tests/Synapbiz.Spec.Brain.Tests/Synapbiz.Spec.Brain.Conformance.csproj` does). The descriptor on the nuvora side is built by compile-linking this repository's `BrainContract.cs` into `Driver.Nuvora` (it may use only `Nuvora.Decisions.Contracts.*` types and `System.Text.Json`, so it compiles against both copies). If it cannot be compile-linked without edits, stop.
- Scenarios, for each of the 77 rule cases, results as invariant strings: `spec/<n>/errors` (sorted `code@path` list); and for valid cases `spec/<n>/table` (hit policy, then per row: key, each condition's fact, operator, operands, inclusivity, and `CanonicalOutcomeJson.Canonicalize(outcome)`; default row), `spec/<n>/goldens` (names, facts canonical JSON, expectation) and `spec/<n>/run` (`GoldenRunner` result, or `error:<code>`). `<n>` is the zero-based index in `conformance.json` padded to 3 digits. Plus the 8 structure-error documents of step 3 as `spec/structure/<i>/errors`.
- Regenerate the baseline with the existing switch; both parity tests and `ParityBaselineTests` must pass. The PR 1 scenarios must be unchanged in name and result (assert: the first 364 baseline entries are byte-identical to the committed ones).

## Step 5 — Docs

- `docs/specs/b0-brain-spec-rules/spec.md`, `plan.md`, `tasks.md` (short: scope, API above, what is deferred to the engine, parity).
- `docs/reference/brain-rule-v1.md`: how to write a `brain.rule@1` document and a contract file, field by field, with one complete synthetic example that the tests load (keep the example as a fixture file and include it from there, tested).
- `docs/migration/interim-rule-shape.md`: field-by-field mapping from the interim rule shape (envelope `id`, `revision`, `meta.facts`, `meta.outcome`, `meta.reasons`; body with lowercase type tags, JSON-number integers, row `key`, goldens as `expected: { kind, rowKeys }`) to `brain.rule@1` + a contract file: `revision` → `version`; add `uses: []` and `requires: ["<module>@1"]`; `body.kind` named `<module>.<snake_case>`; row `key` → `legacyKey` and `id` (slug of the key), `version: 1`; type tags capitalised; Integer and Decimal as strings; typed outcome cells → plain outcome fields; `meta.facts`/`meta.outcome` → contract `facts`/`kinds[].outcomes`; goldens → `expect: { row, outcome }` or `{ noDecision: true }`, and for collect an outcome array; `meta.reasons` stays under `meta`. State which interim constructs have no target. Synthetic examples only.
- Update `docs/architecture/overview.md`, `CHANGELOG.md`, `README.md` (three packages).
- CI: `dotnet pack` now yields 3 packages; no other workflow change.

## Gates

Same commands and order as PR 1 (drift guard, build, test with coverage, live parity with `NUVORA_ROOT`, pack, nuvora status). Warnings: 0 in new files; copied files as PR 1. Coverage of `Synapbiz.Brain.Spec` reported; Engine and Contracts line coverage not lower than 97.0% and 67.0%.

## Acceptance

- 77 of 77 rule conformance cases pass; live parity identical for every `spec/` scenario; PR 1 scenarios untouched.
- No test skipped, disabled or filtered other than the 3 flow cases filtered by `$spec` with their count asserted.
- No file under `/home/user/nuvora` changed outside `bin/` and `obj/`.

## Stop rules

- Any PR 1 stop rule.
- A parity difference in a `spec/` scenario that you cannot remove by making this library match nuvora's observable behaviour: report the case and both results. Never edit a fixture, an expected value or the nuvora side to hide it.
- Matching nuvora would require copying `Synapbiz.Spec.Core` source or adding a package beyond `JsonSchema.Net`.
- The contract fixtures or the schema cannot be consumed as described.
- Anything needing a decision this brief does not make.

## Hand-back

As PR 1: commits, gate summary lines, scenario counts per group, coverage per assembly, warnings, mutation RED outputs, deviations and open questions, and what was not done.
