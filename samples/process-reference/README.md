# Reference process documents

Six synthetic `brain.process@1` documents that together run one durable case, from before a visit until the money is collected, with the rule documents (`brain.rule@1`) and contracts they use. A consumer can copy them and change the names, the thresholds and the queues. Everything in them is invented: the subject is a "case" with ids such as `c-1001`, codes such as `C-100`, and a placeholder model id `model-a`. No document names a person, an organisation or a product.

How to write a document: [brain-process-v1](../../docs/reference/brain-process-v1.md). How to run them on PostgreSQL and what to implement around them: [the consumer guide](../../docs/guides/process-engine.md).

## Layout

Each folder is one process. `process.json` is the document, `contract.json` the module contract of its rules (`<module>@1`), and every `*.rule.json` a rule it names in `uses`. `case.lifecycle` and `practice.daily_refresh` use no rule and have no contract. The tests (`tests/Synapbiz.Brain.Process.Tests/ReferenceFlowTests.cs`) load every document with zero diagnostics, run every scenario and every rule golden, and fail when a step with more than one route has a route that no scenario takes.

| Process | Stage | What it does | Results |
| --- | --- | --- | --- |
| `previsit.outreach` | previsit | Reads what the case is missing, decides with a rule, sends at most three reminders inside the contact hours, waits for the reply, re-checks; the front desk takes over when the reminders are used up or the delivery is uncertain | `previsit_closed`, `previsit_unreachable` |
| `visit.note_wait` | visit | Polls the status of the visit note at growing intervals until a rule says it is signed; a person chases it when two days pass | `note_signed`, `visit_cancelled` |
| `postvisit.coding_review` | postvisit | Lists coding opportunities with a `collect` rule, optionally asks an assistant under contract, and always ends at a physician review | `coding_approved`, `coding_rejected` |
| `collection.followup` | collection | Waits for the claim to be submitted, then polls its status until it is settled; a rule routes paid, denied and pending; it only observes | `collected`, `written_off`, `manual_close` |
| `case.lifecycle` | all four | The root: one instance per case, started by the event `case.opened`, one child process per stage | `collected`, `written_off`, `closed_no_visit`, `closed_not_billed`, `manual_close` |
| `practice.daily_refresh` | none | Started by a schedule every morning at 06:00 in the zone of the activation; reads the day's case list into a variable | `refreshed`, `refresh_failed` |

The processes never submit or change a claim, never write to the record of a visit and never decide on their own that something is billable: they observe through read ports and events, contact a person only through an `atMostOnce` contact action in a delivery window, and hand every judgement to a rule or a human task.

Opening the cases is the consumer's job. `practice.daily_refresh` leaves the day's list in its variable `cases` and ends; the consumer reads the finished instance (or its read port does it as the consumer's own side effect) and publishes one `case.opened` event per case. There is no "publish event" step in the format.

## Ports

A port is named by the `port` member of a `read`, `action`, `ai` or `poll` step. The input is the object the step builds from its `input` mapping; the output members are the ones the step maps into variables with `output` (other members are ignored). A port answers with an output object or a failure kind (`Transient`, `RateLimited`, `NotAvailable`, `NotEntitled`, `Permanent`, `Unknown`); every step says where each kind goes.

| Port | Step kind | Used by | Input | Output |
| --- | --- | --- | --- | --- |
| `intake.requirements` | read | `previsit.outreach` | `case` (the case reference) | `missing_count` (integer, how many items are still missing) |
| `messaging.reminder` | action, `atMostOnce`, contact, window `contact_hours` | `previsit.outreach` | `case`, `missing` | none read |
| `records.note_status` | poll | `visit.note_wait` | `case` | `status` (code: `absent`, `draft`, `awaiting_signature`, `signed`; any other text is "not ready") |
| `records.note_facts` | read | `postvisit.coding_review` | `case` | `items` (integer), `flagged` (integer) |
| `assist.suggest` | ai, model `model-a`, prompt `coding-suggest` version 1 | `postvisit.coding_review` | `case` (string), `opportunities` (array of `{ "opportunity": "C-100" }`) | `code` (string such as `C-100`), `confidence` (integer); nothing else is allowed |
| `claims.status` | poll | `collection.followup` | `case` | `status` (code: `submitted`, `in_review`, `paid`, `partial`, `denied`, `voided`) |
| `schedule.cases_for_day` | read | `practice.daily_refresh` | `day` (date, `yyyy-MM-dd`) | `cases` (array of objects, each with `case_ref`) |

A practice that has no connector for a port answers `NotAvailable`; one whose plan does not include it answers `NotEntitled`. Both are routes of the steps, not errors: they lead to a person.

## Events

| Event | Published by the consumer | Consumed by | Correlation | Payload |
| --- | --- | --- | --- | --- |
| `case.opened` | when a case is opened | the event trigger of `case.lifecycle`, which starts one instance per business key | none | `case_ref` (the business key, a non-empty string of at most 200 characters, not starting with `@`), optional `practice_zone` (an IANA zone id) |
| `case.reply_received` | when the person replies to a reminder | the wait `reply` of `previsit.outreach` | `case_ref` | not read |
| `claim.submitted` | when the claim of the case is submitted | the wait `await_submitted` of `collection.followup` | `case_ref` | not read |

An event reaches only an instance that waits for it when it is published. A reply that arrives while the instance is somewhere else is lost, and the timeout of the wait covers it (the next reminder in `previsit.outreach`, the `overdue` task in `collection.followup`).

## Triggers and inputs

| Process | Trigger | Input variables |
| --- | --- | --- |
| `case.lifecycle` | event `case.opened`, business key from `case_ref`, mapped to `case_ref` and `practice_zone` | `case_ref`, `practice_zone` (the zone of the contact hours), `assist_enabled` (boolean, from the activation input) |
| `practice.daily_refresh` | schedule `daily`, cron `0 6 * * *`, `fireOnce`, zone from the activation, `scheduledDate` mapped to `run_for` | `run_for` (date) |
| the four stages | started as children of `case.lifecycle` (they can also be started directly) | `case_ref`; `practice_zone` for `previsit.outreach`; `assist_enabled` for `postvisit.coding_review` |

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
| `operations` | `case.lifecycle`: `ops_previsit`, `ops_visit`, `ops_postvisit`, `ops_collection` (a child failed or answered with a result nobody listed) | `continue`, `close` for `ops_previsit`; `close` for the other three | `continue` goes on to the visit stage; `close` ends `manual_close` |

The `show` members of a task are the variables its person needs; they are the only values the host stores for the task, and they are protected like every other payload.

## Rules

| Rule | File | Kind and hit policy | Decides |
| --- | --- | --- | --- |
| `previsit.missing_items` | `previsit.outreach/missing_items.rule.json` | `previsit.missing_items`, unique | `none` or `some` from the count of missing items; a negative count decides nothing |
| `visit.note_ready` | `visit.note_wait/note_ready.rule.json` | `visit.note_ready`, unique | `yes` for a `signed` note, `no` for `absent`, `draft` or `awaiting_signature`; any other status decides nothing and the poll goes on |
| `postvisit.opportunities` | `postvisit.coding_review/opportunities.rule.json` | `coding.opportunities`, collect | every coding opportunity as `{ "opportunity": "C-100" }`; none found decides nothing |
| `postvisit.assist_gate` | `postvisit.coding_review/assist_gate.rule.json` | `coding.assist_gate`, unique | `yes` only when the practice enabled the assistant and the note has items |
| `postvisit.suggestion_check` | `postvisit.coding_review/suggestion_check.rule.json` | `coding.suggestion_check`, unique | whether a suggestion is shown as usable (confidence 70 to 100) or unsure (0 to 69); a confidence outside 0 to 100 is an invalid answer and decides nothing |
| `collection.claim_progress` | `collection.followup/claim_progress.rule.json` | `collection.claim_progress`, unique | `open` or `settled` from the status of the claim |
| `collection.claim_result` | `collection.followup/claim_result.rule.json` | `collection.claim_result`, unique | `paid`, `denied` or `pending` (a partial payment) for a settled claim; `voided` decides nothing |

## Scenarios

Every document carries its own scenarios (`RunScenarios()`): previsit 15, visit 11, postvisit 16, collection 18, lifecycle 12, daily refresh 5. Between them they take every route out of every step that can branch: timeouts, failures with and without retry, an `Unknown` outcome of a contact action, `NotAvailable`, `NotEntitled`, loop exhaustion, poll expiry, a window that opens later, a rule that decides nothing and a rule that fails. A scenario that waits for a delivery window lists the zone it uses in `zones`; the zone variable of the document is an input (`practice_zone`).
