# Plan — Rule workbench

| Package | Scope | Depends on | Brief |
| --- | --- | --- | --- |
| W-A | `Synapbiz.Brain.Workbench` headless library, tests, architecture guards, docs | Brain 0.1.0 | `docs/briefs/W-A-rule-workbench-library.md` |
| W-B | `Synapbiz.Brain.Workbench.Blazor` panels, component tests | W-A merged | `docs/briefs/W-B-rule-workbench-panels.md` |
| W-C | Guided editing: typed edits to a rule document that produce a draft revision, validated and diffed | W-A merged | to write |
| Consumer | "Rules" screen in the first consumer's local application | W-B published; that product's rules in `brain.rule@1` | owned by that repository |

Order: W-A, then W-B and W-C in parallel. Each package is one pull request with its own independent verification.

Release: the new packages ship in the next version of the repository; the release owner bumps the version and creates the tag.
