# `brain.rule@1` and the module contract

How to write a decision rule for the Brain engine and the contract file that declares the facts and outcomes it may use. The library is `Synapbiz.Brain.Spec`; design in [the PR 2 spec](../specs/b0-brain-spec-rules/spec.md). Every example here is synthetic.

A rule is validated against **contracts**: the rule says `requires: ["<module>@<n>"]`, and the contract of that module declares the decision kinds, facts and outcome fields the rule may name.

## The contract file

```json
{ "module": "...", "schemaVersion": 1, "facts": [ ... ], "kinds": [ ... ] }
```

| Field | Type | Meaning |
| --- | --- | --- |
| `module` | string | Module name. A rule kind `<module>.<name>` is owned by this module. |
| `schemaVersion` | integer | With `module` it forms the address `<module>@<schemaVersion>` that a rule lists in `requires`. |
| `facts[]` | array | One entry per fact of a kind. File order is kept. |
| `facts[].kind` | string | The kind the fact belongs to; it must be declared in `kinds`. |
| `facts[].name` | string | Fact name, as used in `conditions[].fact` and in golden `facts`. |
| `facts[].type` | string | `Code`, `Enum`, `Boolean`, `Integer`, `Decimal`, `Money`, `Date` or `Reference`. |
| `facts[].nullable` | boolean | Whether the fact may be absent or null. |
| `facts[].allowedValues` | array of strings | The permitted members of an `Enum` fact; list them. An `Enum` value is readable only against a list: **an empty array rejects every value** (`brain.fact.enum_not_allowed`), and a fact with no list makes every value of it a type mismatch (`decisions.validation.cell_type_mismatch` in a condition, `decisions.facts.type_mismatch` in a golden) unless the value itself carries an `allowedValues` array. For the other types the files write `[]`, meaning nothing is declared. |
| `facts[].labelSource` | object or null | For a `Reference`: `{ownerModule, entityType}` naming where its label comes from. |
| `facts[].catalogFactId` | string or null | Optional link to a catalogue fact; it is copied to the fact descriptor and not interpreted. |
| `kinds[].id` | string | Kind identifier, `<module>.<snake_case_name>`. |
| `kinds[].schemaVersion` | integer | Version of the kind's fact and outcome shape. |
| `kinds[].evaluationMode` | `Materialized` or `OnDemand` | How the host evaluates the kind. |
| `kinds[].permissions` | object | `read`, `author`, `approve`, `readTrace`: the permission names the host checks. |
| `kinds[].outcomes[]` | array | The fields of an outcome: `name`, `type`, `nullable`, `labelSource`, and for an `Enum` field `allowedValues`. |
| `kinds[].outcomes[].allowedValues` | array of strings | The permitted members of an `Enum` outcome field. A row or golden outcome with another value is reported as `brain.fact.enum_not_allowed` at the outcome field's path. With no `allowedValues` any string is accepted; as for a fact, an empty array rejects every value. A field with `nullable: false` must be present in every outcome (`brain.outcome.required_field`). |

Other top-level members are ignored. A contract that cannot be read throws `DecisionEngineException` with the code `brain.contract.malformed` and the JSON path of the problem in `Args["path"]` (a missing `module`, an unknown fact type, a fact pointing to an undeclared kind).

## The rule document

Envelope, all members required:

| Member | Meaning |
| --- | --- |
| `$spec` | `brain.rule@1` |
| `id` | Stable identifier: letters, digits, `_`, `.`, `-`, not starting with `.` or `-`. |
| `version` | Integer from 1 to 9007199254740991. Reported on every trace as `VersionNumber`. |
| `uses` | Array of `{"$ref": "<kind>/<id>"}` references to other sections. Use `[]` when none. |
| `requires` | Contract addresses, `<module>@<n>`. The owning module of the kind must be listed. |
| `meta` | Free object (title, reasons, labels). Not interpreted; `BrainRule.Meta` returns a copy. |
| `body` | The rule, below. |

`$v0` and `$scope` members are tolerated and ignored.

### Body

| Member | Meaning |
| --- | --- |
| `kind` | The decision kind, `<module>.<name>`, declared by a required contract. |
| `hitPolicy` | `unique` (at most one row may match), `first` (the first matching row wins) or `collect` (every matching row contributes). |
| `rows[]` | Rows in decision order. |
| `defaultRow` | Optional row (or `null`) used when no row matches. |
| `goldens[]` | The rule's own test cases. |

### Row

| Member | Meaning |
| --- | --- |
| `id` | Row identifier, same pattern as the rule id. |
| `version` | Integer from 1. |
| `legacyKey` | Non-blank, unique within the rule. It becomes the engine row key and is what a trace reports as the matched row. `BrainRule.RowIdsByKey` maps it back to `id`. |
| `conditions[]` | All must hold for the row to match. |
| `outcome` | Object of the kind's outcome fields. Only declared fields, with their declared types. |

### Condition

`fact` names a fact of the kind; `op` is one of the following, and the other members carry its operands. `lowerInclusive` and `upperInclusive` apply to `range` and **default to `true`** when left out.

| `op` | Operands | Allowed fact types |
| --- | --- | --- |
| `any` | none | all |
| `equals`, `notEquals` | `value` | all |
| `in`, `notIn` | `values` (array) | all but `Boolean` |
| `isNull`, `isNotNull` | none | all |
| `range` | `value` (lower), `upper`, `lowerInclusive`, `upperInclusive` | `Integer`, `Decimal`, `Money`, `Date` |
| `startsWith` | `value` | `Code` |

In a `range`, `value` is the lower bound and `upper` the upper bound. **Either may be `null` (or `upper` left out) for an open bound**: `value: null` means no lower bound, `upper: null` no upper bound. A `Money` condition may not mix currencies.

### Typed values

Every operand, golden fact and enum member is `{"type": ..., "value": ...}`. Type tags are capitalised.

| `type` | `value` |
| --- | --- |
| `Code`, `Enum` | string (an `Enum` member must be one of the fact's `allowedValues`) |
| `Boolean` | `true` or `false` |
| `Integer` | a string of digits, optionally negative: `"49"`, not `49` |
| `Decimal` | a string with an optional fraction: `"12.50"` |
| `Money` | `{"amount": "12.50", "currency": "USD"}`, three capital letters |
| `Date` | `"2026-11-18"` |
| `Reference` | `{"guid": "<guid>", "labelSource": {"ownerModule": ..., "entityType": ...}}`; the label source may be left out when the contract supplies it |

Integers and decimals are strings so that no precision is lost in transit. They are read with the invariant culture.

### Goldens

| Member | Meaning |
| --- | --- |
| `name` | Case name. |
| `facts` | Object, fact name to typed value. Only facts the kind declares, with their declared types. |
| `expect` | `{"noDecision": true}`, or a decided expectation `{"outcome": ...}` with an optional `"row": "<row id>"`. A decided expectation needs the `outcome`: **a `row` alone is rejected** (`brain.rule.golden_outcome_missing`). `outcome` is an object, or an array of objects when the hit policy is `collect` (a mismatch is `decisions.definition.malformed`). |

`expect.row` names a row **id**; the projection maps it to the row's `legacyKey`.

## Loading

```csharp
var contract = BrainContract.Parse(File.ReadAllText("synthetic.contract.json"));
var result = BrainRuleLoader.Load(File.ReadAllText("readiness.rule.json"), [contract]);

if (!result.IsValid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"{error.Code} at {error.Path}: {error.MessageEn} ({error.FixHint})");
    }
    return;
}

BrainRule rule = result.Rule!;
var report = rule.ValidateTable();          // overlap, coverage and cell checks of the engine
var goldens = rule.RunGoldens();            // the document's own cases
var decision = rule.Evaluate(facts, DateOnly.FromDateTime(today));
```

`Load` runs three stages in order and stops at the first that reports an error, reporting all errors of that stage, ordered by path and then code.

1. Structure: JSON, envelope, registered format, schema and the golden check. Codes: `spec.json.invalid`, `spec.structure.envelope`, `spec.structure.unknown_format`, `spec.structure.schema` (the path is the offending value), `brain.rule.golden_outcome_missing`.
2. Semantics, node checks and contract checks reported together. Node checks: `spec.semantics.node`, `spec.semantics.duplicate_id`, `spec.semantics.reference` (a `uses` entry that is not in `knownSectionRefs`), `spec.semantics.cycle`, `spec.semantics.translation`. Contract checks: `brain.contract.unavailable`, `brain.contract.module_required`, `decisions.kind.not_registered`, `decisions.validation.duplicate_row_key`, `decisions.validation.unknown_fact`, `decisions.condition.operator_not_allowed`, `decisions.validation.money_currency_mismatch`, `decisions.condition.range_type_mismatch`, `decisions.validation.cell_type_mismatch`, `brain.fact.label_source_mismatch`, `brain.fact.enum_not_allowed`, `decisions.facts.missing`, `decisions.facts.type_mismatch`, `brain.golden.unknown_row`, `brain.outcome.unknown_field`, `brain.outcome.type_mismatch`, `brain.outcome.required_field`.
3. Projection to the engine's table and golden cases. A document that passed the stages above should never fail here; if it does the code is `decisions.definition.malformed`.

Each error carries English and Spanish messages and a fix hint. The codes and texts are those of the source repository's validator.

What the loader does **not** decide is listed in `DeferredToEngine` (`overlap`, `coverage`, `goldens`, `outcome-policy`). Call `ValidateTable()` for the first two and the outcome policy, and `RunGoldens()` for the third; the result of a golden run is only meaningful when the table is valid.

## A complete example

A synthetic readiness rule with a `collect` hit policy, an `Enum` fact and an `Integer` fact with ranges. Both files below are the test fixtures `tests/fixtures/brain-spec/readiness.contract.json` and `readiness.rule.json`; the tests load them and a documentation test fails when this page and the fixtures differ.

The contract, `synthetic@1`:

```json
{
  "module": "synthetic",
  "schemaVersion": 1,
  "facts": [
    {
      "kind": "synthetic.readiness",
      "name": "stage",
      "type": "Enum",
      "nullable": false,
      "allowedValues": ["Intake", "Review", "Complete"],
      "labelSource": null,
      "catalogFactId": null
    },
    {
      "kind": "synthetic.readiness",
      "name": "score",
      "type": "Integer",
      "nullable": false,
      "allowedValues": [],
      "labelSource": null,
      "catalogFactId": null
    }
  ],
  "kinds": [
    {
      "id": "synthetic.readiness",
      "schemaVersion": 1,
      "evaluationMode": "OnDemand",
      "permissions": {
        "read": "synthetic.decisions.read",
        "author": "synthetic.decisions.author",
        "approve": "synthetic.decisions.approve",
        "readTrace": "synthetic.decisions.read_trace"
      },
      "outcomes": [
        { "name": "note", "type": "Code", "nullable": false, "labelSource": null }
      ]
    }
  ]
}
```

The rule, `readiness-notes`:

```json
{
  "$spec": "brain.rule@1",
  "id": "readiness-notes",
  "version": 3,
  "uses": [],
  "requires": ["synthetic@1"],
  "meta": {
    "title": "Readiness notes",
    "reasons": { "low-score": "The score is below 50." }
  },
  "body": {
    "kind": "synthetic.readiness",
    "hitPolicy": "collect",
    "rows": [
      {
        "id": "low-score",
        "version": 1,
        "legacyKey": "low-score",
        "conditions": [
          {
            "fact": "score",
            "op": "range",
            "value": { "type": "Integer", "value": "0" },
            "upper": { "type": "Integer", "value": "49" },
            "lowerInclusive": true,
            "upperInclusive": true
          }
        ],
        "outcome": { "note": "low-score" }
      },
      {
        "id": "mid-score",
        "version": 1,
        "legacyKey": "mid-score",
        "conditions": [
          {
            "fact": "score",
            "op": "range",
            "value": { "type": "Integer", "value": "50" },
            "upper": { "type": "Integer", "value": "79" },
            "lowerInclusive": true,
            "upperInclusive": true
          }
        ],
        "outcome": { "note": "mid-score" }
      },
      {
        "id": "late-stage",
        "version": 1,
        "legacyKey": "late-stage",
        "conditions": [
          {
            "fact": "stage",
            "op": "in",
            "values": [
              { "type": "Enum", "value": "Review" },
              { "type": "Enum", "value": "Complete" }
            ]
          }
        ],
        "outcome": { "note": "late-stage" }
      }
    ],
    "goldens": [
      {
        "name": "A low score at intake",
        "facts": {
          "stage": { "type": "Enum", "value": "Intake" },
          "score": { "type": "Integer", "value": "20" }
        },
        "expect": { "row": "low-score", "outcome": [{ "note": "low-score" }] }
      },
      {
        "name": "A mid score in review matches two rows",
        "facts": {
          "stage": { "type": "Enum", "value": "Review" },
          "score": { "type": "Integer", "value": "65" }
        },
        "expect": { "row": "mid-score", "outcome": [{ "note": "mid-score" }, { "note": "late-stage" }] }
      },
      {
        "name": "A high score at intake matches nothing",
        "facts": {
          "stage": { "type": "Enum", "value": "Intake" },
          "score": { "type": "Integer", "value": "95" }
        },
        "expect": { "noDecision": true }
      }
    ]
  }
}
```

Reading it: a score of 20 at the intake stage matches `low-score` only; a score of 65 in review matches `mid-score` (range 50 to 79) and `late-stage` (stage in Review or Complete), so the collect outcome has two entries in row order; a score of 95 at intake matches nothing, which is the `noDecision` golden.
