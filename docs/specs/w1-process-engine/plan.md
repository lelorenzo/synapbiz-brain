# Plan — W1, durable process engine

Design and owner decisions: [design.md](design.md). Normative spec of the first PR: [spec.md](spec.md).

| PR | Content | Status |
| --- | --- | --- |
| W1-A | `Synapbiz.Brain.Process`: `brain.process@1` schema, loader and static validation, pure interpreter (read, action, ai, decide, humanTask, wait, timer, loop, subprocess, end), state JSON, scenario runner, tests, architecture guards, docs | in progress |
| W1-B | `Synapbiz.Brain.Host.Postgres`: instance store, history, timers, waits, inbox with event de-duplication, outbox, worker with leases, retries, `Unknown` outcome for at-most-once effects whose lease expired, integration tests on PostgreSQL | in progress |
| W1-C | `triggers` (event, schedule with cron and IANA time zone, misfire policy), delivery windows on contact actions (required by the validator), `poll` | not started |
| W1-D | Four synthetic reference flows (pre-visit outreach, visit note wait, post-visit coding review, collection), consumer guide, version 0.2.0 | not started |

Order of the gates in every PR: source drift guard, build, tests with coverage, pack. Code is written by a delegated implementer from the brief in `docs/briefs/`, then an independent verifier re-runs every gate and changes nothing; one repair attempt; then the pull request.

Compatibility: nothing under `src/Synapbiz.Brain.Contracts`, `src/Synapbiz.Brain.Engine` or `src/Synapbiz.Brain.Spec` changes in W1, so the parity proofs with the source repository are unaffected. The format name `brain.flow@1` stays reserved for the source repository's approval overlay.
