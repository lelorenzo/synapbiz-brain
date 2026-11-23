# Rule workbench

`Synapbiz.Brain.Workbench` is a headless library that lets a person who owns a rule read it in plain language, try it with example facts, run and add test cases, compare two revisions, send back a review, and change a rule through typed edits that produce a draft revision to send to its maintainers, all on the real Brain engine. Design: [the rule workbench spec](../specs/rule-workbench/spec.md). Rule and contract formats: [brain-rule-v1](brain-rule-v1.md). Every example here is synthetic.

The library reads no file, opens no connection and keeps no state. It adds presentation and comparison only: every decision it reports comes from `BrainRule.Evaluate`, `TableCompiler`, `ConditionMatcher`, `GoldenRunner` and `TableValidator`. It changes no rule and activates nothing. A host (a Blazor panel, a web API behind a React view) supplies the loaded rule, the typed text and the clock.

Namespace `Synapbiz.Brain.Workbench`; references `Synapbiz.Brain.Contracts`, `.Engine` and `.Spec`, nothing else.

## Labels: `meta.display`

A rule may carry display names under `meta.display.<en|es>`. A host may build a `RuleLabels` itself instead. A missing label falls back to a humanised name (`hasInsuranceOnFile` becomes `has insurance on file`). Labels never change a decision, and a member of the wrong JSON kind is ignored.

```json
"meta": {
  "display": {
    "en": {
      "title": "Intake routing",
      "facts": { "channel": "Contact channel" },
      "outcomes": { "route": "Route" },
      "values": {
        "channel": { "Phone": "the phone line" },
        "route": { "Escalate": "escalation" }
      },
      "rows": { "urgent-phone": "Urgent calls" }
    },
    "es": { "title": "Enrutamiento de solicitudes", "facts": { "channel": "Canal de contacto" } }
  }
}
```

| Member | Type | Meaning |
| --- | --- | --- |
| `title` | string | The rule's title (`RuleLabels.Title`). |
| `facts` | object of strings | Fact name to label. |
| `outcomes` | object of strings | Outcome field name to label. |
| `values` | object of objects of strings | Fact or outcome field name, then raw value, to label. |
| `rows` | object of strings | Row id to title. |

`RuleLabels.ValueFormatter` is not read from `meta`: only a host sets it.

```csharp
var labels = RuleLabels.FromMeta(rule.Meta, WorkbenchLocale.En);
```

## Describe a rule

`RuleDescriber.Describe` returns a `RuleView`: the facts (with their allowed values), the outcome fields, the rows in decision order and the default row. Each row carries its conditions as text, its outcome values and one sentence. The sentence templates started as those of the source repository's sentence generator and are no longer a verbatim copy: the set and emptiness operators read more plainly and every list has a final joiner, in English and Spanish.

```csharp
var view = RuleDescriber.Describe(rule, WorkbenchLocale.En, labels);

foreach (var row in view.Rows)
{
    Console.WriteLine(row.Sentence);
}
// If score is between 0 and 49, then note is low-score.
// With WorkbenchLocale.Es: Si score está entre 0 y 49, entonces note es low-score.
```

A condition that says nothing (`any`, or a range with no bound) is listed in `Conditions` but left out of the sentence; a row with nothing left reads `In every case, ...`. The default row reads `Otherwise, ...`. `WorkbenchText.FormatValue` writes one typed value the same way (`yes`/`no`, a decimal with a comma in Spanish, dates as `MM/dd/yyyy` or `dd/MM/yyyy`).

How the sentences read:

- **Lists.** One item is written as it is; two items are joined by the final joiner; three or more are separated by commas with the final joiner before the last, and never a comma before it. The conditions of a sentence and the parts of an outcome use `and` (`y`): `If A, B and C, then route is Escalate, queue is Urgent queue and notify is yes.` The values of an "is one of" condition use `or` (`o`) and the values of an "is none of" condition use `or` in English and `ni` in Spanish.
- **Operators.** Equals reads `topic is a`; one of reads `topic is a or b`; none of reads `topic is not a or b` (Spanish `topic no es a ni b`); empty reads `rating has no value` (`rating no tiene valor`) and not empty reads `rating has a value` (`rating tiene valor`). The empty and not-empty wording avoids agreeing a word with the gender of the label.

A host can write values its own way with `RuleLabels.ValueFormatter`, a `Func<DecisionValue, string?>`. `FormatValue` asks, in this order: a null value is empty; a code or enum member that has a value label is written by its label; otherwise the formatter, when it is set and returns a text; otherwise the default text above. Condition texts and sentences go through `FormatValue`, so one formatter changes every one of them and a screen can show dates the way the rest of the host does. Outcome values of a row are written from the JSON text of the row and do not go through the formatter.

```csharp
var labels = RuleLabels.FromMeta(rule.Meta, WorkbenchLocale.En) with
{
    ValueFormatter = value => value is DateValue date ? date.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : null,
};
// "... received on is at most 30 Jun 2026 ..."; labelled enum values keep their labels, other values keep the default text.
```

## Parse typed facts

`FactInput.Parse` turns what a person typed into a typed value for one fact; `ParseAll` does it for every fact of a kind that is not composed from another kind.

```csharp
var texts = new Dictionary<string, string?>
{
    ["channel"] = "Phone",
    ["urgent"] = "maybe",
    ["ageDays"] = "12",
};

FactSetInputResult input = FactInput.ParseAll(rule.Kind, texts);
// input.Facts  : channel, ageDays (valid values)
// input.Errors : urgent => "workbench.fact.not_a_boolean", plus "workbench.fact.required" for each required fact with no text
```

Accepted text: `Code` any text; `Enum` one of the allowed values; `Boolean` `true` or `false`; `Integer` a whole number; `Decimal` a number with a point; `Date` `yyyy-MM-dd`; `Money` `12.50 USD` (amount, one space, three capital letters); `Reference` a GUID. Blank text is valid with no value for a nullable fact.

## Simulate

`RuleSimulator.Simulate` evaluates the rule with the engine and explains the result row by row: whether the row matched, whether the engine selected it, and which of its conditions passed (using the engine's own `ConditionMatcher`).

```csharp
SimulationResult result = RuleSimulator.Simulate(rule, input.Facts, DateOnly.FromDateTime(today));

if (!result.Succeeded)
{
    Console.WriteLine($"{result.Failure!.Code}: {result.Failure.Message}");
    return;
}

foreach (var row in result.Rows)
{
    Console.WriteLine($"{row.Key}: matched={row.Matched} selected={row.Selected}");
}
```

`Outcome` and `Trace` are those of `rule.Evaluate`. A fact set or evaluation the engine refuses is a `SimulationFailure` with the engine's code, never a guess. With composed facts `Rows` is empty (and an `IComposedFactResolver` must be supplied).

## Run tests

`RuleTester.Run` runs the rule document's golden cases followed by any extra cases, each on its own through `GoldenRunner`, and lists the rows no passing case covers.

```csharp
var extra = new GoldenCase(
    "A low score at review",
    new Dictionary<string, DecisionValue> { ["stage"] = DecisionValue.Enum("Review"), ["score"] = DecisionValue.Integer(10) },
    new ExpectedDecided("""[{"note":"low-score"},{"note":"late-stage"}]""", "low-score"));

TestRunView run = RuleTester.Run(rule, [extra]);
Console.WriteLine($"{run.PassedCount} passed, {run.FailedCount} failed, uncovered: {string.Join(", ", run.UncoveredRowKeys)}");
```

A case the engine refuses fails with the engine's code in `TestCaseView.Failure` and does not stop the run. A rule with no case at all reports every row uncovered.

## Compare two revisions

```csharp
RuleDiff diff = RuleDiffer.Diff(before, after);          // rows, default row, cases, hit policy, kind, row order
var samples = RuleDiffer.SamplesFromGoldens(before, after);
var changes = RuleDiffer.CompareBehaviour(before, after, samples, DateOnly.FromDateTime(today));

foreach (var change in changes.Where(change => change.Differs))
{
    Console.WriteLine($"{change.Name}: the two revisions decide differently");
}
```

Rows are paired by row key and cases by name (the first case of a name counts). A version difference alone is not a change. `CompareBehaviour` simulates every sample on both revisions.

## Review document

`brain.rule-review@1` is a message from the reviewer to the rule's maintainers: which rule (id, version, content hash of the exact text), a state and a note per rule, row or test case, and proposed test cases. It changes no rule. The library has no clock, so the caller supplies `CreatedAt`.

```csharp
var review = new RuleReview(
    rule.Id,
    rule.Version,
    RuleReviewJson.ContentHash(ruleText),
    "Reviewer One",
    createdAt,
    [new ReviewItem("row:low-score", ReviewState.NeedsFix, "The limit should be 50.")],
    []);

string json = RuleReviewJson.Serialize(review);
RuleReviewParseResult read = RuleReviewJson.Parse(json);
```

```json
{
  "$spec": "brain.rule-review@1",
  "rule": { "id": "readiness-notes", "version": 3, "contentHash": "sha256:..." },
  "reviewer": "Reviewer One",
  "createdAt": "2026-11-22T21:00:00+00:00",
  "items": [
    { "target": "row:low-score", "state": "needsFix", "note": "The limit should be 50." }
  ],
  "proposedCases": [
    { "name": "A score of 50", "facts": { "score": { "type": "Integer", "value": "50" } }, "expect": { "noDecision": true } },
    { "name": "A score of 10", "facts": { }, "expect": { "row": "low-score", "outcome": { "note": "low-score" } } }
  ]
}
```

| Member | Meaning |
| --- | --- |
| `rule.contentHash` | `sha256:` and the lower-case hex SHA-256 of the UTF-8 bytes of the rule text exactly as reviewed. |
| `createdAt` | Date and time to whole seconds, with an offset (`+00:00`; `Z` is also read). |
| `items[].target` | Free text for the library. Convention: `rule`, `row:<row id>` or `golden:<name>`. |
| `items[].state` | `unreviewed`, `confirmed`, `needsFix` or `question`. |
| `proposedCases[].facts` | Typed values as in [brain-rule-v1](brain-rule-v1.md#typed-values); a `Reference` is written `{"guid": "..."}`. Written sorted by name. |
| `proposedCases[].expect` | `{"noDecision": true}`, or `outcome` (an object, or an array of objects for `collect`) with an optional `row` (a row id). |

Notes and case names are typed by a person and may hold personal data the library cannot detect; the panel that edits a review shows a standing notice that they must not identify a real person, and the export is a file the reviewer hands over themselves.

`Parse` collects every defect, ordered by path, and returns no review when there is any. Unknown members are ignored. `ExpectedOutcomeJson` is read back in compact form.

## Drafts and proposals

Guided editing is a set of typed edits that a pure function applies to the text of a `brain.rule@1` document. The result is the text of a draft revision. Nothing here loads, validates, evaluates, activates or keeps state: the host loads the draft with `BrainRuleLoader.Load` and its own contracts, exactly as it loads any rule, so a draft is judged by the same code as production. A person never edits JSON and never activates anything.

### Edits

A row is named by its `legacyKey`, the key `BrainRule.RowIdsByKey` and `RowView.Key` use. Conditions are the engine's own `Condition` (fact name, operator and operands); there is no parallel type.

| Edit | Effect | Error when |
| --- | --- | --- |
| `SetCondition(RowKey, Condition)` | Replaces the row's condition on that fact in place, or adds it at the end. The row never has two conditions on one fact. | `rowNotFound` |
| `ClearCondition(RowKey, Fact)` | Removes the row's condition on the fact. | `rowNotFound`, `conditionNotFound` |
| `SetOutcome(RowKey, Field, Value?)` | Sets one outcome field; a null value removes it (only an optional field loads without it). | `rowNotFound` |
| `AddRow(RowKey, AfterRowKey?, Conditions, Outcome)` | Adds a row with `id` and `legacyKey` equal to the key and version 1; after the named row, or first when null. | `rowKeyNotValid`, `rowKeyTaken`, `rowNotFound` (the anchor) |
| `RemoveRow(RowKey)` | Removes the row and its label under every `meta.display.<locale>.rows`. Test cases that expect the row are left alone, so the loader refuses them: that is the signal. | `rowNotFound` |
| `MoveRow(RowKey, AfterRowKey?)` | Moves the row; null puts it first. The row keeps its version. | `rowNotFound`, `moveOntoItself` |
| `SetDefaultOutcome(Field, Value?)` | Like `SetOutcome` on the default row. | `noDefaultRow` |
| `SetCaseExpectation(CaseName, RowKey?, Outcome)` | The case now expects a decision with that outcome (a one-element list under `collect`) and, when `RowKey` is not null, from that row or the default row. To expect no decision, remove the case and add it again. | `caseNotFound`, `rowNotFound` |
| `AddCase(ProposedCase)` / `RemoveCase(CaseName)` | Appends a case (`ExpectedRowId` is a row id, as in a review) or removes one by name. | `caseNameTaken`, `caseNotValid` / `caseNotFound` |
| `SetRowLabel(RowKey, Locale, Text?)` | Sets, or with null clears, the row's title under `meta.display.<locale>.rows`, keyed by the row's id. | `rowNotFound`, `labelNotValid` |

All codes are `workbench.draft.<name>`. `RuleDrafter.Apply(ruleJson, edits)` also reports `baseNotReadable` when the base is not JSON, repeats a member name, is not an object with `$spec` `brain.rule@1`, a whole-number `version` and a `body.rows` array, or lacks a member an edit needs (a row's `version`, the display labels as objects). An edit that cannot be applied stops the run with one error, whose path is the edit's position (`/edits/2`), and no draft. An empty edit list is valid. A null edit, or an edit with a null member, is an `ArgumentNullException`.

### Versions and preservation

- The draft's document `version` is the base version plus 1.
- A row touched by `SetCondition`, `ClearCondition`, `SetOutcome` or, for the default row, `SetDefaultOutcome` gets its row `version` plus 1, once however many edits touch it. A row added in the same run stays at 1. A moved row keeps its version.
- Every property an edit does not name keeps its value and its place: `$spec`, `id`, `uses`, `requires`, the rest of `meta`, unknown properties, untouched rows and cases. Numbers keep their written form. The text is indented with two spaces, `\n` line ends, non-ASCII letters not escaped, no final newline. The same input gives the same text.
- Typed values are written in the JSON shape the loader reads (tagged values; `values` for sets; `upper`, `lowerInclusive` and `upperInclusive` for ranges; `expect.row` and `expect.outcome` for cases). The loader supplies `allowedValues` of an enum and the `labelSource` of a reference from the contract. `Synapbiz.Brain.Spec` keeps its writer internal, so the workbench has its own, pinned by round-trip tests that load every offered operator for every value type.

### Choices for a form

`RuleChoiceCatalog.For(rule, labels, locale)` lists, from the rule's kind descriptor, the facts and outcome fields in the descriptor's order with their labels and types, the operators a condition on each fact may use (the `Operator` member names the engine's `OperatorMatrix` accepts for the type, the check `ConditionMatcher.Create` makes) and the members of an enum fact with their labels. A kind descriptor does not carry the allowed members of an enum outcome field, so `OutcomeChoices.Values` is empty; the loader still refuses a member the contract does not allow. `HasDefaultRow` and `HitPolicy` (`unique`, `first` or `collect`) say whether `SetDefaultOutcome` applies and how rows combine.

### What a draft changes

`RuleChangeDescriber.Describe(before, after, labelsBefore, labelsAfter, locale, extraSamples)` is built on `RuleDiffer`, `RuleDescriber` and `WorkbenchText`.

- `Rows` lists only the rows that differ, in the draft's order, with a removed row where it was: `Added`, `Removed`, `Changed` (conditions or outcome; it may also have moved) and `Moved` (same content, another place among the rows both revisions have; only the rows outside the longest run that keeps its order are reported). Numbers are 1-based; the sentences are those of `RuleDescriber`.
- `Summary`: `This draft changes 2 rows, adds 1 and removes none.` (Spanish `Este borrador cambia 2 filas, agrega 1 y no quita ninguna.`), `Moves 1 row.` / `Mueve 1 fila.` when rows only moved, `This draft does not change the rule.` / `Este borrador no cambia la regla.` when nothing changed. The default row counts as a row; a changed hit policy adds `The hit policy changes.`
- `CasesThatChange`: every sample (the test cases of both revisions, the newer facts winning by name, plus `extraSamples`) whose outcome reads differently, each side written as a row's outcome reads in a sentence (several matched rows of a `collect` rule are separated by `; `), `no result` / `sin resultado` when that side decided nothing or refused the sample, and the key and 1-based position of the row that decided on each side (the first one under `collect`; null for the default row or when no row decided). Samples run with the as-of date `DateOnly.MinValue`, as golden cases do.
- `DraftErrorLocator.Locate(draftJson, path)` turns the path of a loader error (`BrainSpecError.Path`) into the terms of a form: the row key and number, whether it is the default row, the fact of the condition, the outcome field, the case name. A path that does not resolve gives a location with nothing set; it never throws.

### Proposal document

`brain.rule-proposal@1` carries a draft from the person who made it to the rule's maintainers, bound to the base text by content hash, like a review. It changes no rule. The caller supplies the hash (`RuleReviewJson.ContentHash(baseText)`) and `CreatedAt`.

```json
{
  "$spec": "brain.rule-proposal@1",
  "rule": { "id": "intake-routing", "baseVersion": 1, "baseContentHash": "sha256:..." },
  "author": "Author One",
  "createdAt": "2026-11-23T09:30:15-05:00",
  "note": "Billing goes to the desk.",
  "draft": { "$spec": "brain.rule@1", "id": "intake-routing", "version": 2 }
}
```

`draft` is the draft document as a JSON object, not a string. `RuleProposalJson.Serialize` refuses a draft that is not a JSON object (`ArgumentException`) and writes `createdAt` to whole seconds. `Parse` never throws on content: `workbench.proposal.notJson` (not a JSON object, or a repeated member name), `specNotSupported`, `fieldMissing` and `fieldNotValid` (the path is the JSON Pointer of the member). Every defect is collected, ordered by path; unknown members are ignored; the draft is written again with the drafter's formatting.

### The host flow

```csharp
// 1. The expert's form builds edits; the catalogue says what the form may offer.
var choices = RuleChoiceCatalog.For(rule, labels, WorkbenchLocale.Es);
RuleEdit[] edits =
[
    new SetCondition("billing-topic", Condition.Equals("channel", DecisionValue.Enum("Web"))),
    new SetOutcome("billing-topic", "queue", DecisionValue.Code("billing-desk")),
];

// 2. Draft the text, then load it with the host's contracts: the same loader as production.
RuleDraftResult draft = RuleDrafter.Apply(ruleText, edits);
if (!draft.IsValid) { /* draft.Errors[0].Code, e.g. workbench.draft.rowNotFound */ return; }

var load = BrainRuleLoader.Load(draft.DraftJson!, contracts);
if (!load.IsValid)
{
    foreach (var error in load.Errors)
    {
        DraftLocation place = DraftErrorLocator.Locate(draft.DraftJson!, error.Path); // mark the field
    }

    return;
}

// 3. Show what changes, in words.
var view = RuleChangeDescriber.Describe(
    rule, load.Rule!, labels, RuleLabels.FromMeta(load.Rule!.Meta, WorkbenchLocale.Es), WorkbenchLocale.Es);
Console.WriteLine(view.Summary);

// 4. Send it to the maintainers.
var proposal = new RuleProposal(
    rule.Id, rule.Version, RuleReviewJson.ContentHash(ruleText), "Author One", now, "Billing goes to the desk.", draft.DraftJson!);
string document = RuleProposalJson.Serialize(proposal);
```

The loader also leaves the engine's own checks (overlap, coverage, goldens) to the engine: run `RuleTester.Run` and `rule.ValidateTable()` on the loaded draft before sending it.

## Codes

Engine codes reach the workbench unchanged in `SimulationFailure.Code` and `TestCaseView.Failure.Code`:

| Code | Meaning |
| --- | --- |
| `decisions.facts.missing` | A required fact is missing, or a fact the kind does not declare was given. |
| `decisions.facts.type_mismatch` | A value is not of its fact's type. |
| `decisions.facts.too_large` | The canonical fact set exceeds the kind's byte cap. |
| `decisions.evaluation.multiple_matches` | More than one row matched under the `unique` policy. |
| `decisions.composition.resolver_required` | The kind has composed facts and no resolver was given. |
| `decisions.composition.cycle`, `decisions.composition.too_deep` | The kind's composition graph is refused by the compiler. |

Workbench codes:

| Code | Where | Meaning |
| --- | --- | --- |
| `workbench.fact.required` | `FactInput` | The fact is required and the text is blank. |
| `workbench.fact.value_not_allowed` | `FactInput` | The text is not one of the enum's allowed values. |
| `workbench.fact.not_a_boolean`, `.not_an_integer`, `.not_a_decimal`, `.not_a_date`, `.not_money`, `.not_a_reference` | `FactInput` | The text does not fit the fact's type. |
| `workbench.review.json_invalid` | `RuleReviewJson.Parse` | The text is not JSON, or repeats a member name (path is empty). |
| `workbench.review.unknown_format` | `RuleReviewJson.Parse` | `$spec` names another format (path `/$spec`). |
| `workbench.review.malformed` | `RuleReviewJson.Parse` | A member is missing, of the wrong kind or unreadable; the path is the JSON Pointer of the offending value. |
| `workbench.draft.baseNotReadable`, `.rowNotFound`, `.rowKeyTaken`, `.rowKeyNotValid`, `.conditionNotFound`, `.noDefaultRow`, `.caseNotFound`, `.caseNameTaken`, `.caseNotValid`, `.moveOntoItself`, `.labelNotValid` | `RuleDrafter.Apply` | See [Drafts and proposals](#drafts-and-proposals); the path is `/edits/<position>`, or a path into the base for `baseNotReadable`. |
| `workbench.proposal.notJson`, `.specNotSupported`, `.fieldMissing`, `.fieldNotValid` | `RuleProposalJson.Parse` | The text is not a JSON object; `$spec` names another format; a member is missing or of the wrong kind (path is the member). |
