# Brief W1-B — `Synapbiz.Brain.Host.Postgres` (implementer)

Repository: this checkout of `lelorenzo/synapbiz-brain`. Branch: `claude/project-thread-6zzgr7` (checked out, based on `origin/main` at 6e78b56 plus the spec commit). Commit on it; do **not** push, do not open a pull request, do not use any GitHub tool.

## What to build

Exactly what `docs/specs/w1-process-engine/spec-b-postgres-host.md` specifies, all nine sections. Read it fully first. Then read, for the interpreter you are hosting: `docs/reference/brain-process-v1.md`, `docs/specs/w1-process-engine/spec.md` (sections 3 to 6) and the public types in `src/Synapbiz.Brain.Process` (`BrainProcess`, `ProcessState`, `ProcessTriggers.cs`, `ProcessRequests.cs`, `ProcessHistory.cs`). Do not change anything under `src/Synapbiz.Brain.Process` or the other four existing `src` projects. If the interpreter's public API lacks something the host needs, stop and report (see the stop rule).

The spec is normative for the public API, the tables, the transaction shape, the pump order, the trigger mapping and the tests. The internal structure (classes, the members of the internal `IProcessStore`, SQL text) is yours, within those constraints.

## Where

- `src/Synapbiz.Brain.Host.Postgres/Synapbiz.Brain.Host.Postgres.csproj`: `net9.0`, packable, `PackageId`/`AssemblyName` `Synapbiz.Brain.Host.Postgres`, one-line `Description`, `ProjectReference` to `../Synapbiz.Brain.Process/Synapbiz.Brain.Process.csproj`, package references (no versions in the csproj): `Npgsql`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`. SQL scripts as embedded resources under `Migrations/`.
- `Directory.Packages.props`: add, grouped under comments like the existing ones, `Npgsql` 9.0.4, the four `Microsoft.Extensions.*` packages at 9.0.0, and for tests `Microsoft.Extensions.TimeProvider.Testing` 9.0.0, `Testcontainers.PostgreSql` 4.10.0, plus `Microsoft.Extensions.DependencyInjection` 9.0.0 and `Microsoft.Extensions.Logging` 9.0.0 if the tests need a container and a logger factory. Nothing else. If one of these versions does not restore, stop and report rather than picking another.
- `tests/Synapbiz.Brain.Host.Postgres.Tests/` in the style of `tests/Synapbiz.Brain.Process.Tests/`. Reuse the W1-A fixtures by path (`tests/fixtures/brain-process/`); new fixtures go in `tests/fixtures/brain-host/`.
- Add both projects to `Synapbiz.Brain.sln` with `dotnet sln add` (under the `src` and `tests` solution folders). Do not touch `Synapbiz.Brain.Parity.sln`, `.github/`, `Directory.Build.props` or `tools/`.
- `tests/Synapbiz.Brain.ArchitectureTests`: project reference and a new `HostAssemblyCouplingTests.cs` per spec section 8, modelled on `ProcessAssemblyCouplingTests.cs`. Do **not** add the host to `LibraryPurityArchTests`.
- Docs: new `docs/reference/process-host-postgres.md` (how a consumer registers the host, writes a port, a catalog and a protector, runs the migrator, starts an instance, publishes an event, completes a task; the delivery guarantees and what at-most-once means after a crash; the tables; what is logged). Add the library to the table, the dependency paragraph and the tests list of `docs/architecture/overview.md`; one bullet under `## Unreleased` in `CHANGELOG.md` (keep the existing bullets); one line in `README.md`'s library list and the package count; `docs/consuming.md` package list; in `docs/specs/w1-process-engine/plan.md` set W1-A to merged and W1-B to in progress; a `tasks-b.md` next to the spec with the task list, ticked as you finish (leave "Independent verification" unticked).

## Hard rules

1. Correctness of concurrency and crash behaviour is the point of this PR. Follow the single transaction shape of spec section 4 literally: instance row locked first, work item re-validated under that lock. No other code path may write an instance or its timer, wait, task, effect or inbox rows.
2. All SQL is parameterised. The schema name is validated against the pattern in the spec and quoted; no other identifier comes from input.
3. Time only from the injected `TimeProvider`; never `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, `now()`, `current_timestamp` or `Stopwatch`-based decisions. The port timeout uses a `CancellationTokenSource` created with the `TimeProvider`.
4. No protected value in logs or exception messages (spec section 3). Log ids, kinds and codes only. Use `LoggerMessage` source-generated methods or the existing style that satisfies the analyzers.
5. No `NoWarn`, no `#pragma warning disable`, no analyzer switched off, no skipped or conditional test (`Skip`, `SkippableFact`, early `return` when no database). The Release build must end with exactly the 2 base warnings (CS8604 in `OverlapAnalyzer.cs`). `SuppressMessage` only as spec section 9 allows.
6. Synthetic data only: invented ids (`t-1`, `c-1`), generic names. No personal, clinic or vendor names, no health details.
7. English everywhere. XML documentation on every public type and member.
8. Tests never sleep and never assume timing, except the two concurrency tests, which use real parallel tasks without timing assumptions and must pass reliably: run each of them 20 times in a row before you report (`dotnet test --filter` in a shell loop) and report the result.

## Toolchain in this container

`export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1` before any `dotnet` command (SDK 10 builds the net9.0 projects).

A local PostgreSQL 16 is running. For every test run export:

```bash
export BRAIN_TEST_POSTGRES='Host=localhost;Port=5432;Database=brain_test;Username=brain;Password=brain;Include Error Detail=true'
```

If it is not accepting connections, start it with `service postgresql start`. Docker is not available here, so the Testcontainers fallback cannot run in this container: implement it per the spec and keep it small (it is exercised in CI).

```bash
dotnet restore Synapbiz.Brain.sln
dotnet build Synapbiz.Brain.sln -c Release --no-restore
dotnet test Synapbiz.Brain.sln -c Release --no-build --collect:"XPlat Code Coverage"
dotnet pack Synapbiz.Brain.sln -c Release --no-build -o /tmp/w1b-artifacts
```

The base is green: 1228 tests (Architecture 17, Engine 167, Spec 226, Workbench 299, Process 519), 2 build warnings, five packages.

## Acceptance

Every item of spec sections 8 and 9, with the line coverage of `Synapbiz.Brain.Host.Postgres` computed from the cobertura file of the new test project and reported.

## Stop rule

If the spec is ambiguous, contradicts the interpreter's actual behaviour, or needs something the public API of `Synapbiz.Brain.Process` does not offer, do not choose and do not change the interpreter: finish everything unaffected, commit, and report the exact question with the options you see. Do not widen the scope (no schedules, no retention, no HTTP, no metrics, no second provider).

## Report back

Commit hashes, files created and changed, test counts per project, build warning count, coverage, the result of the 20-run loops of the two concurrency tests, every deviation from the spec and every open question.
