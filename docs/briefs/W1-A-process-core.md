# Brief W1-A — `Synapbiz.Brain.Process` (implementer)

Repository: this checkout of `lelorenzo/synapbiz-brain`. Branch: `claude/project-thread-6zzgr7` (already checked out, based on `origin/main`). Commit on it; do **not** push, do not open a pull request, do not comment on GitHub.

## What to build

Exactly what `docs/specs/w1-process-engine/spec.md` specifies, all ten sections. Read it fully first, then `docs/specs/w1-process-engine/design.md` for context. The spec is normative: names, members, codes, identifiers and semantics are given; do not rename, add or drop any.

## Where

- `src/Synapbiz.Brain.Process/Synapbiz.Brain.Process.csproj`: `net9.0`, `IsPackable` true, `PackageId`/`AssemblyName`/`RootNamespace` `Synapbiz.Brain.Process`, a one-line `Description`, `PackageReference Include="JsonSchema.Net"` (version comes from `Directory.Packages.props`, do not add a version), `ProjectReference` to `../Synapbiz.Brain.Spec/Synapbiz.Brain.Spec.csproj` only, `EmbeddedResource Include="Schemas/brain.process.v1.json"`. Mirror `src/Synapbiz.Brain.Spec/Synapbiz.Brain.Spec.csproj`.
- `tests/Synapbiz.Brain.Process.Tests/` mirroring `tests/Synapbiz.Brain.Spec.Tests/` (same packages, `Using Include="Xunit"`), fixtures in `tests/fixtures/brain-process/`.
- Add both projects to `Synapbiz.Brain.sln` (`dotnet sln add`, under the `src` and `tests` solution folders). Do not touch `Synapbiz.Brain.Parity.sln`.
- `tests/Synapbiz.Brain.ArchitectureTests`: add the project reference, add `"Process"` to `LibraryPurityArchTests.LibraryList` and its `Library` switch, and a new `ProcessAssemblyCouplingTests.cs` modelled on `SpecAssemblyCouplingTests.cs` (allowed assemblies: the BCL, `Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine`, `Synapbiz.Brain.Spec`, and the JsonSchema.Net assemblies listed there; the project file declares exactly the package `JsonSchema.Net` and the project reference to `Synapbiz.Brain.Spec`).
- Docs: new `docs/reference/brain-process-v1.md` (how to write a process document: envelope, variables, every step kind, routing, errors, scenarios, one complete synthetic example that a documentation test loads and runs, as `tests/Synapbiz.Brain.Spec.Tests/DocumentationTests.cs` does for rules); add the library to the table and the tests list in `docs/architecture/overview.md`; add an `## Unreleased` section at the top of `CHANGELOG.md` with one bullet for W1-A; one line in `README.md` where the libraries are listed; tick the finished items in `docs/specs/w1-process-engine/tasks.md` (leave "Independent verification" unticked).

## Hard rules

1. No file under `src/Synapbiz.Brain.Contracts`, `src/Synapbiz.Brain.Engine` or `src/Synapbiz.Brain.Spec` may change. `Synapbiz.Brain.Spec` internals are not visible to the new library: write the envelope/schema validation you need inside the new project, reusing the public `BrainSpecError` type and the existing codes and texts for JSON, envelope, unknown format and schema errors (copy the texts from `src/Synapbiz.Brain.Spec/Diagnostics.cs`).
2. Purity: no `DateTime.Now/UtcNow`, `DateTimeOffset.Now/UtcNow`, `Guid.NewGuid`, `System.Random`, `System.IO`, `System.Net`, `TimeZoneInfo`, environment or culture-dependent call in `src/Synapbiz.Brain.Process`. The embedded schema must be read the way `RuleSchema.Build` does; if the purity scan flags that member, add it to the `InMemoryOnly` exemptions exactly as the existing entry is written and say so in your report. All parsing and formatting uses `CultureInfo.InvariantCulture`; all string comparison is ordinal.
3. Determinism: dictionaries that reach output (state JSON, history data, request JSON) are written in ordinal key order. No reliance on `Dictionary` enumeration order.
4. No `NoWarn`, no `#pragma warning disable`, no analyzer switched off, no test skipped. The Release build must end with the same 2 warnings the base has and none in a file you created or touched. `SuppressMessage` only where the spec section 10 allows it.
5. Synthetic data only: invented ids such as `c-1`, generic names (`check`, `send`, `front_desk`). No personal names, no clinic or vendor names, no health details beyond generic words like "missing items".
6. English everywhere (code, comments, docs); the Spanish text exists only in the `MessageEs` of diagnostics.
7. XML documentation on every public type and member (the build generates documentation files).

## Toolchain in this container

`export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1` before any `dotnet` command (SDK 10 builds the net9.0 projects). The base is green: 402 tests, 2 build warnings.

```bash
dotnet restore Synapbiz.Brain.sln
dotnet build Synapbiz.Brain.sln -c Release --no-restore
dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"
dotnet pack Synapbiz.Brain.sln -c Release --no-build -o /tmp/w1a-artifacts
```

## Acceptance

Every item of spec sections 9 and 10. Compute the line coverage of `Synapbiz.Brain.Process` from the cobertura file of the new test project and report the number.

## Stop rule

If the spec is ambiguous or contradicts the code base in a way that forces a choice the spec does not make (for example an outcome type that `Synapbiz.Brain.Spec` writes in a different JSON form than spec section 2.2, or a JsonSchema.Net API that cannot do what a rule needs), do not choose: finish everything unaffected, commit, and report the exact question with the two options you see. Do not widen the scope: no persistence, no timers beyond requests, no `triggers`, no `poll`, no delivery windows.

## Report back

Commit hashes, the list of files created and changed, test counts per project, build warning count, the coverage number, any exemption added to the purity scan, and every open question.
