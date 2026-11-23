# Reference process documents

Six synthetic `brain.process@1` documents that together run one durable case, from before a visit until the money is collected, with the rule documents (`brain.rule@1`) and contracts they use. A consumer can copy them and change the names, the thresholds and the queues. Everything in them is invented: the subject is a "case" with ids such as `c-1001`, codes such as `C-100`, and a placeholder model id `model-a`. No document names a person, an organisation or a product.

How to write a document: [brain-process-v1](../../docs/reference/brain-process-v1.md). How to run them on PostgreSQL and what to implement around them: [the consumer guide](../../docs/guides/process-engine.md).

## Layout

Each folder is one process. `process.json` is the document, `contract.json` the module contract of its rules (`<module>@1`), and every `*.rule.json` a rule it names in `uses`. `practice.daily_refresh` uses no rule and has no contract. The tests load every document with zero diagnostics, run every scenario and every rule golden, and fail when a step with more than one route has a route that no scenario takes (`ReferenceFlowTests`; the one route that no input can reach, `route_claim` `onError`, is named in the test with its reason). The durations, the delivery window and the zone of the documents are pinned by tests that call `Start` and `Advance` directly, with the values written out (`ReferenceTimingTests`): the scenarios alone would pass whatever the durations are, because an `advance` that is long enough fires every timer.

| Process | Stage | What it does | Results |
| --- | --- | --- | --- |
| `previsit.outreach` | previsit | Reads what the case is missing, decides with a rule, sends at most three reminders inside the contact hours, waits for the reply, re-checks; the front desk takes over when the reminders are used up or the delivery is uncertain | `previsit_closed`, `previsit_unreachable` |
| `visit.note_wait` | visit | Polls the status of the visit note at growing intervals until a rule says it is signed; a person chases it when two days pass | `note_signed`, `visit_cancelled` |
| `postvisit.coding_review` | postvisit | Lists coding opportunities with a `collect` rule, optionally asks an assistant under contract, and always ends at a physician review | `coding_approved`, `coding_rejected` |
| `collection.followup` | collection | Waits for the claim to be submitted, then polls its status until it is settled; a rule routes paid, denied and pending; it only observes | `collected`, `written_off`, `manual_close` |
| `case.lifecycle` | all four | The root: one instance per case, started by the event `case.opened`; first checks with a rule that the case came with its zone and hands it to operations when it did not, then one child process per stage | `collected`, `written_off`, `closed_no_visit`, `closed_not_billed`, `manual_close` |
| `practice.daily_refresh` | none | Started by a schedule every morning at 06:00 in the zone of the activation; reads the day's case list into a variable; when the read fails it raises a task for operations and ends as `refresh_failed` only once a person has handled it | `refreshed`, `refresh_failed` |

The processes never submit or change a claim, never write to the record of a visit and never decide on their own that something is billable: they observe through read ports and events, contact a person only through an `atMostOnce` contact action in a delivery window, and hand every judgement to a rule or a human task.

Opening the cases is the consumer's job. `practice.daily_refresh` leaves the day's list in its variable `cases` and ends; the consumer reads the finished instance (or its read port does it as the consumer's own side effect) and publishes one `case.opened` event per case. There is no "publish event" step in the format.

## Labels

Every document carries `meta.display`, the words a person reads in a cockpit, in English (`en`) and Spanish (`es`): a title and a label for each stage, step, result, queue, outcome, event and delivery window the document has, read with `ProcessDisplay` (see [the reference](../../docs/reference/brain-process-v1.md#display-labels)). The wording is synthetic product text. `ReferenceLabelTests` fails when a stage or a step of a document has no label in either language, when a label is for something the document does not have, or when `ProcessDisplay.Validate` has a warning. The labels are `meta`: they change no behaviour of a process, only its content hash, and a consumer that copies a folder changes them with the names.

## Ports

A port is named by the `port` member of a `read`, `action`, `ai` or `poll` step. The input is the object the step builds from its `input` mapping; the output members are the ones the step maps into variables with `output` (other members are ignored). A port answers with an output object or a failure kind (`Transient`, `RateLimited`, `NotAvailable`, `NotEntitled`, `Permanent`, `Unknown`); every step says where each kind goes.

| Port | Step kind | Used by | Input | Output |
| --- | --- | --- | --- | --- |
| `intake.requirements` | read | `previsit.outreach` | `case` (the case reference) | `missing_count` (integer, how many items are still missing) |
| `messaging.reminder` | action, `atMostOnce`, contact, window `contact_hours` | `previsit.outreach` | `case`, `missing` | none read |
| `records.note_status` | poll | `visit.note_wait` | `case` | `status` (code: `absent`, `draft`, `awaiting_signature`, `signed`; any other text is "not ready") |
| `records.note_facts` | read | `postvisit.coding_review` | `case` | `items` (integer), `flagged` (integer) |
| `assist.suggest` | ai, model `model-a`, prompt `coding-suggest` version 1 | `postvisit.coding_review` | `case` (string), `opportunities` (array of `{ "opportunity": "C-100" }`) | `code` (string such as `C-100`), `confidence` (integer, optional: an answer without it is shown to the physician without a verdict); nothing else is allowed, an extra member makes the answer invalid |
| `claims.status` | poll | `collection.followup` | `case` | `status` (code: `submitted`, `in_review`, `paid`, `partial`, `denied`, `voided`) |
| `schedule.cases_for_day` | read | `practice.daily_refresh` | `day` (date, `yyyy-MM-dd`) | `cases` (array of objects, each with `case_ref`) |

A practice that has no connector for a port answers `NotAvailable`; one whose plan does not include it answers `NotEntitled`. Both are routes of the steps, not errors: they lead to a person.

## Events

| Event | Published by the consumer | Consumed by | Correlation | Payload |
| --- | --- | --- | --- | --- |
| `case.opened` | when a case is opened | the event trigger of `case.lifecycle`, which starts one instance per business key | none | `case_ref` (the business key, a non-empty string of at most 200 characters, not starting with `@`), `practice_zone` (an IANA zone id; mandatory in practice, see below) |
| `case.reply_received` | when the person replies to a reminder | the wait `reply` of `previsit.outreach` | `case_ref` | not read |
| `claim.submitted` | when the claim of the case is submitted | the wait `await_submitted` of `collection.followup` | `case_ref` | not read |

An event reaches only an instance that waits for it when it is published. A reply that arrives while the instance is somewhere else is lost, and the timeout of the wait covers it (the next reminder in `previsit.outreach`, the `overdue` task in `collection.followup`).

## Triggers and inputs

| Process | Trigger | Input variables |
| --- | --- | --- |
| `case.lifecycle` | event `case.opened`, business key from `case_ref`, mapped to `case_ref` and `practice_zone` | `case_ref`, `practice_zone` (the zone of the contact hours; see below), `assist_enabled` (boolean, from the activation input) |
| `practice.daily_refresh` | schedule `daily`, cron `0 6 * * *`, `fireOnce`, zone from the activation, `scheduledDate` mapped to `run_for` | `run_for` (date) |
| the four stages | started as children of `case.lifecycle` (they can also be started directly) | `case_ref`; `practice_zone` for `previsit.outreach`; `assist_enabled` for `postvisit.coding_review` |

### The zone is mandatory

`practice_zone` is the zone of the contact hours, and the delivery window of `previsit.outreach` reads it. A case needs it from the day it is opened: give it in the activation input of `case.lifecycle` (every case of the practice) or in the payload of `case.opened` (one case). What happens without it:

- In `case.lifecycle`, the first step, `zone_check`, asks the rule `lifecycle.zone_ready`. A zone that is missing (the rule fails on the missing fact) or empty (the rule decides nothing) goes to the task `ops_zone` of the queue `operations`, before any child starts: `continue` skips the pre-visit stage and goes on to the visit, `close` ends the case as `manual_close`. A zone that is there but is not an IANA zone the host knows passes this check; the first reminder then fails the window with `process.window.zone_unknown`, which `send` routes to the front desk task `escalate_final`.
- In `previsit.outreach` started directly (it can be) without the zone, the step `send` does not route and does not fail: the instance is **suspended** with the problem `process.window.zone_missing` at the step `send`, and its parent, if it has one, hears nothing. The instance shows as `Suspended` with that problem code, and the log carries `process.instance.suspended`. `ResumeAsync` runs `send` again, which finds the zone still missing and suspends again: an input cannot be patched in a running instance. The way out is `CancelAsync`: a cancelled child answers its parent with `process.child.cancelled`, and the parent routes that to its operations task of the stage (`ops_previsit` in `case.lifecycle`). Starting the case again with the same business key is not possible (the key is taken), so the case is continued or closed through that task.

## Human tasks

| Queue | Created by | Outcomes | Where each goes |
| --- | --- | --- | --- |
| `front_desk` | `previsit.outreach`: `escalate` (three days to answer), `escalate_final` (three days) | `resolved`, `unreachable` | `escalate`: `resolved` back to the reminder loop, `unreachable` ends `previsit_unreachable`; `escalate_final`: `resolved` ends `previsit_closed`, `unreachable` ends `previsit_unreachable`; no answer in three days ends `previsit_unreachable` |
| `note_followup` | `visit.note_wait`: `chase_note` | `signed`, `no_visit` | `signed` ends `note_signed`, `no_visit` ends `visit_cancelled` |
| `note_confirmation` | `visit.note_wait`: `confirm_note` (no connector or not entitled) | `signed`, `no_visit` | as `note_followup` |
| `physician_review` | `postvisit.coding_review`: `physician_review`, always, no timeout | `approved`, `changed`, `rejected` | `approved` and `changed` end `coding_approved` (the data member `code` is kept as `final_code`), `rejected` ends `coding_rejected` |
| `billing_followup` | `collection.followup`: `overdue` (claim not submitted in ten days), `stalled` (the poll expired or failed), `exhausted` (the loop is used up) | `submitted`, `manual_close` for `overdue`; `keep_waiting`, `manual_close` for `stalled`; `written_off`, `manual_close` for `exhausted` | `submitted` and `keep_waiting` go back to the bounded loop; `written_off` ends `written_off`; `manual_close` ends `manual_close` |
| `denials` | `collection.followup`: `denial` | `resubmitted`, `written_off` | `resubmitted` goes back to the bounded loop (the claim is resubmitted by a person, outside the process), `written_off` ends `written_off` |
| `billing_confirmation` | `collection.followup`: `confirm` (no connector or not entitled) | `paid`, `written_off` | `paid` ends `collected`, `written_off` ends `written_off` |
| `operations` | `case.lifecycle`: `ops_zone` (the case came without its zone), `ops_previsit`, `ops_visit`, `ops_postvisit`, `ops_collection` (a child failed or answered with a result nobody listed); `practice.daily_refresh`: `refresh_attention` (the day's list could not be read) | `continue`, `close` for `ops_zone` and `ops_previsit`; `close` for the other three; `handled` for `refresh_attention` | `continue` goes on to the visit stage; `close` ends `manual_close`; `handled` ends `refresh_failed`, so a consumer that watches that result sees it only after a person acted, and the open task is the alert |

The `show` members of a task are the variables its person needs (the visit task of operations also shows `missing_at_close`, how many items the pre-visit stage left missing, when it finished); they are the only values the host stores for the task, and they are protected like every other payload.

## Rules

| Rule | File | Kind and hit policy | Decides |
| --- | --- | --- | --- |
| `previsit.missing_items` | `previsit.outreach/missing_items.rule.json` | `previsit.missing_items`, unique | `none` or `some` from the count of missing items; a negative count decides nothing |
| `visit.note_ready` | `visit.note_wait/note_ready.rule.json` | `visit.note_ready`, unique | `yes` for a `signed` note, `no` for `absent`, `draft` or `awaiting_signature`; any other status decides nothing and the poll goes on |
| `postvisit.opportunities` | `postvisit.coding_review/opportunities.rule.json` | `coding.opportunities`, collect | every coding opportunity as `{ "opportunity": "C-100" }`; none found decides nothing |
| `postvisit.assist_gate` | `postvisit.coding_review/assist_gate.rule.json` | `coding.assist_gate`, unique | `yes` only when the practice enabled the assistant and the note has items |
| `postvisit.suggestion_check` | `postvisit.coding_review/suggestion_check.rule.json` | `coding.suggestion_check`, unique | whether a suggestion is shown as usable (confidence 70 to 100) or unsure (0 to 69); a confidence outside 0 to 100 is an invalid answer and decides nothing; a suggestion without a confidence makes the rule fail, and the physician sees it without a verdict |
| `collection.claim_progress` | `collection.followup/claim_progress.rule.json` | `collection.claim_progress`, unique | `open` or `settled` from the status of the claim |
| `collection.claim_result` | `collection.followup/claim_result.rule.json` | `collection.claim_result`, unique | `paid`, `denied` or `pending` (a partial payment) for a settled claim; `voided` decides nothing |
| `lifecycle.zone_ready` | `case.lifecycle/zone_ready.rule.json` | `lifecycle.zone_ready`, unique | `yes` when the case has a zone; an empty zone decides nothing and a missing one fails the rule |

## Scenarios

Every document carries its own scenarios (`RunScenarios()`): previsit 16, visit 11, postvisit 18, collection 18, lifecycle 15, daily refresh 6 (84 in all). Between them they take every route out of every step that can branch, except the one defence named above: timeouts, failures with and without retry, an `Unknown` outcome of a contact action, `NotAvailable`, `NotEntitled`, loop exhaustion, poll expiry, a window that opens later, a rule that decides nothing and a rule that fails, a case that came without its zone, an assistant that answers with a member the schema does not allow, and a case that is suspended. A scenario that waits for a delivery window lists the zone it uses in `zones`; the zone variable of the document is an input (`practice_zone`). The scenarios do not check how long a timer, a wait, a poll round or a window is, nor which days and hours a window covers: `ReferenceTimingTests` does, with the values of the documents written out, and a change to one of them fails there.
