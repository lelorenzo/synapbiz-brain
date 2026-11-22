# Migrating an interim rule file to `brain.rule@1`

An interim rule file is a decision table written before the module contract existed: facts and outcome fields are declared inside the rule's own `meta`, values are typed with lowercase tags, and goldens name rows instead of outcomes. `brain.rule@1` keeps the table and moves the declarations into a **contract file** owned by the module. The target format is in [brain-rule-v1](../reference/brain-rule-v1.md). This page is written against a real interim file; every example here is synthetic, and the worked example at the end is a test fixture.

## Recognising an interim file

An interim file **already carries `"$spec": "brain.rule@1"`**, so the format tag does not tell the two apart. Recognise it by:

- `revision` where `brain.rule@1` has `version`,
- `meta.facts` (and `meta.outcome`) as maps keyed by name,
- no `uses`, no `requires`, and no `body.kind`.

Loaded as it stands, an interim file is refused with `spec.structure.schema` errors (the envelope has `revision` and lacks `version`, `uses` and `requires`; the body lacks `kind`; row `key`, lowercase tags and the `expected` goldens do not match the schema). Fix the shape first, then the contract-dependent checks run.

## What you decide at migration

Four things have no interim source and are assigned by whoever migrates the file:

| Item | Where it goes | Value |
| --- | --- | --- |
| The module | the prefix of `body.kind`, the `module` of the contract, `requires` | A short lowercase name for the owning module, for example `intake`. |
| The kind | `body.kind` and the contract's `kinds[].id` | `<module>.<snake_case>`, for example `intake.requirements`. There is no `body.kind` in an interim file. |
| `kinds[].schemaVersion` | the contract | `1` for a new kind. |
| `kinds[].evaluationMode` | the contract | `OnDemand`. |
| `kinds[].permissions` | the contract | For a consumer with no permission model of its own, `<module>.rules.read`, `<module>.rules.author`, `<module>.rules.approve` and `<module>.rules.trace` for `read`, `author`, `approve` and `readTrace`. |

## Envelope

| Interim | `brain.rule@1` | Note |
| --- | --- | --- |
| `$spec: "brain.rule@1"` | `$spec: "brain.rule@1"` | Unchanged. |
| `id` | `id` | Unchanged (letters, digits, `_`, `.`, `-`, not starting with `.` or `-`). |
| `revision` | `version` | Same integer, at least 1. |
| (none) | `uses: []` | New and required; empty when the rule uses no other section. |
| (none) | `requires: ["<module>@1"]` | New and required: the contract address of the owning module, plus any other contract the rule needs. |
| `meta.facts` | the contract's `facts[]` | See below. Removed from `meta`. |
| `meta.outcome` | the contract's `kinds[].outcomes[]` | See below. Removed from `meta`. |
| `meta.reasons`, `meta.status` and other `meta` members | `meta` | Copied, with the review of free text below. |

## `meta.facts` and `meta.outcome` become the contract

Both are maps keyed by name in the interim file (`"idImage": { "type": "enum", "values": [...] }`); the contract holds arrays.

| Interim fact entry | Contract `facts[]` entry |
| --- | --- |
| the key | `name` |
| `type` in lowercase (`boolean`, `enum`, `code`, `integer`, ...) | `type` capitalised (`Boolean`, `Enum`, `Code`, `Integer`, ...) |
| `values` (an `enum` member list) | `allowedValues` |
| (none) | `kind`: the kind chosen above; `id`: `<kind>/<name>`; `labelSource: null`, `catalogFactId: null` unless the fact is a `Reference` |
| (none) | `nullable`: **`true`**, see below |

**Facts are nullable.** In the interim format a fact that is absent when the table is evaluated matches no row, which is what a nullable fact does. Migrate every fact as `nullable: true` unless the interim reader rejects the fact's absence (then `false`). A fact declared `nullable: false` that a caller omits is refused by the engine (`FactSet` rejects the evaluation) instead of quietly matching no row, so `true` is the default that preserves behaviour.

Interim outcome entries become `kinds[].outcomes[]` entries the same way: key to `name`, tag capitalised to `type`, `nullable: false` when every row supplies the field. For an `enum` outcome field the member list `values` becomes **`allowedValues` on the outcome entry**; the loader honours it: an outcome value that is not a member is reported as `brain.fact.enum_not_allowed` at the row's or golden's outcome path, exactly as it is for a fact. An outcome field with no member list accepts any string.

## Body

| Interim | `brain.rule@1` | Note |
| --- | --- | --- |
| (none) | `body.kind` | Assigned above. |
| `body.hitPolicy` | `body.hitPolicy` | `unique`, `first` or `collect`. |
| row `key` | row `legacyKey` and row `id` | `legacyKey` is the key exactly (it stays the engine row key and the key traces report). `id` is the key when it already fits the id pattern, otherwise a slug of it (disallowed characters replaced by `-`). Keys must be unique. |
| (none) | row `version: 1` | New and required. |
| condition `fact`, `op`, `value`, `values` | the same members | Operators: `any`, `equals`, `notEquals`, `in`, `notIn`, `isNull`, `isNotNull`, `range`, `startsWith`. |
| lowercase type tags (`boolean`, `enum`, `code`, `integer`) | capitalised (`Boolean`, `Enum`, `Code`, `Integer`) | A lowercase tag is rejected. |
| `Integer` and `Decimal` values as JSON numbers | strings: `"49"`, `"12.50"` | `Money` becomes `{"amount": "12.50", "currency": "USD"}`. |
| typed outcome cells `{"type": "code", "value": "x"}` | plain outcome fields `"x"` | The JSON type follows the contract's outcome field type; there is no `{type, value}` wrapper. |
| `defaultRow` | `defaultRow` | A row of the same shape, or `null`. |

## Goldens

An interim golden is `{ name, facts, expected: { kind, rowKeys } }`. It names the rows that must decide but carries **no outcome**. `brain.rule@1` pins the outcome, so the migrator derives it from the table.

| Interim | `brain.rule@1` |
| --- | --- |
| `name`, `facts` | `name`; `facts` with each value tagged and capitalised like any operand |
| `expected.kind` other than `decided` (for example `no_decision`) | `expect: { "noDecision": true }` |
| `expected.kind: decided` with `rowKeys: [k1, k2, ...]` | `expect.outcome`: the outcomes of the rows `k1`, `k2`, ... **in that order**, as plain outcome objects; for a `collect` rule an array of them (for `unique` and `first`, the single object of `k1`). `expect.row`: the `id` of the row for `k1`, the first key. |

For a `collect` rule only the first key is named in `expect.row`; **the other keys are pinned only through the outcome array**. Two rows with identical outcomes cannot be told apart by it. `BrainRule.RunGoldens()` then proves that the table still yields exactly that array for those facts.

A golden with a row and no outcome is rejected (`brain.rule.golden_outcome_missing`).

## Free text in `meta`

`meta` is copied as it is, with one review. Free text that records where the rule came from (for example a `source` member naming people, clinics, meetings or documents) must be read by the migrator and either removed or replaced by a description that names no one: the rule file travels with the consumer that adopts it. `status`, `reasons` and other structured members carry over unchanged.

## Lossless and lossy

Preserved exactly:

- every row, its order and its row key (`legacyKey`),
- every condition: fact, operator and operands, with the same values,
- every outcome value, now written plainly,
- the hit policy and `defaultRow`,
- every golden: its name, its facts and its expectation (the outcome is derived from the rows, so the expectation is as strong as the table makes it),
- `meta.reasons`, `meta.status` and the rule `id`.

Assigned at migration, with no interim source:

- the module, the kind, and `kinds[].schemaVersion`, `evaluationMode` and `permissions`,
- `requires`, `uses`, the row `id` and `version`,
- the `nullable` flag of each fact (default `true`) and each outcome field.

Lost or changed:

- `revision` is renamed `version`, with the same number;
- the interim `meta.facts` and `meta.outcome` maps no longer sit in the rule; they live in the contract, shared by every rule of the module;
- the interim `expected.rowKeys` of a `collect` golden is replaced by an outcome array, so keys are pinned only through outcomes;
- free-text provenance in `meta` is dropped or rewritten at review.

The real interim file uses the operators `equals` and `in` and the types `boolean`, `enum` and `code`; all three have targets. A construct that has none is rejected by the loader with a code and a path, so it shows up at the first load.

## Worked example

A synthetic interim file, `tests/fixtures/brain-spec/migration/interim.rule.json`:

```json
{
  "$spec": "brain.rule@1",
  "id": "intake.checklist",
  "revision": 1,
  "meta": {
    "status": "draft",
    "source": "Synthetic planning notes, first session",
    "facts": {
      "isTrial": { "type": "boolean" },
      "idImage": { "type": "enum", "values": ["present", "absent"] },
      "formStatus": { "type": "enum", "values": ["present", "absent", "partial"] }
    },
    "outcome": {
      "requirement": { "type": "code" },
      "status": { "type": "enum", "values": ["missing", "review", "excluded"] },
      "reason": { "type": "code" }
    },
    "reasons": {
      "trial_excluded": "A trial record is excluded from the checklist.",
      "id_required": "An identity image is required.",
      "form_incomplete": "The form is absent or partial."
    }
  },
  "body": {
    "hitPolicy": "collect",
    "rows": [
      {
        "key": "trial",
        "conditions": [
          { "fact": "isTrial", "op": "equals", "value": { "type": "boolean", "value": true } }
        ],
        "outcome": {
          "requirement": { "type": "code", "value": "trial" },
          "status": { "type": "enum", "value": "excluded" },
          "reason": { "type": "code", "value": "trial_excluded" }
        }
      },
      {
        "key": "id_missing",
        "conditions": [
          { "fact": "isTrial", "op": "equals", "value": { "type": "boolean", "value": false } },
          { "fact": "idImage", "op": "equals", "value": { "type": "enum", "value": "absent" } }
        ],
        "outcome": {
          "requirement": { "type": "code", "value": "identity" },
          "status": { "type": "enum", "value": "missing" },
          "reason": { "type": "code", "value": "id_required" }
        }
      },
      {
        "key": "form_open",
        "conditions": [
          { "fact": "isTrial", "op": "equals", "value": { "type": "boolean", "value": false } },
          {
            "fact": "formStatus",
            "op": "in",
            "values": [
              { "type": "enum", "value": "absent" },
              { "type": "enum", "value": "partial" }
            ]
          }
        ],
        "outcome": {
          "requirement": { "type": "code", "value": "form" },
          "status": { "type": "enum", "value": "review" },
          "reason": { "type": "code", "value": "form_incomplete" }
        }
      }
    ],
    "goldens": [
      {
        "name": "everything present: nothing open",
        "facts": {
          "isTrial": { "type": "boolean", "value": false },
          "idImage": { "type": "enum", "value": "present" },
          "formStatus": { "type": "enum", "value": "present" }
        },
        "expected": { "kind": "no_decision" }
      },
      {
        "name": "trial record: only the exclusion",
        "facts": {
          "isTrial": { "type": "boolean", "value": true },
          "idImage": { "type": "enum", "value": "absent" },
          "formStatus": { "type": "enum", "value": "absent" }
        },
        "expected": { "kind": "decided", "rowKeys": ["trial"] }
      },
      {
        "name": "identity and form both open",
        "facts": {
          "isTrial": { "type": "boolean", "value": false },
          "idImage": { "type": "enum", "value": "absent" },
          "formStatus": { "type": "enum", "value": "partial" }
        },
        "expected": { "kind": "decided", "rowKeys": ["id_missing", "form_open"] }
      }
    ]
  }
}
```

The contract for the module `intake`, `tests/fixtures/brain-spec/migration/migrated.contract.json`. The facts are nullable, the outcome field `status` carries its member list as `allowedValues`, and the kind takes the assigned `schemaVersion`, `evaluationMode` and permissions:

```json
{
  "module": "intake",
  "schemaVersion": 1,
  "facts": [
    { "id": "intake.requirements/isTrial", "kind": "intake.requirements", "name": "isTrial", "type": "Boolean", "nullable": true, "allowedValues": [], "labelSource": null, "catalogFactId": null },
    { "id": "intake.requirements/idImage", "kind": "intake.requirements", "name": "idImage", "type": "Enum", "nullable": true, "allowedValues": ["present", "absent"], "labelSource": null, "catalogFactId": null },
    { "id": "intake.requirements/formStatus", "kind": "intake.requirements", "name": "formStatus", "type": "Enum", "nullable": true, "allowedValues": ["present", "absent", "partial"], "labelSource": null, "catalogFactId": null }
  ],
  "kinds": [
    {
      "id": "intake.requirements",
      "schemaVersion": 1,
      "evaluationMode": "OnDemand",
      "permissions": {
        "read": "intake.rules.read",
        "author": "intake.rules.author",
        "approve": "intake.rules.approve",
        "readTrace": "intake.rules.trace"
      },
      "outcomes": [
        { "name": "requirement", "type": "Code", "nullable": false, "labelSource": null },
        { "name": "status", "type": "Enum", "nullable": false, "labelSource": null, "allowedValues": ["missing", "review", "excluded"] },
        { "name": "reason", "type": "Code", "nullable": false, "labelSource": null }
      ]
    }
  ]
}
```

The migrated rule, `tests/fixtures/brain-spec/migration/migrated.rule.json`. The `source` text was dropped at review; the third golden's outcome array holds the outcomes of `id_missing` then `form_open`, the rows named by the interim golden, in that order:

```json
{
  "$spec": "brain.rule@1",
  "id": "intake.checklist",
  "version": 1,
  "uses": [],
  "requires": ["intake@1"],
  "meta": {
    "status": "draft",
    "reasons": {
      "trial_excluded": "A trial record is excluded from the checklist.",
      "id_required": "An identity image is required.",
      "form_incomplete": "The form is absent or partial."
    }
  },
  "body": {
    "kind": "intake.requirements",
    "hitPolicy": "collect",
    "rows": [
      {
        "id": "trial",
        "version": 1,
        "legacyKey": "trial",
        "conditions": [
          { "fact": "isTrial", "op": "equals", "value": { "type": "Boolean", "value": true } }
        ],
        "outcome": { "requirement": "trial", "status": "excluded", "reason": "trial_excluded" }
      },
      {
        "id": "id_missing",
        "version": 1,
        "legacyKey": "id_missing",
        "conditions": [
          { "fact": "isTrial", "op": "equals", "value": { "type": "Boolean", "value": false } },
          { "fact": "idImage", "op": "equals", "value": { "type": "Enum", "value": "absent" } }
        ],
        "outcome": { "requirement": "identity", "status": "missing", "reason": "id_required" }
      },
      {
        "id": "form_open",
        "version": 1,
        "legacyKey": "form_open",
        "conditions": [
          { "fact": "isTrial", "op": "equals", "value": { "type": "Boolean", "value": false } },
          {
            "fact": "formStatus",
            "op": "in",
            "values": [
              { "type": "Enum", "value": "absent" },
              { "type": "Enum", "value": "partial" }
            ]
          }
        ],
        "outcome": { "requirement": "form", "status": "review", "reason": "form_incomplete" }
      }
    ],
    "goldens": [
      {
        "name": "everything present: nothing open",
        "facts": {
          "isTrial": { "type": "Boolean", "value": false },
          "idImage": { "type": "Enum", "value": "present" },
          "formStatus": { "type": "Enum", "value": "present" }
        },
        "expect": { "noDecision": true }
      },
      {
        "name": "trial record: only the exclusion",
        "facts": {
          "isTrial": { "type": "Boolean", "value": true },
          "idImage": { "type": "Enum", "value": "absent" },
          "formStatus": { "type": "Enum", "value": "absent" }
        },
        "expect": {
          "row": "trial",
          "outcome": [ { "requirement": "trial", "status": "excluded", "reason": "trial_excluded" } ]
        }
      },
      {
        "name": "identity and form both open",
        "facts": {
          "isTrial": { "type": "Boolean", "value": false },
          "idImage": { "type": "Enum", "value": "absent" },
          "formStatus": { "type": "Enum", "value": "partial" }
        },
        "expect": {
          "row": "id_missing",
          "outcome": [
            { "requirement": "identity", "status": "missing", "reason": "id_required" },
            { "requirement": "form", "status": "review", "reason": "form_incomplete" }
          ]
        }
      }
    ]
  }
}
```

The tests load the migrated rule with its contract, run its goldens, check that each migrated golden carries the outcomes of the rows the interim golden named in order, and fail if this page and the fixtures differ.

## Checking a migrated file

```csharp
var contract = BrainContract.Parse(File.ReadAllText("intake.contract.json"));
var result = BrainRuleLoader.Load(File.ReadAllText("intake.checklist.rule.json"), [contract]);
var run = result.Rule!.RunGoldens();      // IsOk when every golden gives its expected result and every row is covered
```
