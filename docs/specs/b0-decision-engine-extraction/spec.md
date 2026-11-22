# B0 — Extract the Brain Decision Engine into `synapbiz-brain`

Status: Draft for owner review (2026-11-22). Owner decisions: 2026-11-22 (Brain extracted once into this repo; Decision Engine first; no second `spec-core`).

## 1. Goal

Make this repository the home of the Brain libraries, starting with the Decision Engine, so that Synapbiz Health (SzH) evaluates its clinical rules with the same engine the ERP runs, and the ERP can replace its in-repo copy with the package after 2027-01-18.

Out of scope for B0: the Workflow Engine (deferred until the first real SzH need; nothing in January needs it), `brain-host-efcore` (persistence, four-eyes activation, tenant scope), `spec-core`, `brain.flow@1`, any change to `lelorenzo/nuvora`.

## 2. Inventory of the source (nuvora `master` @ `054828b115de569b98b220f10b83c4b35f83a9ea`, read-only)

| Project (`src/Shared/`) | Files / LOC | Role | Nuvora coupling |
| --- | --- | --- | --- |
| `Nuvora.Decisions.Contracts` | 30 / 1,241 | Public surface: table model, values, kinds, outcome, trace, validation, host ports | `DomainException` (base of `DecisionEngineException`); `ITenantScoped` + `TenantId` (`Traces/DecisionTraceRecord`) |
| `Nuvora.Decisions.Engine` | 14 / 1,958 | Pure engine: compiler, evaluator, operator matrix, validator, overlap and coverage analyzers, golden runner, canonical facts | `Checksum` (`FactSet.Hash`, `CanonicalFacts.Hash`) |
| `Nuvora.Decisions` | 50 / 5,449 | Host: aggregates, EF, MediatR handlers, caps, `DecisionDefinitionCodec` | `Nuvora.Domain`, `.Application`, `.Infrastructure` |
| `Nuvora.Workflows.Contracts` | 26 / 565 | Workflow public surface | `IIntegrationEvent` |
| `Nuvora.Workflows.Engine` | 11 / 961 | Pure overlay planner | none |
| `Nuvora.Workflows` | 34 / 2,382 | Host | `Nuvora.Domain`, `.Application`, `.Infrastructure` |

Facts that shape the design:

- The two Decision cores use only the BCL plus four Nuvora types. They declare no package references. Nuvora already guards this with `BrainCoreCouplingBudgetTests`.
- Nuvora already holds an approved extraction plan (`docs/architecture/brain-extraction-plan.md`): assemblies `Synapbiz.Brain.Contracts` and `Synapbiz.Brain.Engine`, **namespaces kept** (`Nuvora.Decisions.*`), `Checksum` replaced by an engine-owned `ContentHash` with the same bytes, `DomainException` replaced by an engine-owned `BrainException`. B0 executes that plan here instead of inside nuvora.
- `Synapbiz.Spec.Core` (1.2k LOC) and `Synapbiz.Spec.Brain` (2.8k LOC, the `brain.rule@1` schema, validator and the v0 bridge) already exist on nuvora `master` at version 0.1.0, with a conformance suite of 80 cases (`tests/fixtures/spec/brain/conformance.json`). They are not copied in B0 (owner decision: no second `spec-core`).
- The real `brain.rule@1` differs from the "brain.rule@1-shaped" file in `synapbiz-health-mcp` (`rules/frontdesk.patient-requirements.rule.json`): `version` instead of `revision`; required `uses` and `requires`; `body.kind`; rows carry `id`, `version`, `legacyKey`; value type tags are capitalised (`Enum`, `Boolean`); outcomes are plain field objects; facts and outcome fields are declared by a contract (`contract-<module>.json`), not under `meta`. The SzH rule file needs a one-time migration when it adopts the package.
- The only parser from stored JSON to `DecisionTable` is `DecisionDefinitionCodec` (640 LOC, public, pure) and it lives in the host project. A standalone consumer needs it; it moves in PR 2.

## 3. Design

### 3.1 Libraries and names

| Approved name | Package / assembly | Content | Depends on |
| --- | --- | --- | --- |
| `brain-engine` | `Synapbiz.Brain.Contracts` | `Nuvora.Decisions.Contracts` minus `Traces/` (28 files), plus `ContentHash` and `BrainException` | BCL only |
| `brain-engine` | `Synapbiz.Brain.Engine` | `Nuvora.Decisions.Engine` (14 files) | `Synapbiz.Brain.Contracts` |
| `brain-spec` (PR 2) | `Synapbiz.Brain.Spec` | `brain.rule@1` schema, loader to `DecisionTable` + kind descriptor from a contract file, the definition codec; written against the engine's own value types | the two above, `JsonSchema.Net` |
| `brain-host-efcore` | later | persistence, lifecycle, traces | — |

Target `net9.0`, C# 13, same analyzers and `.editorconfig` as nuvora, so a file that is clean there is clean here.

### 3.2 Public signatures

Moved types keep their namespace, name, members and ordinals. Files are copied byte-for-byte except this declared patch:

| File | Change |
| --- | --- |
| `DecisionEngineException.cs` | base class `Nuvora.Domain.Exceptions.DomainException` → `Synapbiz.Brain.BrainException` (same constructor, `ErrorCode`, `Args`) |
| `Facts/FactSet.cs`, `Facts/CanonicalFacts.cs` | `Nuvora.Domain.Common.Checksum` → `Synapbiz.Brain.ContentHash` (SHA-256 of the same bytes, lowercase hex, same `.Value`) |
| `Traces/DecisionTraceRecord.cs`, `Traces/IDecisionTraceSource.cs` | not moved: they are the host's persistence entity (tenant-scoped). They stay nuvora-side until `brain-host-efcore`. |

New types live in namespace `Synapbiz.Brain`. Renaming `Nuvora.Decisions.*` is a later mechanical step, as nuvora's plan already says.

What nuvora must do at swap time (after 2027-01-18, by the ERP plan thread, not here): reference the packages instead of the two projects, map `BrainException` in `GlobalExceptionHandler` exactly as `DomainException`, convert `ContentHash` to `Checksum` at the host boundary, keep `Traces/` in a nuvora-side assembly.

### 3.3 Parity while both copies exist

Three independent proofs, none of which touches nuvora:

1. **Source drift guard** (`tools/parity/check-source-drift.sh <nuvora-root>`): every moved file equals its nuvora original at the pinned commit; the patched files differ by exactly the committed `tools/parity/expected.patch`. `tools/parity/SOURCE.lock` records the nuvora commit.
2. **Behaviour parity, live** (`tests/parity/`): one scenario source compiled twice, once against this library and once against the nuvora projects (`NUVORA_ROOT`), each producing a list of `(scenario, canonical result)`. The test asserts the two lists are identical: every operator × fact type cell, the three hit policies, default row, null and missing facts, Money in two currencies, composition with child trace, validator and analyzer codes, golden runs, canonical facts JSON and hash, exception codes. Fails loudly when `NUVORA_ROOT` is not set; it is not part of the default solution.
3. **Behaviour parity, baseline**: the nuvora-side run is committed as `tests/parity/baseline/decisions.parity.json` (with the nuvora commit). The default unit test run executes the same scenarios against the library and compares with the baseline, so CI needs no access to nuvora.

Nuvora's engine unit tests (9 files, 2,291 LOC) move with the code, unchanged. When nuvora's cores change before the swap (three commits in the last five weeks), the drift guard reports it and the change is re-imported mechanically.

### 3.4 Architecture guards in this repo

- `Synapbiz.Brain.Contracts` and `Synapbiz.Brain.Engine` reference only the BCL and each other, with no package reference (PE metadata scan, the nuvora budget test with an empty allowance).
- The engine reads no clock, GUID generator, randomness, file or network (port of AT-DEC-02).
- `Synapbiz.Brain.Spec` (PR 2) never references a host or EF Core.

### 3.5 Provenance for SzH

`DecisionTrace` already carries what the project rule asks for: kind, matched row keys, outcome, canonical facts JSON and their hash. The rule id and version come from the `brain.rule@1` envelope and are attached by the loader in PR 2 (`VersionId`/`VersionNumber` exist on the trace).

## 4. Delivery

| PR | Content | Gate |
| --- | --- | --- |
| PR 1 | `brain-engine` (contracts + engine), moved unit tests, architecture guards, the three parity proofs, CI, docs | brief `docs/briefs/B0-PR1-brain-engine.md` |
| PR 2 | `brain-spec` for rules: schema, loader, definition codec, conformance cases for rules from nuvora's suite, contract file format for kinds | brief written after PR 1 is verified |
| PR 3 | Package publishing and SzH adoption: migrate the front-desk rule file to real `brain.rule@1`, replace `Health.Mcp.Rules` evaluator (PR in `synapbiz-health-mcp`, owned by its thread) | needs the feed decision |

Target: first published Decision Engine package on 2026-12-01.

## 5. Open decisions for the owner

1. Seed `main` in this repository (it has no branch yet) so pull requests have a base.
2. Give this repository's CI read access to `lelorenzo/nuvora` (read-only deploy key as a secret) so the live parity runs on every PR. Until then it runs in the working sessions and CI runs the baseline.
3. Distribute the packages through the private GitHub Packages NuGet feed of the account.
