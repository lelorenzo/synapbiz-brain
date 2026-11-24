# Brain MCP: the protocol adapter and the `brain-mcp` program

`Synapbiz.Brain.Mcp` and `Synapbiz.Brain.Mcp.Tool` are the second half of Brain MCP (package version 0.7.0, specification [docs/specs/brain-mcp/spec.md](../specs/brain-mcp/spec.md), tasks in [tasks.md](../specs/brain-mcp/tasks.md)). They put the seventeen tools of [`Synapbiz.Brain.Authoring`](brain-authoring.md) in front of an assistant through the Model Context Protocol, using the official C# SDK (NuGet package `ModelContextProtocol` 2.4.0). Nothing else in the repository references the SDK, and neither package has a model, a vendor, a network of its own or a file access outside the store it is given.

Wording: every text a person reads says "process" (`proceso`), never "flow" (`flujo`). Tool names, codes and the prompt name keep the word `flow` only because they are names.

## Three rules that do not bend

They are the rules of the specification (section 2) and the adapter keeps them by adding nothing that could break them.

1. **The assistant proposes; it never activates.** No tool changes an approved document, activates a process, starts a real case or calls a port. Every result is a draft in the workspace or a proposal file.
2. **The engine is the judge.** A draft is ready to propose only when the loader accepts it, every embedded test passes and the readiness checks pass. The tools return facts Brain computed; nothing the assistant writes can mark a draft valid.
3. **Definitions and made-up cases only.** The tools never read the host's tables, a live case or patient data. Their descriptions say so, and every free-text input carries the warning against text that identifies a patient.

## What the server offers

**Tools.** One MCP tool for each authoring tool, in the order of the specification: `brain_guide`, `brain_catalog`, `flow_list`, `flow_get`, `rule_list`, `rule_get`, `flow_draft_save`, `flow_draft_patch`, `flow_draft_simulate`, `flow_draft_add_scenario`, `rule_draft_edit`, `rule_simulate`, `rule_test`, `flow_propose`, `rule_propose`, `proposal_list`, `proposal_get`. The name, the English description and the JSON Schema are those of the authoring tool (the schema is the input schema of the MCP tool). The ten that only read (`brain_guide`, `brain_catalog`, `flow_list`, `flow_get`, `rule_list`, `rule_get`, `rule_simulate`, `rule_test`, `proposal_list`, `proposal_get`) say so in their annotations; none claims to reach outside the workspace, and none is destructive.

**What a call returns.** The whole answer of the authoring tool, twice: as structured content, and as text. The text content has two blocks, and the first is the `say` sentence in the asked language, so a client that shows only the first block already shows something a person can read; the second is the answer as JSON. A coded refusal (an unknown id, a draft that is not ready, an input the schema refuses, a workspace that refuses a request) is a normal tool result with `isError` true and the code, hint and `say` in it, never a protocol error and never an exception. The language is the `language` of the call; when a call has none, the host's `DefaultLanguage` (English unless the host says Spanish).

**Resources.** One for each section of the guide, `brain://guide/<section>` (`overview`, `steps`, `routes`, `timeouts`, `loops`, `triggers`, `scenarios`, `labels`, `good`, `rules`, `method`, `examples`), as `text/markdown`. They hold the same text as `brain_guide`; `examples` also carries the two reference documents, so a client that loads resources up front gives the assistant the format before it writes a word.

**Prompt.** `model_a_process_with_me`, with the optional argument `language` (`en` or `es`; without it the host's default). It is one user message that sets the working method of the specification (section 7): read before writing, restate the request and ask about what is ambiguous, change the smallest thing, save the draft and read Brain's answer, fix every error first, add a scenario for each new behaviour, simulate the case the person described and tell them what the process did, propose only when Brain says ready, never claim a change is live. It also states the three rules above and tells the assistant to relay each `say` text as it is and never to show the person JSON, a pointer or a code. Another language is refused as invalid parameters.

## Mounting it in a host

The host builds its own MCP server, with its own tools if it has any, and adds Brain's with one call. The host supplies the store (and with it the catalog), the clock and the authorization; it chooses the transport.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Synapbiz.Brain.Authoring;
using Synapbiz.Brain.Mcp;

var store = new InMemoryAuthoringStore(approvedFlows, approvedRules, catalogJson); // or FileAuthoringStore, or your own IAuthoringStore

builder.Services
    .AddMcpServer()                     // the host's own server, transport and tools
    .WithBrainAuthoring(new BrainMcpOptions
    {
        Store = store,                                           // approved documents, catalog, drafts, proposals
        Authoring = new AuthoringOptions { Clock = timeProvider }, // the only clock the tools use
        DefaultLanguage = "es",                                  // when a call does not say
        Authorize = (request, cancellationToken) =>              // before every tool call, resource read and prompt
            ValueTask.FromResult(request.User?.IsInRole("process-author") == true),
    });
```

A host that registers primitives itself takes the lists instead: `var brain = BrainMcp.Create(options);` gives `brain.Tools`, `brain.Resources` and `brain.Prompts` (and `brain.ToServerOptions()` for a server built without a host). Calling the extension and `BrainMcp.Create` registers nothing twice: the names of the tools, the URIs of the resources and the name of the prompt are unique, and a test pins it, with a host tool next to them.

`BrainMcpOptions`:

| Member | Meaning |
| --- | --- |
| `Store` (required) | The workspace behind the tools: `FileAuthoringStore` for a folder, `InMemoryAuthoringStore` for tests, or the host's own `IAuthoringStore`. It reads the approved documents and the catalog and writes only drafts and proposals. |
| `Authoring` | `AuthoringOptions`: the clock (session expiry and the time a proposal carries), how long a simulation lives (30 minutes), how many may live at once (20), the largest document (1,000,000 characters). |
| `DefaultLanguage` | `en` (default) or `es`. A call that carries `language` wins. |
| `Authorize` | A function `(BrainMcpRequest, CancellationToken) -> ValueTask<bool>`. `BrainMcpRequest` has `Kind` (`tool`, `resource` or `prompt`), `Name` (tool name, resource URI or prompt name) and `User` (the `ClaimsPrincipal` of the connection when the transport has one, null on stdio). False stops the call before the tool runs: a tool answers with `isError` true and the code `mcp.not_authorized`; a resource or a prompt is refused as a protocol error (there is no result shape for it). Null allows everything, which is right only for one person on their own machine. |

## The `brain-mcp` program

`Synapbiz.Brain.Mcp.Tool` is a .NET tool with the command `brain-mcp`: the same tools over standard input and output for a folder of documents, which is what a desktop or coding assistant connects to on an author's machine.

```text
brain-mcp --workspace <folder> [--catalog <file>] [--language en|es]
```

| Argument | Meaning |
| --- | --- |
| `--workspace <folder>` | Required. The workspace layout of [brain-authoring](brain-authoring.md#the-workspace): approved documents in `flows/` and `rules/`; drafts and proposals are written under `drafts/` and `proposals/` and nowhere else. A `FileAuthoringStore` serves it. |
| `--catalog <file>` | A `brain.catalog@1` file of what the host offers. It is checked when the program starts. It takes the place of the workspace's own `catalog.json`; without the option the `catalog.json` of the workspace is used if there is one. |
| `--language en\|es` | The language of the answers when a call does not say. Default `en`. |
| `--help`, `--version` | Print to the error stream and exit 0. |

Standard output carries the protocol and nothing else. The program writes one line to standard error when it starts (`brain-mcp: serving <folder> over standard input and output (language en).`) and one line for any problem, and logs nothing else. It ends with 0 when the client closes the connection.

| Exit code | Meaning |
| --- | --- |
| 0 | The client closed the connection (or `--help`, `--version`). |
| 1 | The connection failed while serving. |
| 2 | The arguments are wrong (a missing, unknown or valueless argument, a language other than `en` or `es`). |
| 3 | The workspace folder is missing or cannot be read. |
| 4 | The catalog file is missing, cannot be read or is not a valid `brain.catalog@1`. |

Without a catalog the tools treat what a process may name as not restricted, and `brain_catalog` says so in words ("the host has not limited what processes may use"); with a catalog a process that names a port, event, queue or stage outside it is refused as not available here.

### Installing and connecting an assistant

From the private feed (`Synapbiz.Brain.Mcp.Tool` is published with the other packages when a version tag is pushed), or from a checkout with `dotnet run --project src/Synapbiz.Brain.Mcp.Tool -- <arguments>`:

```text
dotnet tool install --global Synapbiz.Brain.Mcp.Tool --add-source <the GitHub Packages feed of the owner>
```

Any MCP client that starts a server as a child process takes a configuration of this shape (the file and the key names are the client's; the command and arguments are the program's):

```json
{
  "mcpServers": {
    "brain": {
      "command": "brain-mcp",
      "args": ["--workspace", "/path/to/workspace", "--catalog", "/path/to/catalog.json", "--language", "en"]
    }
  }
}
```

Use absolute paths: a client starts the program from a folder of its own. From a checkout, `"command": "dotnet"` and `"args": ["run", "--project", "/path/to/src/Synapbiz.Brain.Mcp.Tool", "--", "--workspace", "..."]`. The person then asks the assistant for the prompt `model_a_process_with_me` (or the assistant reads the `brain://guide/method` resource) and describes the change they want in plain words.

The course of a conversation is the one the end-to-end test drives against the built program over real standard input and output: list the tools, `flow_list`, `flow_get`, save a broken draft, read the hints, fix it, simulate, add a scenario, propose; at the end the proposal is a file under `proposals/` and every approved document is byte for byte what it was.

## What the host must supply and enforce

The adapter forwards the tools and decides nothing about who may use them. A host that mounts it owns these:

- **The store.** Where the approved documents come from, where drafts and proposals go, and that nothing else is written. `FileAuthoringStore` already refuses a path outside `drafts` and `proposals` (traversal, absolute paths, links). A host with its own `IAuthoringStore` must keep that contract.
- **The catalog.** A process loads only against the fact contracts the catalog carries (`contracts`); a workspace without them shows its processes as unreadable. What the practice offers: ports (kind, delivery, whether they contact a person), events, queues, stage names and the fact contracts of the rules, as `brain.catalog@1` through `IAuthoringStore.ReadCatalog()`. Without ports in it a process that reads or acts is not ready (`authoring.ready.catalog_missing`).
- **Authorization.** Who may connect and which calls they may make: the transport's authentication, and `BrainMcpOptions.Authorize` for each call. The server enforces no role; deciding that a physician may propose a rule and that a process proposal is reviewed by the product team is the host's. Because the adapter holds one set of tools, drafts and simulation sessions are shared by every connection to the same `BrainMcp`: a host that serves several people builds one set (and one store, hence one workspace) for each person, with `BrainMcp.Create`, instead of sharing one.
- **The clock.** `AuthoringOptions.Clock`: the time a proposal carries and the expiry of simulations. The libraries never read the machine clock.
- **Applying proposals.** The server activates nothing. A proposal is a file; applying it (a commit in a repository, an activation in the host) is a person's step outside these tools. A proposal for a process whose base moved since is refused when it is written (`authoring.proposal.base_stale`), and `FlowProposalCheck.Against` re-checks a stored proposal before the host applies it.

### `needsReview`

Every proposal file carries `needsReview`, and the answer of `flow_propose` and `rule_propose` repeats it under `proposal.needsReview`. The host applies the rule of the owner (specification, section 10, decision 1a):

- A **process** proposal always has `needsReview` true. The host holds it as "waiting for review" until the product team accepts it, and activates nothing in the meantime.
- A **rule** proposal has `needsReview` false when the draft stays within the facts the rule already reads and the outcomes it already gives. The host may apply it when the clinical partner who owns the rule approves it. It has `needsReview` true, with `needsReviewReasons` (`kind_changed`, `hit_policy_changed`, `new_fact_read`, `new_outcome_field`, `new_outcome_value`), when it goes beyond that; the host holds it for the product team like a process proposal. A rule proposal file without the member counts as needing review.

Neither the tools nor the adapter enforce this; the host and the repository do.

### Simulation tokens

The token of a simulation is the only handle on a case. `BrainMcp.Create` gives it 24 random bytes (`sim-` and 32 characters) unless `AuthoringOptions.SessionIds` is set. The official SDK registers one tool set for every connection of a host, so sessions are shared by the connections of one process; a host that needs them apart serves one `BrainMcp` per user. A document nested deeper than the protocol reads is refused with a coded answer and a sentence (`mcp.document_too_deep`) where the adapter can catch it.

## Not here

Any model call or vendor code; activating or deploying a proposal; reading live cases or the host's database; a screen for proposals; authorization (the host's); packs and their settings (section 4.1 of the specification, undecided); an HTTP transport of our own (a host that wants one adds the SDK's HTTP package to its own application and calls `WithBrainAuthoring`).
