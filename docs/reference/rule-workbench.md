# Rule workbench

`Synapbiz.Brain.Workbench` is a headless library that lets a person who owns a rule read it in plain language, try it with example facts, run and add test cases, compare two revisions and send back a review, all on the real Brain engine. Design: [the rule workbench spec](../specs/rule-workbench/spec.md). Rule and contract formats: [brain-rule-v1](brain-rule-v1.md). Every example here is synthetic.

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

`RuleDescriber.Describe` returns a `RuleView`: the facts (with their allowed values), the outcome fields, the rows in decision order and the default row. Each row carries its conditions as text, its outcome values and one sentence. The sentence templates are those of the source repository's sentence generator, in English and Spanish.

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
