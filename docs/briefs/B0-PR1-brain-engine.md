# Brief B0-PR1 — `brain-engine`: move the Decision Engine cores into `synapbiz-brain`

Design: `docs/specs/b0-decision-engine-extraction/spec.md` (read it first). This brief is self-contained and deterministic. You never choose between options and never widen scope. If anything differs from what is written here, stop and report.

## Repositories and branch

- Target: `lelorenzo/synapbiz-brain`, local `/home/user/synapbiz-brain`, branch `claude/project-thread-4wlciu`. Commit there. Do not push; the owning thread pushes and opens the PR. Do not comment, open issues or PRs on GitHub.
- Source: `lelorenzo/nuvora`, local `/home/user/nuvora`, **read-only**: never commit, branch, stash, reset, build output into it outside `bin/` and `obj/`, or push. Pinned commit `054828b115de569b98b220f10b83c4b35f83a9ea`. First command: `git -C /home/user/nuvora rev-parse HEAD` must print that commit and `git -C /home/user/nuvora status --porcelain -- src tests` must print nothing. Otherwise stop.

## Environment

- `dotnet --version` prints `10.x` (Ubuntu package `dotnet-sdk-10.0`). Export `DOTNET_ROLL_FORWARD=Major` and `DOTNET_CLI_TELEMETRY_OPTOUT=1` for every command. Projects target `net9.0`.
- Do not add a `global.json`.
- NuGet restore uses nuget.org only.

## Step 1 — Repository skeleton

Create:

- `.gitignore` (standard `dotnet new gitignore` output).
- `.editorconfig`: byte copy of `/home/user/nuvora/.editorconfig`.
- `Directory.Build.props` with exactly: `LangVersion` 13.0, `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` false, `WarningLevel` 5, `NoWarn` `$(NoWarn);CS1591`, `GenerateDocumentationFile` true, `AnalysisMode` AllEnabledByDefault, `EnforceCodeStyleInBuild` true, `Company` VigiaSoft LLC, `Version` 0.1.0, and the two analyzer package references (`Microsoft.CodeAnalysis.NetAnalyzers`, `SonarAnalyzer.CSharp`) with `PrivateAssets` all, as in nuvora's `Directory.Build.props`.
- `Directory.Packages.props` (`ManagePackageVersionsCentrally` true) with exactly these versions: `Microsoft.CodeAnalysis.NetAnalyzers` 10.0.401, `SonarAnalyzer.CSharp` 10.8.0.113526, `xunit` 2.9.2, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.12.0, `FluentAssertions` 7.0.0, `coverlet.collector` 10.0.1.
- `Synapbiz.Brain.sln` containing the four projects of steps 2, 3 and 5 (not the parity projects of step 6).
- `README.md`: one paragraph on what the repository is, the build and test commands of "Gates", and a link to the spec. English. No mention of customers, partners or other products beyond "Synapbiz".

## Step 2 — `src/Synapbiz.Brain.Contracts`

- `Synapbiz.Brain.Contracts.csproj`: `net9.0`, `RootNamespace` `Nuvora.Decisions.Contracts`, `AssemblyName` and `PackageId` `Synapbiz.Brain.Contracts`, `IsPackable` true, description "Public surface of the Brain decision engine: decision table model, typed values, kinds, outcome, trace and validation contracts." No project or package references besides the analyzers from `Directory.Build.props`.
- Copy byte-for-byte every `*.cs` under `/home/user/nuvora/src/Shared/Nuvora.Decisions.Contracts/` (excluding `bin/`, `obj/`) into `src/Synapbiz.Brain.Contracts/Decisions/`, keeping relative paths, **except** the directory `Traces/` (2 files), which is not copied. Expected: 28 files.
- Add two new files in namespace `Synapbiz.Brain`:
  - `BrainException.cs`: `public abstract class BrainException : Exception` with the same members and constructor as `/home/user/nuvora/src/Nuvora.Domain/Exceptions/DomainException.cs` (`ErrorCode`, `Args`, protected constructor `(string errorCode, string message, IReadOnlyDictionary<string, object?>? args = null, Exception? innerException = null)`, same argument check).
  - `ContentHash.cs`: `public sealed class ContentHash : IEquatable<ContentHash>` with `public string Value { get; }` (64 lowercase hex characters), `public static ContentHash FromBytes(ReadOnlySpan<byte> bytes)` computing SHA-256 exactly as `Checksum.FromBytes` in `/home/user/nuvora/src/Nuvora.Domain/Common/Checksum.cs`, ordinal value equality on `Value`, `GetHashCode`, `==`/`!=`, and `ToString()` returning `Value`. Private constructor. Nothing else.
- Patch one copied file: in `Decisions/DecisionEngineException.cs` replace `using Nuvora.Domain.Exceptions;` with `using Synapbiz.Brain;` and the base type `DomainException` with `BrainException`. No other edit (comments mentioning `DomainException` stay).

## Step 3 — `src/Synapbiz.Brain.Engine`

- `Synapbiz.Brain.Engine.csproj`: `net9.0`, `RootNamespace` `Nuvora.Decisions.Engine`, `AssemblyName` and `PackageId` `Synapbiz.Brain.Engine`, `IsPackable` true, description "Pure Brain decision table engine: compiler, evaluator, validator, overlap and coverage analysis, golden runner. No persistence, no DI, no clock, IO or randomness." One project reference: `../Synapbiz.Brain.Contracts/Synapbiz.Brain.Contracts.csproj`.
- Copy byte-for-byte every `*.cs` under `/home/user/nuvora/src/Shared/Nuvora.Decisions.Engine/` into `src/Synapbiz.Brain.Engine/Decisions/`, keeping relative paths. Expected: 14 files.
- Patch two copied files, `Decisions/Facts/FactSet.cs` and `Decisions/Facts/CanonicalFacts.cs`: replace `using Nuvora.Domain.Common;` with `using Synapbiz.Brain;` and every identifier `Checksum` used as a type or in `Checksum.FromBytes(...)` with `ContentHash`. Keep the usings sorted as the file's existing order requires only if the build reports an ordering diagnostic. No other edit.

## Step 4 — Drift guard

- `tools/parity/SOURCE.lock`: one line, the pinned nuvora commit.
- `tools/parity/expected.patch`: the output of `diff -u` between each of the three patched files and its nuvora original, concatenated in this order: `DecisionEngineException.cs`, `Facts/CanonicalFacts.cs`, `Facts/FactSet.cs`, with labels `a/<path relative to the nuvora project>` and `b/<same path>`.
- `tools/parity/check-source-drift.sh <nuvora-root>` (bash, `set -euo pipefail`): (1) fail unless `git -C <nuvora-root> rev-parse HEAD` equals `SOURCE.lock`; (2) for every `*.cs` of the two nuvora projects (excluding `bin/`, `obj/`, and `Traces/` of Contracts) require the file to exist here and be byte-identical, except the three patched files; (3) require no extra `*.cs` under either `Decisions/` directory; (4) regenerate the patch for the three files and require it to be byte-identical to `expected.patch`. Print one line per offender and exit 1 on any; print `source drift: none (44 files, 3 patched, 2 excluded)` and exit 0 otherwise.

## Step 5 — Tests

`tests/Synapbiz.Brain.Engine.Tests` (xunit, FluentAssertions, coverlet.collector, `IsPackable` false, references both library projects):

- Copy byte-for-byte from `/home/user/nuvora/tests/Nuvora.UnitTests/Decisions/`: `DecisionKindKeyTests.cs` and every file under `Engine/` **except** `Engine/DecisionTraceMappingTests.cs`. Expected: 9 files. Keep namespaces. If a copied test does not compile without an edit, stop and report the file and the error.
- New `ContentHashTests.cs`: `FromBytes` of the UTF-8 bytes of `abc` has `Value` `ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad`; equality and `ToString`.
- New `BrainExceptionTests.cs`: `DecisionEngineException` is a `BrainException`, exposes `ErrorCode` and `Args`, empty code throws `ArgumentException`.
- New `ParityBaselineTests.cs`: see step 6.

`tests/Synapbiz.Brain.ArchitectureTests` (xunit, FluentAssertions, references both library projects):

- `CoreAssembliesReferenceOnlyTheBclAndEachOther`: port of `CoreAssembliesUseOnlyTheFiveApprovedExternalTypeReferences` from `/home/user/nuvora/tests/Nuvora.ArchitectureTests/Brain/BrainCoreCouplingBudgetTests.cs` with cores `Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine` and an **empty** allowance.
- `CoreProjectsDeclareNoPackageReference`: both `.csproj` contain no `PackageReference` and no `Reference`; the Engine has exactly one `ProjectReference` (Contracts); Contracts has none.
- `EngineNeverReadsAClockGuidRandomnessOrPerformsIoOrNetwork`: port of `AT_DEC_02_...` and its helper `CallsForbiddenApi` from `/home/user/nuvora/tests/Nuvora.ArchitectureTests/DecisionsArchTests.cs`, same forbidden list, same positive control.
- Mutation-verify each of the three: introduce the violation, see the test fail, revert. Record the three RED outputs in the hand-back. No mutation is committed.

## Step 6 — Behaviour parity

Directory `tests/parity/`:

- `Scenarios/*.cs`: one shared source, namespace `Synapbiz.Brain.Parity`, public entry `public static class ParityRunner { public static IReadOnlyList<KeyValuePair<string, string>> Run(); }`. It uses only API present in both copies (types under `Nuvora.Decisions.Contracts.*` and `Nuvora.Decisions.Engine.*`; hashes only through `.Value`; exceptions only as `DecisionEngineException.ErrorCode`). Each scenario has a unique, stable name and a result string built with invariant culture. A thrown `DecisionEngineException` is recorded as `error:<ErrorCode>`; any other exception is not caught. Scenarios, in this order:
  1. `matcher/<FactType>/<Operator>/<case>`: for every one of the 8 fact types × 9 operators, `ConditionMatcher.Create` then `Matches` against: a matching value, a non-matching value, and `null`. Operands: Code `"A-1"`/`"B-2"`, prefix `"A-"`; Enum `"A"`/`"B"`; Boolean true/false; Integer 10/20, range [5,15]; Decimal 1.50/2.5, range [1.0,2.0], plus equality case `1.50` vs `1.5`; Money 10.00 USD/20.00 USD, range [5,15] USD, plus a 10.00 EUR fact against every USD condition; Date 2026-01-10/2026-02-10, range [2026-01-01,2026-01-31], each range also with both bounds exclusive at the bound value; Reference two fixed GUIDs. Result: `true`, `false` or `error:<code>`.
  2. `table/<policy>/<case>`: for Unique, First and Collect: single match, two matching rows, no match without default row, no match with default row, explicit null fact vs missing nullable fact, missing required fact, fact of the wrong type. Kind: `synthetic.parity` with one fact per fact type (all nullable except `code`), two outcome fields. Path: `TableCompiler.Compile` → `FactSet.Build` → `TableEvaluator.Evaluate` with `asOf` 2026-11-18. Result: `IsDecided|RowKey|MatchedRowKeys joined by ,|OutcomeJson|trace.FactsJson|trace.FactsHash`.
  3. `composition/<case>`: a kind with a fact composed from a second kind, with a resolver that evaluates the child table: resolved value, child trace present, and no resolver supplied.
  4. `validate/<case>`: `TableValidator.Validate` for: valid table, duplicate row key, unknown fact, cell type mismatch, operator not allowed, overlap under Unique, unreachable row under First, incomplete coverage with `RequiresTotalCoverage`, Money currency mismatch, invalid outcome JSON. Result: sorted error codes, then `|`, then sorted warning codes.
  5. `goldens/<case>`: `GoldenRunner.Run` for: all pass, one failing, one row uncovered, empty set. Result: `IsOk|failed names|uncovered row keys|issue codes`.
  6. `facts/<case>`: `CanonicalFacts.ToCanonicalJson` and `.Hash(...).Value` for: all eight types, keys in two different insertion orders (same result expected), decimals `1.50` and `1.5`, a facts set above `DecisionKindDescriptor.DefaultMaxFactsBytes` through `EnsureWithinCap`.
  
  Take every call shape from the copied unit tests; do not invent API. Minimum 200 scenarios; duplicate names fail the run.
- `Driver.Library/Driver.Library.csproj`: library, `AssemblyName` `Synapbiz.Brain.Parity.Library`, compiles `../Scenarios/*.cs`, references the two library projects.
- `Driver.Nuvora/Driver.Nuvora.csproj`: library, `AssemblyName` `Synapbiz.Brain.Parity.Nuvora`, compiles `../Scenarios/*.cs`, references `$(NUVORA_ROOT)/src/Shared/Nuvora.Decisions.Engine/Nuvora.Decisions.Engine.csproj` and the nuvora Contracts project. A target fails the build with the message `NUVORA_ROOT is not set: the live parity needs a checkout of the source repository.` when the property is empty.
- `Synapbiz.Brain.Parity.Tests/` (xunit): references both drivers with `Aliases="lib"` and `Aliases="erp"`; one test asserts the two `Run()` lists are equal element by element (name and result) and reports the first 20 differing names; a second test writes nothing and asserts the `erp` list equals `tests/parity/baseline/decisions.parity.json`.
- `Synapbiz.Brain.Parity.sln` with the three parity projects and the two library projects.
- `tests/parity/baseline/decisions.parity.json`: `{ "source": "<nuvora commit>", "scenarios": [{ "name": ..., "result": ... }] }`, produced by a small console entry in `Driver.Nuvora` or a test switch `PARITY_WRITE_BASELINE=1`, never edited by hand.
- `ParityBaselineTests.cs` in `Synapbiz.Brain.Engine.Tests`: compiles `../parity/Scenarios/*.cs` by link, runs `ParityRunner.Run()` against the library and asserts equality with the committed baseline.

## Step 7 — CI and docs

- `.github/workflows/ci.yml`: on push and pull_request; `permissions: contents: read`; `actions/checkout@34e114876b0b11c390a56381ad16ebd13914f8d5` and `actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9` with `dotnet-version: 9.0.x`; then `dotnet restore Synapbiz.Brain.sln`, `dotnet build Synapbiz.Brain.sln -c Release --no-restore`, `dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"`, `dotnet pack Synapbiz.Brain.sln -c Release --no-build -o artifacts`, upload `artifacts/*.nupkg` with `actions/upload-artifact` pinned by commit SHA of its v4 line. No publish step. No secrets.
- `docs/architecture/overview.md`: the libraries table and dependency rule of spec §3.1, the declared patch of §3.2, the three parity proofs of §3.3 with their commands.
- `docs/specs/b0-decision-engine-extraction/plan.md` and `tasks.md`: mark the PR 1 tasks done.
- `CHANGELOG.md`: `## 0.1.0 (unreleased)` with one entry for this PR.

## Gates (run all, in this order, paste the summary lines in the hand-back)

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
bash tools/parity/check-source-drift.sh /home/user/nuvora
dotnet build Synapbiz.Brain.sln -c Release            # 0 errors
dotnet test  Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"
NUVORA_ROOT=/home/user/nuvora dotnet test Synapbiz.Brain.Parity.sln -c Release
dotnet pack  Synapbiz.Brain.sln -c Release --no-build -o artifacts   # 2 packages; artifacts/ is git-ignored
git -C /home/user/nuvora status --porcelain -- src tests             # prints nothing
```

Warnings: new files must build with 0 warnings. For copied files, list every warning the build prints for them; each must also appear for the same file in the nuvora build triggered by the parity solution. A warning that appears only here is a failure. Never pass `NoWarn`, never disable analyzers, never add a suppression to a copied file.

## Acceptance

- Drift guard prints `source drift: none (44 files, 3 patched, 2 excluded)`.
- All moved tests pass unchanged; the three architecture tests pass and were mutation-verified.
- Live parity and baseline parity pass with at least 200 scenarios.
- Line coverage of the two library assemblies is reported (this PR sets the base).
- `git status` in nuvora is clean; nothing under `/home/user/nuvora` is committed or modified outside `bin/` and `obj/`.
- No test is skipped, disabled or filtered out.

## Stop rules

- The nuvora commit or working tree differs from the pin.
- A copied file needs any edit outside the declared patch to compile, or a copied test needs any edit.
- Live parity reports a difference: report the scenario names and both results; never change an expected value, a scenario or the library to hide it.
- A warning appears here that does not appear in nuvora for the same file.
- Anything requires a package beyond the pinned list, a change in nuvora, or a decision this brief does not make.

## Hand-back

Commit list, the gate summary lines, scenario count, coverage numbers, the warnings list, the three mutation RED outputs, and every deviation or open question. Say plainly what was not done.
