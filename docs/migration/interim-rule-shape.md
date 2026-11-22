# Migrating the interim rule shape to `brain.rule@1`

The interim shape is a rule file with an envelope (`id`, `revision`, `meta.facts`, `meta.outcome`, `meta.reasons`) and a body with lowercase type tags, JSON-number integers, a row `key` and goldens written as `expected: { kind, rowKeys }`. `brain.rule@1` keeps the body's meaning and moves what describes the module's facts and outcomes into a **contract file**. The target is specified in [brain-rule-v1](../reference/brain-rule-v1.md); every name below is synthetic.

This page lists the mapping as the shape is described in the PR 2 brief. A file that departs from that description needs a check against the target schema: `BrainRuleLoader.Load` names every problem with a code and a path.

## Envelope

| Interim | `brain.rule@1` | Note |
| --- | --- | --- |
| (none) | `"$spec": "brain.rule@1"` | New. |
| `id` | `id` | Same pattern: letters, digits, `_`, `.`, `-`, not starting with `.` or `-`. |
| `revision` | `version` | Integer from 1. A string or a float is rejected (`spec.structure.schema` at `/version`). |
| (none) | `uses: []` | New, required. Lists `{"$ref": ...}` references to other sections; empty when the rule uses none. |
| (none) | `requires: ["<module>@1"]` | New, required. The contract address of the module that owns the kind; list every contract the rule needs. |
| `meta.facts` | the module contract `facts[]` | Moves out of the rule. Each fact becomes `{kind, name, type, nullable, allowedValues, labelSource, catalogFactId}` with a capitalised `type`. |
| `meta.outcome` | the module contract `kinds[].outcomes[]` | Moves out of the rule: `{name, type, nullable, labelSource}` per outcome field. |
| `meta.reasons` | `meta.reasons` | Stays under `meta`, unchanged; `meta` is not interpreted. |
| other `meta` members | `meta` | Stay under `meta`. |

## Body

| Interim | `brain.rule@1` | Note |
| --- | --- | --- |
| `body.kind` | `body.kind` | Named `<module>.<snake_case>`, for example `synthetic.readiness`; the module part selects the owning contract. |
| hit policy | `body.hitPolicy` | `unique`, `first` or `collect`. |
| row `key` | row `legacyKey` and `id` | `legacyKey` keeps the key exactly (it stays the engine row key and the key traces report); `id` is a slug of it (letters, digits, `_`, `.`, `-`). |
| (none) | row `version: 1` | New, required. |
| condition `fact`, `op` | the same | Operators: `any`, `equals`, `notEquals`, `in`, `notIn`, `isNull`, `isNotNull`, `range`, `startsWith`. |
| lowercase type tags (`integer`, `code`) | capitalised tags (`Integer`, `Code`) | One of `Code`, `Enum`, `Boolean`, `Integer`, `Decimal`, `Money`, `Date`, `Reference`. A lowercase tag is rejected. |
| `Integer` and `Decimal` as JSON numbers | strings: `"49"`, `"12.50"` | Matching `^-?(0\|[1-9][0-9]*)$` and the same with an optional fraction. |
| `Money` amount as a number | `{"amount": "12.50", "currency": "USD"}` | Amount a string, currency three capitals. |
| typed outcome cells | plain outcome fields | `outcome: {"note": "low-score"}`, the value's JSON type given by the contract's outcome field type, no `{type, value}` wrapper. |
| `defaultRow` | `defaultRow` | A row with the same shape, or `null`. |

## Goldens

| Interim | `brain.rule@1` | Note |
| --- | --- | --- |
| `expected` whose `kind` says no row decides (and has no `rowKeys`) | `expect: { "noDecision": true }` | |
| `expected` whose `kind` says a row decides, with `rowKeys: [k]` | `expect: { "row": "<row id>", "outcome": { ... } }` | `row` is the row **id**, not the legacy key; the loader maps it back to the key. State the expected outcome object. |
| `expected` for a `collect` rule | `expect: { "row": "<first row id>", "outcome": [ { ... }, { ... } ] }` | The outcome is an array in row order; `row` names a matching row. |
| golden facts | `facts: { "<name>": {"type": ..., "value": ...} }` | Typed like any operand. |
| golden `name` | `name` | Required. |

A golden that has neither `row`, `outcome` nor `noDecision` is rejected with `brain.rule.golden_outcome_missing`.

## Worked shape, synthetic

Interim body row (fragment):

```json
{ "key": "low-score", "conditions": [ { "fact": "score", "op": "range", "value": { "type": "integer", "value": 0 }, "upper": { "type": "integer", "value": 49 } } ], "outcome": { "note": { "type": "code", "value": "low-score" } } }
```

The same row in `brain.rule@1` (the complete rule, with its contract, is in [brain-rule-v1](../reference/brain-rule-v1.md#a-complete-example)):

```json
{ "id": "low-score", "version": 1, "legacyKey": "low-score", "conditions": [ { "fact": "score", "op": "range", "value": { "type": "Integer", "value": "0" }, "upper": { "type": "Integer", "value": "49" }, "lowerInclusive": true, "upperInclusive": true } ], "outcome": { "note": "low-score" } }
```

## Interim constructs with no target

`brain.rule@1` has no place for these; each is rejected or has to be resolved before migrating.

- **Rule-local fact and outcome declarations.** Facts and outcome fields exist only in a module contract. A rule that declares facts of its own in `meta.facts` has to have them moved into the owning module's contract first; a condition on a fact the contract does not declare is `decisions.validation.unknown_fact`.
- **A kind without a module prefix.** `body.kind` must be `<module>.<name>` so that the owning contract is known.
- **A goldens expectation that names rows only.** `expected.rowKeys` without an outcome has no target: write the outcome object (or array for `collect`).
- **Revision values that are not integers.** `version` is an integer of at least 1.
- **Blank or duplicated row keys.** `legacyKey` must be non-blank and unique (`decisions.validation.duplicate_row_key`).
- **Operators outside the list above, and operators on a type that does not support them** (`range` on a `Code`, `startsWith` on a number, `in` on a `Boolean`): `decisions.condition.operator_not_allowed`.
- **Typed outcome cells that carry a type different from the contract's outcome field**: `brain.outcome.type_mismatch`.

## Checking a migrated file

Load it with the contract and run its goldens:

```csharp
var rule = BrainRuleLoader.Load(json, [contract]).Rule;      // null with errors when the document is not valid
var goldens = rule!.RunGoldens();                            // IsOk when every golden gives its expected result
```
