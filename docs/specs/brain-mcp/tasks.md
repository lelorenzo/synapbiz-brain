# Tasks: Brain MCP

Spec: [spec.md](spec.md). Version 0.7.0. M1 is the headless library; M2 is the protocol adapter and the program.

## M1: `Synapbiz.Brain.Authoring` (headless)

- [x] Spec in the repository as `docs/specs/brain-mcp/spec.md`
- [x] Library and test project, in `Synapbiz.Brain.sln` (so CI and the release workflow build, test and pack them with the others)
- [x] Workspace: store abstraction, file system and in-memory stores, ids that cannot name a path, no write outside `drafts` and `proposals`
- [x] Catalog `brain.catalog@1`: model, serializer, parser, check of a draft
- [x] RFC 6902 JSON Patch written for the package
- [x] `FlowDiffer`, `FlowChangeDescriber` and the behaviour replay, in English and Spanish
- [x] Readiness checks, one code and one sentence in both languages each
- [x] Fix hints for every loader and validator code, with a test that finds the codes by reflection
- [x] `brain.process-proposal@1`: model, serializer, parser, check; the rule proposal file with `needsReview` and the reasons
- [x] The seventeen tools as plain functions with a JSON Schema each; coded refusals; `say` in `en` and `es`
- [x] Simulation sessions with expiry by an injected clock
- [x] `brain_guide` with a drift test against the format reference and the reference documents
- [x] Tests: every tool (schema, success, every refusal), the differ with golden texts in both languages, proposal round trip, one failing fixture per readiness check, the scripted conversation (list, get, save a broken draft, read hints, fix, simulate, add scenario, propose), no write outside `drafts` and `proposals` (traversal, absolute path, link) and approved documents byte-identical after a randomized sequence of calls, text parity and the wording rule
- [x] Architecture tests: pinned references of the library; no network, machine clock, randomness or GUID; files only in the file workspace
- [x] Reference page `docs/reference/brain-authoring.md`, overview and consumer guide updated, changelog entry

## M2: `Synapbiz.Brain.Mcp`, `brain-mcp`

- [ ] `Synapbiz.Brain.Mcp`: adapt the tools to the MCP protocol with the official C# SDK; resources for the guide sections and a prompt "model a process with me" that carries the working method
- [ ] `brain-mcp`: a .NET tool serving the same tools over stdio for a folder of documents
- [ ] End-to-end test with a scripted MCP client: list, get, save a broken draft, read hints, fix, simulate, add scenario, propose
- [ ] Architecture test: only the MCP adapter references the SDK
- [ ] Documentation and release of version 0.7.0
