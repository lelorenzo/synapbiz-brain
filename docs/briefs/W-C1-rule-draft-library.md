# Brief W-C1 — Guided editing, headless part: draft revisions and proposals

Package: `Synapbiz.Brain.Workbench` (existing). No new package, no new dependency.
Spec: `docs/specs/rule-workbench/spec.md` (owner decision 3: guided editing that produces a draft revision with its diff; the expert never edits JSON and never activates anything).
Follows: W-A (merged), W-B and W-B2 (merged). The panels for this (W-C2, a "Changes" tab and row forms) are a later brief and are out of scope here.

## 1. Goal

Give a host everything it needs to let a domain expert change a rule through forms:

1. A closed set of **edits** (typed records) that describe a change in the expert's terms.
2. A **drafter** that applies edits to the exact text of a `brain.rule@1` document and returns the text of a draft revision. It is a pure text-to-text function. It never loads, validates or evaluates: the host loads the draft with `BrainRuleLoader.Load` and its own contracts, exactly as it loads any rule, so a draft is judged by the same code as production.
3. A **choice catalogue**: for a loaded rule, which facts, operators and value types a form may offer, so a form cannot build an edit the contract would refuse.
4. A **change view**: the existing `RuleDiffer` output turned into sentences and a one-line summary, per locale.
5. A **proposal document**, `brain.rule-proposal@1`: the message that carries a draft from the expert to the rule maintainers, bound to the base revision by content hash, like `brain.rule-review@1`.

Nothing here changes a decision, activates a rule or keeps state.

## 2. Public API (namespace `Synapbiz.Brain.Workbench`)

Names are binding. Members not listed are internal. XML docs on every public member.

### 2.1 Edits

```csharp
public abstract record RuleEdit;

// A condition on one fact of one row. Replaces the row's condition on that fact, or adds it when the row has none.
public sealed record SetCondition(string RowKey, RowCondition Condition) : RuleEdit;

// Removes the row's condition on that fact (the fact then accepts any value). No-op error when the row has none.
public sealed record ClearCondition(string RowKey, string Fact) : RuleEdit;

// Sets one outcome field of a row. Value null removes the field (only valid for an optional field; the loader decides).
public sealed record SetOutcome(string RowKey, string Field, DecisionValue? Value) : RuleEdit;

// Adds a row. AfterRowKey null puts it first; otherwise right after that row. Conditions may be empty.
public sealed record AddRow(string RowKey, string? AfterRowKey, IReadOnlyList<RowCondition> Conditions,
    IReadOnlyDictionary<string, DecisionValue> Outcome) : RuleEdit;

public sealed record RemoveRow(string RowKey) : RuleEdit;

// Moves a row. AfterRowKey null puts it first.
public sealed record MoveRow(string RowKey, string? AfterRowKey) : RuleEdit;

// Sets one outcome field of the default row. Error when the rule has no default row.
public sealed record SetDefaultOutcome(string Field, DecisionValue? Value) : RuleEdit;

// Changes what a test case expects: the row key (null = the default row or no row) and the outcome.
public sealed record SetCaseExpectation(string CaseName, string? RowKey,
    IReadOnlyDictionary<string, DecisionValue> Outcome) : RuleEdit;

// Adds a test case, or removes one by name.
public sealed record AddCase(ProposedCase Case) : RuleEdit;
public sealed record RemoveCase(string CaseName) : RuleEdit;

// Sets or clears (Text null) the display name of a row for one locale, under meta.display.<locale>.rows.
public sealed record SetRowLabel(string RowKey, WorkbenchLocale Locale, string? Text) : RuleEdit;
```

`RowCondition` is the engine's own condition type as `RuleView`/`ConditionView` already expose it (reuse the contract type the projection produces; do not invent a parallel one). If the engine type cannot express the fact name together with the operator and operands in one value, define `public sealed record RowCondition(string Fact, <engine condition type> Condition)` and say so in the report.

Row keys are the rows' `legacyKey` (the key `BrainRule.RowIdsByKey` and `RowView` use).

### 2.2 Drafter

```csharp
public sealed record RuleDraftResult(string? DraftJson, IReadOnlyList<WorkbenchError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class RuleDrafter
{
    public static RuleDraftResult Apply(string ruleJson, IReadOnlyList<RuleEdit> edits);
}
```

Rules of `Apply`:

- Parses `ruleJson` as JSON. Not JSON, or not an object with `$spec` equal to `brain.rule@1` and a `body.rows` array: one error `workbench.draft.baseNotReadable`, no draft.
- Applies the edits in order to a copy. An edit that cannot be applied stops the run with one error and no draft. Codes: `workbench.draft.rowNotFound` (args: key), `workbench.draft.rowKeyTaken` (AddRow with an existing key), `workbench.draft.rowKeyNotValid` (AddRow key that is not a valid section id; use the same rule as `SectionIdentity.IsId`), `workbench.draft.conditionNotFound` (ClearCondition), `workbench.draft.noDefaultRow`, `workbench.draft.caseNotFound`, `workbench.draft.caseNameTaken`, `workbench.draft.moveOntoItself` (MoveRow after itself), `workbench.draft.labelNotValid` (empty or whitespace text; pass null to clear).
- An empty edit list is valid and returns a draft that differs from the base only by the version rule below.
- **Version rule.** The draft's document `version` is the base version plus 1. A row touched by `SetCondition`, `ClearCondition` or `SetOutcome` gets its row `version` plus 1, once, however many edits touch it. A new row has `id` and `legacyKey` equal to the given key and `version` 1. A moved row keeps its version. The default row follows the same once-only bump on `SetDefaultOutcome`.
- **Shape rule.** Conditions, typed values, outcomes and test cases are written in exactly the JSON shape `RuleProjection` reads (tagged values, `values` for sets, `upper` / `lowerInclusive` / `upperInclusive` for ranges, `expect.row` and `expect.outcome` for cases). Reuse the writer in `Synapbiz.Brain.Spec` (`TaggedValues`, `Json`) where it is already public. Where it is internal, do not widen `Synapbiz.Brain.Spec`: write the shape in the drafter and pin it with the round-trip tests of section 4. Report which path you took.
- **Nothing else changes.** Every property the edits do not name is kept byte-for-byte in value and order (`$spec`, `id`, `uses`, `requires`, `meta` outside the touched label, unknown properties, untouched rows and cases). Output is indented with two spaces and `\n` line ends, UTF-8 without escaping non-ASCII letters.
- Deterministic: the same input gives the same text. No clock, no random, no IO.
- `RemoveRow` also removes the row's label under every `meta.display.<locale>.rows`. It does not touch test cases that expect the removed row: those fail when the host runs them, which is the signal the expert needs.

### 2.3 Choice catalogue

```csharp
public sealed record FactChoices(string Fact, string Label, string ValueType,
    IReadOnlyList<string> Operators, IReadOnlyList<ValueChoice> Values);   // Values: enum members, else empty
public sealed record ValueChoice(DecisionValue Value, string Label);
public sealed record OutcomeChoices(string Field, string Label, string ValueType, bool Required,
    IReadOnlyList<ValueChoice> Values);
public sealed record RuleChoices(IReadOnlyList<FactChoices> Facts, IReadOnlyList<OutcomeChoices> Outcomes,
    bool HasDefaultRow, string HitPolicy);

public static class RuleChoiceCatalog
{
    public static RuleChoices For(BrainRule rule, RuleLabels labels, WorkbenchLocale locale);
}
```

Everything comes from `rule.Kind` (the kind descriptor) and `rule.Table`: facts and outcome fields in the descriptor's order, the operators the descriptor allows for each fact, enum members where the descriptor declares them. If the descriptor does not carry allowed operators per fact, derive them from the value type with one internal table that mirrors what `ConditionMatcher.Create` accepts, and cover every pair with a test that calls `ConditionMatcher.Create`. Labels come from `labels` with the existing humanised fallback.

### 2.4 Change view

```csharp
public enum RowChangeTag { Added, Removed, Changed, Moved }

public sealed record RowChangeView(string Key, string Label, RowChangeTag Tag,
    int? NumberBefore, int? NumberAfter, string? SentenceBefore, string? SentenceAfter);

public sealed record CaseOutcomeChange(string Name, string Before, string After);

public sealed record RuleChangeView(string Summary, IReadOnlyList<RowChangeView> Rows,
    bool HitPolicyChanged, bool DefaultRowChanged, IReadOnlyList<CaseOutcomeChange> CasesThatChange);

public static class RuleChangeDescriber
{
    public static RuleChangeView Describe(BrainRule before, BrainRule after,
        RuleLabels labelsBefore, RuleLabels labelsAfter, WorkbenchLocale locale,
        IReadOnlyList<BehaviourSample>? extraSamples = null);
}
```

- Built only on `RuleDiffer.Diff`, `RuleDiffer.SamplesFromGoldens`, `RuleDiffer.CompareBehaviour`, `RuleDescriber.Describe` and `WorkbenchText`. No comparison logic of its own.
- `Rows` are in the new order, with removed rows placed where they were. A row both changed and moved is `Changed` and carries both numbers. Numbers are 1-based positions.
- `Summary`, Spanish: `Este borrador cambia {n} fila(s), agrega {n} y quita {n}.` with correct singular and plural and "no cambia ninguna fila" / "no agrega ninguna" / "no quita ninguna" for zero; moved-only rows add `Mueve {n} fila(s).`; no change at all: `Este borrador no cambia la regla.` English equivalents: `This draft changes {n} row(s), adds {n} and removes {n}.`, `This draft does not change the rule.` Write the templates beside the existing ones in `SentenceTemplates` and pin every count form in tests.
- `CasesThatChange`: one entry per sample whose outcome differs, `Before` and `After` formatted the way a row's outcome reads in a sentence (labelled values). A sample one revision refuses reads `no result` / `sin resultado` on that side. Samples are the test cases of both revisions (by name; the newer facts win) plus `extraSamples`.

### 2.5 Proposal document

```csharp
public sealed record RuleProposal(
    string RuleId, long BaseVersion, string BaseContentHash,
    string Author, DateTimeOffset CreatedAt, string Note, string DraftJson);

public sealed record RuleProposalParseResult(RuleProposal? Proposal, IReadOnlyList<WorkbenchError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class RuleProposalJson
{
    public const string Spec = "brain.rule-proposal@1";
    public static string Serialize(RuleProposal proposal);
    public static RuleProposalParseResult Parse(string json);
}
```

- Shape: `{ "$spec", "rule": { "id", "baseVersion", "baseContentHash" }, "author", "createdAt", "note", "draft": <the draft document as a JSON object, not a string> }`. `BaseContentHash` is `RuleReviewJson.ContentHash(baseText)`; the caller computes it.
- `Serialize` refuses (ArgumentException) a `DraftJson` that is not a JSON object. `CreatedAt` is supplied by the caller (no clock here) and written as ISO 8601 with offset.
- `Parse` returns typed errors, never throws on bad input: `workbench.proposal.notJson`, `workbench.proposal.specNotSupported`, `workbench.proposal.fieldMissing` (args: path), `workbench.proposal.fieldNotValid` (args: path). `DraftJson` of a parsed proposal is the draft object re-serialised with the drafter's formatting.
- Follow `RuleReviewJson` for style, error handling and tests.

### 2.6 Addendum (design review)

Two additions, in the same namespace, built after the rest of this brief.

**Deciding rows in the change view.** `CaseOutcomeChange` carries the rows that decided on each side:

```csharp
public sealed record CaseOutcomeChange(string Name, string Before, string After,
    string? RowKeyBefore, int? RowNumberBefore, string? RowKeyAfter, int? RowNumberAfter);
```

Row key and 1-based position of the row that decided on each side, taken from the `SimulationResult` of `RuleDiffer.CompareBehaviour`; null when no row decided (default row, no match, or a refused sample). Under hit policy `collect`, the first deciding row.

**A locator from a loader error path to the terms of a form**, so a panel can mark the field:

```csharp
public sealed record DraftLocation(string? RowKey, int? RowNumber, bool IsDefaultRow, string? Fact, string? OutcomeField, string? CaseName);

public static class DraftErrorLocator
{
    // path is BrainSpecError.Path (a JSON pointer into the document, e.g. /body/rows/2/conditions/1/value).
    public static DraftLocation Locate(string draftJson, string path);
}
```

Pure; never throws on a bad document or a path that does not resolve (every member null or false). `/body/rows/{i}/...` gives the row's `legacyKey` and number `i + 1`; under it `conditions/{j}/...` gives that condition's `fact` and `outcome/{field}` gives the field. `/body/defaultRow/...` sets `IsDefaultRow` and the field. `/body/goldens/{k}/...` gives the case's `name`. Anything else: nothing set. Tested with real errors: drafts that the loader refuses (an enum value not allowed, an operator not allowed for the fact, an outcome type mismatch, a missing required outcome field, a case expecting a removed row), loaded with `BrainRuleLoader.Load`, and `Locate` on each returned error path.

## 3. Rules

- All code, comments and docs in English. Sentence templates in English and Spanish.
- Purity: no IO, clock, random or network (the purity scan covers this assembly; do not add exemptions). The coupling test's reference list must not change.
- No change to `Synapbiz.Brain.Contracts`, `.Engine`, `.Spec`, `.Process`, `.Workbench.Blazor`, the workflows, `Directory.Packages.props` or the version.
- Synthetic fixtures only (reuse `tests/fixtures/workbench/`).
- Never skip or disable a test; no `NoWarn`; no analyzer switches.

## 4. Tests (`tests/Synapbiz.Brain.Workbench.Tests`)

- **Round trip, the core guard:** for every edit kind, apply it to a fixture, load the draft with `BrainRuleLoader.Load` and the test contracts, and assert on the loaded rule (table rows, conditions, outcomes, cases, versions), not on text. Every operator the catalogue offers for every value type goes through `SetCondition` and loads.
- **Simulation after an edit:** at least three cases where a `SetCondition` or `SetOutcome` changes what `RuleSimulator.Simulate` returns for a fixed fact set, asserted before and after.
- **Preservation:** an empty edit list changes only `version`; an edit on one row leaves every other row, `meta`, `uses`, `requires` and an unknown property identical (compare parsed nodes with `JsonNode.DeepEquals`).
- Every error code of 2.2 and 2.5; edit order (later edits see earlier ones); the once-only version bump; determinism (two runs, same text).
- Catalogue: both locales, fallbacks, every fact and outcome field of the fixtures, the operator table against `ConditionMatcher.Create`.
- Change view: added, removed, changed, moved, changed-and-moved, no change, every count form of the summary in both locales, cases that change, a refused sample.
- Proposal: round trip, every parse error, the draft kept as an object.
- Null guards for every public member, as the existing `NullGuardTests` do.
- Line coverage of `Synapbiz.Brain.Workbench` stays at or above its current 98.5%.

## 5. Docs

- `docs/reference/rule-workbench.md`: a section "Drafts and proposals" (edits table, the version and preservation rules, error codes, the proposal shape, a short example of the host flow: apply, load, describe changes, serialise).
- `docs/specs/rule-workbench/spec.md`: move guided editing out of "Out of scope" into a section for W-C with the two parts (W-C1 this brief; W-C2 panels, pending design); `tasks.md`: a W-C1 section, ticked as you go, leaving "Independent verification" unticked.
- `docs/architecture/overview.md` if it lists what the workbench provides. `CHANGELOG.md`: one entry under Unreleased.

## 6. Gates and report

Run from the repository root with `DOTNET_ROLL_FORWARD=Major`:

- `dotnet build Synapbiz.Brain.sln -c Release --no-incremental`: 0 errors, only the 2 baseline warnings in `OverlapAnalyzer.cs`.
- `dotnet test Synapbiz.Brain.sln -c Release --no-build`: all green. Baseline 22 + 167 + 226 + 330 + 519 + 297.
- Coverage of `Synapbiz.Brain.Workbench` (XPlat Code Coverage on its test project).
- `dotnet pack` still gives six packages.
- `bash tools/parity/check-source-drift.sh <source-checkout>` still reports no drift.

Report: commits, gate numbers, the two design choices this brief leaves to you (the `RowCondition` type, the JSON writer path), deviations, anything undone.
