# Tasks — W1-B, `Synapbiz.Brain.Host.Postgres`

- [x] Project `src/Synapbiz.Brain.Host.Postgres`, solution entries, package versions in `Directory.Packages.props`
- [x] Migration `0001_initial.sql` and the migrator (advisory lock, version table, idempotent, concurrent runs)
- [x] Public API: engine, ports, records, options, pump, registration, plaintext protector
- [x] Internal `IProcessStore` and the PostgreSQL store (the only user of `Npgsql` besides the migrator)
- [x] One transaction shape: instance lock first, work item re-validated, definition resolved, advance, persist, consume, commit
- [x] Applying requests: effects, timers, waits, tasks, child starts, terminal cleanup, parent and child notifications
- [x] Engine commands: start, publish event, complete task, cancel, resume, queries
- [x] Pump: expired leases, inbox in order, due timers, effect claim, port call outside any transaction, result application
- [x] Hosted worker
- [x] Payload protection and log hygiene
- [x] Tests: migrator, start idempotency, outreach scenarios, events, effects, tasks, child processes, commands
- [x] Tests: concurrency A and B, tenant isolation, payload protection, definition unavailable, options and arguments, worker, store lock discipline, logging hygiene
- [x] Architecture tests: `HostAssemblyCouplingTests` (reference set, project file, restore graph, `Npgsql` confinement, purity scan unchanged)
- [x] Docs: `docs/reference/process-host-postgres.md`, architecture overview, changelog, README, consuming guide, plan
- [ ] Independent verification of every gate
