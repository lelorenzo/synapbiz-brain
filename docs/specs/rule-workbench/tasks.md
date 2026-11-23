# Tasks — Rule workbench

## W-A — headless library

- [x] Project, solution entry, package metadata
- [x] Labels (`RuleLabels`, `meta.display`) and text helpers
- [x] Rule view and sentences, English and Spanish
- [x] Fact input parsing
- [x] Simulation with per-row explanation
- [x] Test run with extra cases
- [x] Revision diff and behaviour comparison
- [x] Review document `brain.rule-review@1`
- [x] Architecture tests: reference freeze, purity scan
- [x] Docs: reference page, overview, changelog, README
- [x] Independent verification of every gate

## W-B — Blazor panels

- [x] Brief
- [x] `RuleLabels.ValueFormatter` in the headless library
- [x] Project, solution entry, package metadata, demo host
- [x] `RuleWorkbench`: list, Read, Simulate, Test cases, Review, export
- [x] Fixed strings in Spanish and English, overridable, with the four markings protected
- [x] `--wb-*` token stylesheet, light and dark
- [x] bUnit tests, architecture tests (coupling, purity, identifier guard)
- [x] Screenshots of the demo in light, dark and English
- [x] Docs: reference page, overview, changelog, README, consuming guide
- [x] Independent verification of every gate

## W-B2 — panel refinements

- [x] Brief
- [x] Sentence grammar in the headless library: list joiners, plainer set and emptiness sentences
- [x] Review control in the test table
- [x] No repeated value, any-value conditions
- [x] Headline fields and labelled values
- [x] Money select, date `lang`
- [x] Folding after six rows
- [x] A fix or a question needs its note, export held
- [x] bUnit tests for each item
- [x] Screenshots of the demo, with the review line and the missing note
- [x] Docs: reference pages, changelog
- [x] Independent verification of every gate

## W-C — guided editing

### W-C1 — headless drafts and proposals

- [x] Brief, with the addendum (deciding rows in `CaseOutcomeChange`, `DraftErrorLocator`)
- [x] Typed edits and `RuleDrafter` (version rule, preservation, error codes)
- [x] Round-trip tests: every edit kind and every offered operator for every value type load with `BrainRuleLoader`
- [x] Simulation tests before and after an edit
- [x] `RuleChoiceCatalog`, with the operator table checked against `ConditionMatcher.Create`
- [x] `RuleChangeDescriber`: row changes, summary in both locales, cases that change, deciding rows
- [x] `DraftErrorLocator`, tested with real loader errors
- [x] `brain.rule-proposal@1`: serialise, parse, error codes
- [x] Null guards, purity scan and coupling tests unchanged and green
- [x] Docs: reference page, spec, overview, changelog
- [x] Independent verification of every gate

### W-C2 — panels

- [x] Brief and design
- [x] Session draft state and the apply and undo engine, tested
- [x] Draft bar, view switch, start and discard
- [x] Row form and row menu, with refusals and warnings
- [x] Test cases that change result, and decisions about them
- [x] Changes tab and proposal export
- [x] Demo (`LoadDraft`, `OutcomeTexts`), screenshots in light and dark
- [x] Docs: reference page, spec, changelog
- [ ] Independent verification of every gate
