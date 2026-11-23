# Brief W-A — `Synapbiz.Brain.Workbench`: headless rule workbench library

Design: `docs/specs/rule-workbench/spec.md`. Read it, `docs/reference/brain-rule-v1.md` and `docs/architecture/overview.md` first. This brief is self-contained and deterministic. You never choose between options and never widen scope. If anything differs from what is written here, or a gate cannot pass without breaking a rule of this brief, stop and report.

## Repository, branch, environment

- Target: `/home/user/synapbiz-brain`, branch `claude/project-thread-nj7hyx` (already checked out, at `main` plus the spec commit). Commit there. Do not push. Do not touch GitHub. Do not edit `.github/workflows/**`, `Synapbiz.Brain.Parity.sln`, `tools/**`, `tests/parity/**`, or any existing file under `src/Synapbiz.Brain.Contracts`, `src/Synapbiz.Brain.Engine`, `src/Synapbiz.Brain.Spec`. Do not add `InternalsVisibleTo`. Do not add a package reference to any `src` project.
- Read-only reference for wording: `/home/user/nuvora/frontend/packages/shared/src/decisions/sentences.messages.ts` and `sentences.ts`. Never write in `/home/user/nuvora`. Run no command with that directory as the working directory.
- Environment: `export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1` (SDK 10 builds the `net9.0` projects).
- Baseline before you start (verified 2026-11-22): `dotnet build Synapbiz.Brain.sln -c Release` gives 0 errors, 2 warnings; `dotnet test Synapbiz.Brain.sln -c Release --no-build` gives 9 + 167 + 226 passed.
- Everything is synthetic. No real person, clinic or product name in code, tests, fixtures or docs. English only.

## Step 1 — Projects

`src/Synapbiz.Brain.Workbench/Synapbiz.Brain.Workbench.csproj`: `net9.0`; `RootNamespace`, `AssemblyName`, `PackageId` = `Synapbiz.Brain.Workbench`; `IsPackable` true; `Description` "Headless rule workbench for brain.rule@1: plain-language rule views, simulation with a per-row explanation, test runs, revision diff and review documents, all on the Brain decision engine."; project references to `Synapbiz.Brain.Contracts`, `Synapbiz.Brain.Engine`, `Synapbiz.Brain.Spec`; no package reference.

`tests/Synapbiz.Brain.Workbench.Tests/Synapbiz.Brain.Workbench.Tests.csproj`: copy of `tests/Synapbiz.Brain.Spec.Tests/Synapbiz.Brain.Spec.Tests.csproj` plus a project reference to the new library. Fixtures are read from `tests/fixtures/` the same way `Synapbiz.Brain.Spec.Tests` reads them (follow that project's mechanism exactly).

Add both to `Synapbiz.Brain.sln` (`dotnet sln add`, under the `src` and `tests` solution folders). Add a project reference from `tests/Synapbiz.Brain.ArchitectureTests` to the new library.

## Step 2 — Public API

Namespace `Synapbiz.Brain.Workbench`. Exactly these public types and members, nothing else public. Every public member has an XML doc comment. Engine types come from the existing namespaces (`Nuvora.Decisions.Contracts.*`, `Nuvora.Decisions.Engine.*`, `Synapbiz.Brain.Spec`).

```csharp
public enum WorkbenchLocale { En = 0, Es = 1 }

public sealed record WorkbenchError(string Code, string Path, string Message);

// ---- Labels -------------------------------------------------------------
public sealed record RuleLabels
{
    public static RuleLabels Empty { get; }
    public string? Title { get; init; }
    public IReadOnlyDictionary<string, string> Facts { get; init; }          // fact name -> label
    public IReadOnlyDictionary<string, string> OutcomeFields { get; init; }  // outcome field name -> label
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values { get; init; } // fact or outcome field name -> raw value -> label
    public IReadOnlyDictionary<string, string> Rows { get; init; }           // row id -> title
    public static RuleLabels FromMeta(JsonObject meta, WorkbenchLocale locale);
}

public static class WorkbenchText
{
    public static string Humanize(string name);
    public static string FormatValue(DecisionValue? value, string name, WorkbenchLocale locale, RuleLabels? labels = null);
}

// ---- Rule view ----------------------------------------------------------
public sealed record ValueOption(string Value, string Label);
public sealed record FactView(string Name, string Label, FactType Type, bool Nullable, IReadOnlyList<ValueOption> AllowedValues);
public sealed record OutcomeFieldView(string Name, string Label, FactType Type, bool Nullable);
public sealed record ConditionView(int Index, string FactName, string FactLabel, Operator Operator, string Text);
public sealed record OutcomeValueView(string Field, string FieldLabel, string RawValue, string Text);
public sealed record RowView(string Key, string RowId, int Index, bool IsDefault, string? Title,
    IReadOnlyList<ConditionView> Conditions, IReadOnlyList<OutcomeValueView> Outcome, string Sentence);
public sealed record RuleView(string Id, long Version, string Title, string Kind, HitPolicy HitPolicy,
    IReadOnlyList<FactView> Facts, IReadOnlyList<OutcomeFieldView> OutcomeFields,
    IReadOnlyList<RowView> Rows, RowView? DefaultRow);

public static class RuleDescriber
{
    public static RuleView Describe(BrainRule rule, WorkbenchLocale locale, RuleLabels? labels = null);
}

// ---- Fact input ---------------------------------------------------------
public sealed record FactInputResult(bool IsValid, DecisionValue? Value, string? ErrorCode);
public sealed record FactSetInputResult(IReadOnlyDictionary<string, DecisionValue> Facts, IReadOnlyDictionary<string, string> Errors)
{
    public bool IsValid { get; }   // Errors.Count == 0
}

public static class FactInput
{
    public const string ErrorRequired = "workbench.fact.required";
    public const string ErrorNotAllowed = "workbench.fact.value_not_allowed";
    public const string ErrorNotABoolean = "workbench.fact.not_a_boolean";
    public const string ErrorNotAnInteger = "workbench.fact.not_an_integer";
    public const string ErrorNotADecimal = "workbench.fact.not_a_decimal";
    public const string ErrorNotADate = "workbench.fact.not_a_date";
    public const string ErrorNotMoney = "workbench.fact.not_money";
    public const string ErrorNotAReference = "workbench.fact.not_a_reference";
    public static FactInputResult Parse(FactDescriptor fact, string? text);
    public static FactSetInputResult ParseAll(DecisionKindDescriptor kind, IReadOnlyDictionary<string, string?> texts);
}

// ---- Simulation ---------------------------------------------------------
public sealed record SimulationFailure(string Code, string Message);
public sealed record ConditionCheck(int Index, string FactName, bool Passed, DecisionValue? Actual);
public sealed record RowExplanation(string Key, bool IsDefault, bool Matched, bool Selected, IReadOnlyList<ConditionCheck> Conditions);
public sealed record SimulationResult(DecisionOutcome? Outcome, DecisionTrace? Trace, SimulationFailure? Failure, IReadOnlyList<RowExplanation> Rows)
{
    public bool Succeeded { get; }   // Failure is null
}

public static class RuleSimulator
{
    public static SimulationResult Simulate(BrainRule rule, IReadOnlyDictionary<string, DecisionValue> facts, DateOnly asOf,
        IComposedFactResolver? resolver = null);
}

// ---- Tests --------------------------------------------------------------
public sealed record TestCaseView(string Name, bool FromDocument, bool Passed, IReadOnlyDictionary<string, DecisionValue> Facts,
    GoldenExpectation Expected, DecisionOutcome? Actual, IReadOnlyList<string> MatchedRowKeys, SimulationFailure? Failure);
public sealed record TestRunView(IReadOnlyList<TestCaseView> Cases, IReadOnlyList<string> UncoveredRowKeys)
{
    public int PassedCount { get; }
    public int FailedCount { get; }
    public bool IsOk { get; }      // FailedCount == 0 && UncoveredRowKeys.Count == 0
}

public static class RuleTester
{
    public static TestRunView Run(BrainRule rule, IReadOnlyList<GoldenCase>? extraCases = null, IComposedFactResolver? resolver = null);
}

// ---- Diff ---------------------------------------------------------------
public enum ChangeKind { Unchanged = 0, Added = 1, Removed = 2, Changed = 3 }
public sealed record RowChange(string Key, ChangeKind Kind, int? IndexBefore, int? IndexAfter, bool ConditionsChanged, bool OutcomeChanged);
public sealed record CaseChange(string Name, ChangeKind Kind);
public sealed record RuleDiff(long VersionBefore, long VersionAfter, bool KindChanged, bool HitPolicyChanged, bool RowOrderChanged,
    IReadOnlyList<RowChange> Rows, RowChange? DefaultRow, IReadOnlyList<CaseChange> Cases)
{
    public bool HasChanges { get; }
}
public sealed record BehaviourSample(string Name, IReadOnlyDictionary<string, DecisionValue> Facts);
public sealed record BehaviourChange(string Name, SimulationResult Before, SimulationResult After, bool Differs);

public static class RuleDiffer
{
    public static RuleDiff Diff(BrainRule before, BrainRule after);
    public static IReadOnlyList<BehaviourSample> SamplesFromGoldens(BrainRule before, BrainRule after);
    public static IReadOnlyList<BehaviourChange> CompareBehaviour(BrainRule before, BrainRule after,
        IReadOnlyList<BehaviourSample> samples, DateOnly asOf);
}

// ---- Review document ----------------------------------------------------
public enum ReviewState { Unreviewed = 0, Confirmed = 1, NeedsFix = 2, Question = 3 }
public sealed record ReviewItem(string Target, ReviewState State, string Note);
public sealed record ProposedCase(string Name, IReadOnlyDictionary<string, DecisionValue> Facts, bool ExpectNoDecision,
    string? ExpectedOutcomeJson, string? ExpectedRowId);
public sealed record RuleReview(string RuleId, long RuleVersion, string RuleContentHash, string Reviewer, DateTimeOffset CreatedAt,
    IReadOnlyList<ReviewItem> Items, IReadOnlyList<ProposedCase> ProposedCases);
public sealed record RuleReviewParseResult(RuleReview? Review, IReadOnlyList<WorkbenchError> Errors)
{
    public bool IsValid { get; }   // Errors.Count == 0
}

public static class RuleReviewJson
{
    public const string Format = "brain.rule-review@1";
    public const string ErrorJsonInvalid = "workbench.review.json_invalid";
    public const string ErrorUnknownFormat = "workbench.review.unknown_format";
    public const string ErrorMalformed = "workbench.review.malformed";
    public static string ContentHash(string ruleJson);
    public static string Serialize(RuleReview review);
    public static RuleReviewParseResult Parse(string json);
}
```

Every public static method throws `ArgumentNullException` for a null non-optional reference argument (`ArgumentNullException.ThrowIfNull`). `RuleLabels` dictionary properties default to empty dictionaries and never return null. All string comparisons and dictionaries are ordinal.

## Step 3 — Behaviour

### 3.1 Labels and text

`RuleLabels.FromMeta(meta, locale)` reads `meta["display"]["en"]` or `["es"]`. Members: `title` (string), `facts` (object of strings), `outcomes` (object of strings, goes to `OutcomeFields`), `values` (object of objects of strings), `rows` (object of strings). Any member that is absent or of another JSON kind is ignored, entry by entry; the method never throws on content and returns `Empty`-equivalent content when there is nothing to read.

`WorkbenchText.Humanize(name)`: walk the characters; `_`, `-` and `.` become a space; an uppercase letter whose previous character is a lowercase letter or a digit becomes a space followed by that letter in lower case (invariant); every other character is kept. Trim the result. Examples: `hasInsuranceOnFile` → `has insurance on file`; `to_confirm` → `to confirm`; `low-score` → `low score`; `Review` → `Review`.

Lookups: fact label = `labels.Facts[name]` or `Humanize(name)`; outcome field label = `labels.OutcomeFields[name]` or `Humanize(name)`; value label = `labels.Values[name][raw]` when present.

`WorkbenchText.FormatValue(value, name, locale, labels)`:

| Value | Text |
| --- | --- |
| null | empty string |
| `CodeValue` | value label, else the raw value |
| `EnumValue` | value label, else `Humanize(raw)` |
| `BooleanValue` | template `value.true` / `value.false` |
| `IntegerValue` | invariant digits |
| `DecimalValue` | invariant text; for `Es` the `.` is replaced by `,` |
| `MoneyValue` | template `value.money` with the amount formatted as a decimal and the currency |
| `DateValue` | `En`: `MM/dd/yyyy`; `Es`: `dd/MM/yyyy` (invariant culture custom format) |
| `ReferenceValue` | the guid in `D` format |

Templates: copy the `en` and `es` tables of `sentences.messages.ts` verbatim into an internal static class (the keys `value.deleted` and `value.placeholder` are not needed; leave them out). Filling replaces `{name}` placeholders.

### 3.2 Rule view

`RuleDescriber.Describe`: `Id` = `rule.Id`; `Version`; `Title` = `labels.Title` or `rule.Id`; `Kind` = `rule.Kind.Key.Value`; `HitPolicy` = `rule.Table.HitPolicy`.

- `Facts`: `rule.Kind.Facts` in order; `AllowedValues` = each allowed value with its value label or `Humanize(value)` (empty list when the descriptor has none).
- `OutcomeFields`: `rule.Kind.OutcomeFields` in order.
- `Rows`: `rule.Table.Rows` in order, `Index` from 0, `IsDefault` false; `RowId` = `rule.RowIdsByKey[key]` (the key itself when absent); `Title` = `labels.Rows[RowId]` or null. `DefaultRow`: the table's default row, `Index` = number of rows, `IsDefault` true.
- `Conditions`: every condition of the row in order, `Index` from 0, with `Text` rendered as in `conditionText` of `sentences.ts`, with two differences: an `Any` condition gets the `operator.any` text, and a range with no bound gets the `operator.any` text. Operand values are formatted with `FormatValue(value, factName, ...)`.
- `Outcome`: parse the row's `Outcome` JSON object. Fields in the order of `rule.Kind.OutcomeFields`, then any other member in document order; a member whose value is JSON null is skipped. `RawValue`: the string for a JSON string, the raw JSON text otherwise. `Text`: string → value label (looked up under the field name), else `Humanize(raw)` when the field type is `Enum`, else the raw string; `true`/`false` → the `value.true`/`value.false` template; number → raw text; an object with `amount` and `currency` → `value.money`; any other object or array → raw JSON text.
- `Sentence`: exactly the composition of `sentenceFor` / `renderDefaultSentence` in `sentences.ts`: conditions that are `Any` or an unbounded range are left out; no conditions left → `sentence.always`; otherwise `sentence.if` with the condition texts joined by `sentence.and`; the outcome part is `outcome.field` per outcome value joined by `sentence.list`, or `outcome.none` when there is none; the default row uses `sentence.default`.

Pinned expectations on the existing fixture `tests/fixtures/brain-spec/readiness.rule.json` with no labels:

- row `low-score`, `En`: `If score is between 0 and 49, then note is low-score.`
- row `low-score`, `Es`: `Si score está entre 0 y 49, entonces note es low-score.`
- row `late-stage`, `En`: `If stage is one of Review, Complete, then note is late-stage.`

### 3.3 Fact input

`FactInput.Parse(fact, text)`: null or whitespace-only text → valid with a null value when `fact.Nullable`, else `ErrorRequired`. Otherwise trim and parse by `fact.Type`:

| Type | Accepts | Error |
| --- | --- | --- |
| `Code` | any text → `CodeValue` | none |
| `Enum` | when `AllowedValues` is not null the text must be one of them (ordinal) → `EnumValue` | `ErrorNotAllowed` |
| `Boolean` | `true` / `false`, ignoring case | `ErrorNotABoolean` |
| `Integer` | `long`, `NumberStyles.AllowLeadingSign`, invariant | `ErrorNotAnInteger` |
| `Decimal` | `decimal`, `AllowLeadingSign | AllowDecimalPoint`, invariant | `ErrorNotADecimal` |
| `Date` | exact `yyyy-MM-dd`, invariant | `ErrorNotADate` |
| `Money` | exactly two parts split on one space: a decimal as above and three letters `A`–`Z` | `ErrorNotMoney` |
| `Reference` | `Guid.TryParse` | `ErrorNotAReference` |

`ParseAll(kind, texts)`: for each fact of `kind.Facts` in order whose `ComposedFromKind` is null, parse `texts[name]` (null when the key is absent). A valid non-null value goes to `Facts`; a valid null is left out; an invalid one puts its error code in `Errors[name]`. Keys of `texts` that name no fact are ignored.

### 3.4 Simulation

`RuleSimulator.Simulate`:

1. `rule.Compile()` and `FactSet.Build(rule.Kind, facts)`. A `DecisionEngineException` from either → result with `Failure(ErrorCode, Message)`, null outcome and trace, empty `Rows`.
2. Explanation. When the compiled table has composed facts, `Rows` is empty. Otherwise one `RowExplanation` per row of `rule.Table.Rows` in order, then the default row (`IsDefault` true) when there is one. For each condition, in order: the fact's type from `rule.Kind.Facts`; `ConditionMatcher.Create(condition, type).Matches(value)` where `value` is the fact's entry in `facts` or null; `ConditionCheck(index, factName, passed, value)`. `Matched` = every check passed (true for a row with no conditions). No other matching logic may be written.
3. `rule.Evaluate(facts, asOf, resolver)`. A `DecisionEngineException` → `Failure`, null outcome and trace, the `Rows` of step 2 with `Selected` false. Otherwise the engine's outcome and trace, and `Selected` = the outcome is `Decided` and its `MatchedRowKeys` contains the row key.

No other exception type is caught.

### 3.5 Tests

`RuleTester.Run`: the cases are `rule.Goldens` in order (`FromDocument` true) followed by `extraCases` in order (`FromDocument` false). Each case is run on its own with `GoldenRunner.Run(rule.Compile(), [case], DateOnly.MinValue, resolver)`. Its single result gives `Passed`, `Actual` and `MatchedRowKeys` (the `Decided` outcome's keys, empty otherwise). A `DecisionEngineException` for a case → `Passed` false, `Actual` null, empty keys, `Failure(ErrorCode, Message)`. `UncoveredRowKeys`: every row key in table order, then the default row key, that is in the `MatchedRowKeys` of no passing case. With no case at all, `Cases` is empty and every row is uncovered; the method does not throw. `PassedCount` / `FailedCount` count `Passed` true / false.

### 3.6 Diff

`RuleDiffer.Diff(before, after)`:

- `KindChanged`: the kind keys differ. `HitPolicyChanged`: the hit policies differ.
- Rows are paired by row key. `Rows` lists the rows of `after` in order, then the rows only in `before` in their order. Only in `after` → `Added` (`IndexBefore` null). Only in `before` → `Removed` (`IndexAfter` null). In both → `Changed` when the conditions or the outcome differ, else `Unchanged`; both indexes set. `ConditionsChanged`: the condition lists differ in length or in any position on `FactName`, `Operator`, `Value`, `UpperValue`, `LowerInclusive`, `UpperInclusive` or the `Values` sequence (element equality). Do not rely on the record equality of `Condition`: its `Values` list compares by reference. `OutcomeChanged`: the `Outcome` strings differ (ordinal).
- `RowOrderChanged`: the sequence of keys present in both, taken in `before` order, differs from the same keys in `after` order.
- `DefaultRow`: null when neither has one; otherwise a `RowChange` for it with the same rules (indexes null).
- `Cases`: goldens paired by name; the first case with a name counts, later ones with the same name are ignored. Order and kinds as for rows. `Changed` when the fact dictionaries differ (keys or values) or the expectations differ (`ExpectedNoDecision` equals `ExpectedNoDecision`; two `ExpectedDecided` are equal when `OutcomeJson` and `RowKey` are equal, ordinal).
- `HasChanges`: `KindChanged`, `HitPolicyChanged`, `RowOrderChanged`, or any row, default row or case whose kind is not `Unchanged`. A version difference alone is not a change.

`SamplesFromGoldens(before, after)`: the goldens of `after` in order (name and facts), then the goldens of `before` whose name is not among them; the first case with a name counts.

`CompareBehaviour`: for each sample in order, `RuleSimulator.Simulate` on both revisions with `asOf`. `Differs`: one succeeded and the other failed; or both failed with different codes; or both succeeded and one is `Decided` and the other `NoDecision`; or both are `Decided` with different `OutcomeJson` (ordinal) or different `MatchedRowKeys` sequences.

### 3.7 Review document

`ContentHash(ruleJson)`: `"sha256:"` followed by the lowercase hex SHA-256 of the UTF-8 bytes of the text exactly as given.

`Serialize` writes, with `Utf8JsonWriter` over an `ArrayBufferWriter<byte>` (no stream), indented, with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, members in this order:

```json
{
  "$spec": "brain.rule-review@1",
  "rule": { "id": "readiness-notes", "version": 3, "contentHash": "sha256:..." },
  "reviewer": "Reviewer One",
  "createdAt": "2026-11-22T21:00:00+00:00",
  "items": [
    { "target": "row:low-score", "state": "needsFix", "note": "..." }
  ],
  "proposedCases": [
    { "name": "...", "facts": { "score": { "type": "Integer", "value": "20" } }, "expect": { "noDecision": true } },
    { "name": "...", "facts": { }, "expect": { "row": "low-score", "outcome": { "note": "low-score" } } }
  ]
}
```

`createdAt` is round-trip format `"o"` trimmed to seconds as shown (`yyyy-MM-ddTHH:mm:sszzz`, invariant). `state` is `unreviewed`, `confirmed`, `needsFix` or `question`. `target` is free text for the library; the convention is `rule`, `row:<row id>` or `golden:<name>`. Facts are written sorted by name (ordinal) as typed values exactly as the table "Typed values" of `docs/reference/brain-rule-v1.md` defines them (a `Reference` is written as `{"type": "Reference", "value": {"guid": "<guid>"}}`). `expect`: `{"noDecision": true}` when `ExpectNoDecision`; otherwise `row` (only when `ExpectedRowId` is not null) and `outcome` (the parsed `ExpectedOutcomeJson`, written as JSON, not as a string). The library has no clock: `CreatedAt` is the caller's.

`Parse`: invalid JSON → one error `ErrorJsonInvalid` at path `""`. `$spec` other than `Format` → `ErrorUnknownFormat` at `/$spec`. Any missing member, wrong JSON kind, unknown state, unknown value type or unreadable typed value → `ErrorMalformed` with the JSON Pointer of the offending value; all such errors are collected, ordered by path (ordinal). With any error `Review` is null. A decided expectation without `outcome` is malformed. `Parse(Serialize(x))` gives a review equal to `x` member by member (`ExpectedOutcomeJson` compared after the same compact re-serialisation `Parse` produces; document this in the XML comment).

## Step 4 — Tests (`tests/Synapbiz.Brain.Workbench.Tests`)

New synthetic fixtures under `tests/fixtures/workbench/`, loaded through `BrainContract.Parse` and `BrainRuleLoader.Load` (every fixture must load with no error, its table must validate, its goldens must pass and cover every row; a test asserts this for each):

- `intake.contract.json` (`module` `sample`, kind `sample.intake`, `OnDemand`): facts of all eight types, the `Enum` one with `allowedValues`, at least three nullable; outcome fields: one `Enum` with `allowedValues`, one `Code`, one `Boolean`, one `Integer`, the last two nullable.
- `intake.first.rule.json`: `first` policy, a default row, at least six rows that together use every operator, with ranges that are closed, half open on each side and exclusive, and `meta.display.en` and `meta.display.es` covering a title, two facts, one outcome field, values of the enum fact and of the enum outcome, and one row.
- `intake.first.v2.rule.json`: version 2 of the same rule with one row added, one removed, one with a changed condition, one with a changed outcome, two rows swapped, the default row's outcome changed, one golden added, one removed, one changed.
- `intake.unique.rule.json`: `unique` policy with two rows that overlap for some fact set (to exercise the engine's `decisions.evaluation.multiple_matches` failure in simulation); its table validation may report the overlap, so this fixture is exempt from the "table must validate" assertion and that exemption is stated in the test.

Tests, at least:

- `Humanize` examples of 3.1; `FormatValue` for every value type in both locales, with and without labels.
- `FromMeta`: both locales; missing `display`; every wrong-kind member ignored.
- `Describe`: the three pinned sentences of 3.2; for `intake.first` every row's sentence in both locales as full expected strings; labels applied; default row sentence; `Any` and unbounded range condition texts; outcome order and skipped null.
- `FactInput.Parse`: for every type a valid text, an invalid text with its exact code, null/blank for nullable and for required; `ParseAll` with a mix.
- `Simulate`: for every golden of every fixture, the outcome and trace equal `rule.Evaluate` on the same facts; for every row of every fixture and every golden fact set, `RowExplanation.Matched` equals `rule.Compile()` row `Matches(facts)`; `Selected` rows equal the decided `MatchedRowKeys`; `first`, `collect` and default-row selection; a missing required fact and a mistyped fact give the engine's `decisions.facts.missing` / `decisions.facts.type_mismatch` with empty rows; the `unique` overlap gives `decisions.evaluation.multiple_matches` with two matched, unselected rows.
- `RuleTester.Run`: for every fixture with goldens, `UncoveredRowKeys` equals `rule.RunGoldens().UncoveredRowKeys` and each case's `Passed` equals the runner's; a failing extra case; an extra case with a missing required fact gives a `Failure` and does not stop the run; a rule with zero cases (construct by loading a fixture text with `goldens` emptied in the test) reports every row uncovered.
- `Diff`: `intake.first` against itself has no changes; against v2 the exact list of row, default row and case changes, `RowOrderChanged` true; a condition differing only in its `Values` content is `Changed`, and two conditions with equal `Values` content in different list instances are `Unchanged`.
- `CompareBehaviour` and `SamplesFromGoldens` on the two revisions: which samples differ, exactly; one sample that fails on one side only.
- Review: `ContentHash` of a fixed string equals a hash computed in the test; serialise a review with every state, every value type, both expectation shapes and non-ASCII text, compare with an expected JSON string, and round-trip it; every `Parse` error code with its path.
- Null-argument guards for every public method.

Do not weaken, skip or delete any existing test.

## Step 5 — Architecture tests

- `LibraryPurityArchTests`: add `Workbench` to the library list and its resolver so the purity scan covers the new assembly. No exemption may be added; if the scan flags a call, change the code.
- New `WorkbenchAssemblyCouplingTests`, modelled on `SpecAssemblyCouplingTests`: the Workbench assembly references only the base class library and the three Brain libraries; its restore graph holds no package beyond the build analyzers; its project file declares no `PackageReference`.

## Step 6 — Docs

- `docs/reference/rule-workbench.md`: what the library is for, one short example per area (describe, parse facts, simulate, run tests, diff, review), the `meta.display` convention, the review document format, the failure and error codes. All examples synthetic.
- `docs/architecture/overview.md`: add the package row and the dependency rule for it; update the test-project list.
- `README.md`: four packages. `CHANGELOG.md`: a new `## Unreleased` section at the top with one entry for W-A. `docs/specs/rule-workbench/tasks.md`: tick the W-A items except the last.
- Do not edit `docs/reference/brain-rule-v1.md`.

## Gates (run all, paste the tail of each output in your report)

```bash
export DOTNET_ROLL_FORWARD=Major DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build Synapbiz.Brain.sln -c Release 2>&1 | tee /tmp/wa-build.log | tail -5
grep -c "Synapbiz.Brain.Workbench" /tmp/wa-build.log   # then: grep "warning" /tmp/wa-build.log | grep -i workbench  → must print nothing
dotnet test Synapbiz.Brain.sln -c Release --no-build
rm -rf /tmp/wa-cov && dotnet test tests/Synapbiz.Brain.Workbench.Tests -c Release --no-build --collect:"XPlat Code Coverage" --results-directory /tmp/wa-cov
dotnet pack Synapbiz.Brain.sln -c Release --no-build -o /tmp/wa-artifacts && ls /tmp/wa-artifacts
bash tools/parity/check-source-drift.sh
git -C /home/user/nuvora status --porcelain | wc -l
```

Expected: build 0 errors, still exactly the 2 baseline warnings and none in a new file (never pass `NoWarn`, never disable an analyzer, never add a suppression attribute or pragma in the new code); the three existing test projects with their baseline counts (the architecture project grows by the new tests) and the new project all green; in the cobertura file the `line-rate` of the package `Synapbiz.Brain.Workbench` is at least `0.95`; four `.nupkg` files; the drift guard prints its usual final line; the nuvora status count is the same as before you started (record it first).

## Commits and report

Commit on the branch in small steps with messages in English, ending each message with:

```
Co-Authored-By: Claude <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01VN2ND7Ahy1HAQ2DW9D8MUU
```

Report: the commit list, the tail of every gate, the coverage number, the public API as built (`grep -rn "^public\|^    public" src/Synapbiz.Brain.Workbench`), and every place where you could not follow this brief literally.
