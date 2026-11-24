using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Synapbiz.Brain.Mcp.Tests;

/// <summary>
/// The built <c>brain-mcp</c> started as a child process on a temporary copy of a workspace made from the reference documents, driven by the SDK's
/// own client over real standard input and output.
/// </summary>
public sealed class StdioEndToEndTests
{
    private static readonly int[] RequestIds = [1, 2, 3, 4, 5, 6, 7];

    private static StdioClientTransport Transport(string folder, List<string> stderr, params string[] more) => new(new StdioClientTransportOptions
    {
        Name = "brain-mcp",
        Command = Environment.ProcessPath!,
        Arguments = [Program.ToolAssembly(), "--workspace", folder, .. more],
        StandardErrorLines = stderr.Add,
    });

    private static async Task<JsonObject> Call(McpClient client, string name, JsonObject? arguments = null)
    {
        var input = (arguments ?? []).ToDictionary(item => item.Key, item => (object?)JsonSerializer.SerializeToElement(item.Value));
        var result = await client.CallToolAsync(name, input);
        var answer = (JsonObject)JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!;
        result.IsError.Should().Be(!answer["ok"]!.GetValue<bool>(), name + " is an error result exactly when the answer is a refusal");
        ((TextContentBlock)result.Content[0]).Text.Should().Be(answer["say"]!.GetValue<string>(), "the say text comes first");
        return answer;
    }

    [Fact]
    public async Task An_assistant_goes_from_a_request_to_a_proposal_and_nothing_else_in_the_workspace_changes()
    {
        var folder = Fixtures.MakeWorkspace();
        var stderr = new List<string>();
        try
        {
            var before = Program.Snapshot(folder);
            await using var client = await McpClient.CreateAsync(Transport(folder, stderr));

            // List what the server offers.
            var tools = await client.ListToolsAsync();
            tools.Should().HaveCount(17);
            tools.Select(tool => tool.Name).Should().Contain(["flow_list", "flow_get", "flow_draft_save", "flow_draft_patch", "flow_draft_simulate", "flow_draft_add_scenario", "flow_propose"]);

            // Read before writing.
            var list = await Call(client, "flow_list");
            list["flows"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Should().Equal("visit.note_wait");
            var get = await Call(client, "flow_get", new JsonObject { ["id"] = "visit.note_wait" });
            var document = (JsonObject)get["document"]!;

            // Save a broken draft (a route to a step that does not exist) and read the hint.
            document["version"] = 2;
            document["body"]!["steps"]!["await_note"]!["onExpired"] = "front_desk_call";
            var broken = await Call(client, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = document.DeepClone() });
            broken["ok"]!.GetValue<bool>().Should().BeTrue("a draft that does not load is saved and described, not refused");
            broken["loaded"]!.GetValue<bool>().Should().BeFalse();
            broken["ready"]!.GetValue<bool>().Should().BeFalse();
            broken["errors"]!.AsArray()[0]!["hint"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();

            // Fix it.
            var fixedUp = await Call(client, "flow_draft_patch", new JsonObject
            {
                ["id"] = "visit.note_wait",
                ["operations"] = new JsonArray(
                    new JsonObject { ["op"] = "replace", ["path"] = "/body/steps/await_note/onExpired", ["value"] = "chase_note" },
                    new JsonObject { ["op"] = "add", ["path"] = "/body/steps/confirm_note/show/-", ["value"] = "note_status" }),
            });
            fixedUp["loaded"]!.GetValue<bool>().Should().BeTrue();
            fixedUp["changes"]!["items"]!.AsArray().Should().NotBeEmpty();

            // Simulate the case the person described.
            var started = await Call(client, "flow_draft_simulate", new JsonObject { ["id"] = "visit.note_wait", ["start"] = new JsonObject { ["input"] = new JsonObject { ["case_ref"] = "c-1001" } } });
            var session = started["session"]!.GetValue<string>();
            var effect = started["outstanding"]!.AsArray()[0]!["id"]!.GetValue<string>();
            var answered = await Call(client, "flow_draft_simulate", new JsonObject
            {
                ["session"] = session,
                ["command"] = new JsonObject { ["type"] = "complete", ["effect"] = effect, ["output"] = new JsonObject { ["status"] = "signed" } },
            });
            answered["status"]!.GetValue<string>().Should().Be("Completed");

            // Add the case as a test and propose.
            var added = await Call(client, "flow_draft_add_scenario", new JsonObject
            {
                ["id"] = "visit.note_wait",
                ["fromSimulation"] = new JsonObject { ["session"] = session, ["name"] = "a made-up visit whose note is signed at once" },
            });
            added["ready"]!.GetValue<bool>().Should().BeTrue(added["say"]!.GetValue<string>());
            var proposed = await Call(client, "flow_propose", new JsonObject { ["id"] = "visit.note_wait", ["summary"] = "The front desk learns the note was signed meanwhile.", ["requestedBy"] = "Front desk lead" });
            proposed["ok"]!.GetValue<bool>().Should().BeTrue(proposed.ToJsonString());
            proposed["proposal"]!["needsReview"]!.GetValue<bool>().Should().BeTrue();
            proposed["proposal"]!["applied"]!.GetValue<bool>().Should().BeFalse();

            // The proposal is a file under proposals.
            var file = Path.Combine(folder, "proposals", proposed["proposal"]!["name"]!.GetValue<string>() + ".json");
            File.Exists(file).Should().BeTrue();
            JsonNode.Parse(await File.ReadAllTextAsync(file))!["$spec"]!.GetValue<string>().Should().Be("brain.process-proposal@1");
            var read = await Call(client, "proposal_get", new JsonObject { ["name"] = proposed["proposal"]!["name"]!.GetValue<string>() });
            read["text"]!.GetValue<string>().Should().Contain("Nothing changes until a person applies");

            // Nothing outside drafts and proposals changed: every approved document is byte-identical.
            var after = Program.Snapshot(folder);
            after.Keys.Where(path => !path.StartsWith("drafts/", StringComparison.Ordinal) && !path.StartsWith("proposals/", StringComparison.Ordinal))
                .Should().BeEquivalentTo(before.Keys, "no file appeared or vanished outside drafts and proposals");
            foreach (var (path, hash) in before)
            {
                after[path].Should().Be(hash, path + " is not changed");
            }

            after.Keys.Should().Contain(path => path.StartsWith("drafts/flows/", StringComparison.Ordinal)).And.Contain(path => path.StartsWith("proposals/", StringComparison.Ordinal));
            stderr.Should().Contain(line => line.Contains("serving", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task The_catalog_file_of_the_command_line_is_the_one_the_tools_check_against()
    {
        var folder = Fixtures.MakeWorkspace(withCatalog: false);
        var catalog = Path.Combine(folder, "host-catalog.json");
        await File.WriteAllTextAsync(catalog, Fixtures.CatalogText());
        var stderr = new List<string>();
        try
        {
            await using var with = await McpClient.CreateAsync(Transport(folder, stderr, "--catalog", catalog, "--language", "es"));
            var given = await Call(with, "brain_catalog");

            given["restricted"]!.GetValue<bool>().Should().BeTrue();
            given["ports"]!.AsArray().Select(port => port!["name"]!.GetValue<string>()).Should().Equal("records.note_status", "messages.send_reminder");
            given["say"]!.GetValue<string>().Should().Contain("puertos", "the default language of the program is the one asked with --language");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Without_a_catalog_the_tools_say_that_nothing_is_restricted()
    {
        var folder = Fixtures.MakeWorkspace(withCatalog: false);
        var stderr = new List<string>();
        try
        {
            await using var client = await McpClient.CreateAsync(Transport(folder, stderr));
            var answer = await Call(client, "brain_catalog");

            answer["restricted"]!.GetValue<bool>().Should().BeFalse();
            answer["say"]!.GetValue<string>().Should().Contain("has not limited");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Standard_output_carries_only_protocol_messages_and_the_program_ends_with_0_when_the_client_leaves()
    {
        var folder = Fixtures.MakeWorkspace();
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Program.ToolAssembly());
            start.ArgumentList.Add("--workspace");
            start.ArgumentList.Add(folder);
            using var process = System.Diagnostics.Process.Start(start)!;
            var standardError = process.StandardError.ReadToEndAsync();

            var document = JsonNode.Parse(Fixtures.NoteWait)!;
            var requests = new[]
            {
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"raw","version":"1"}}}""",
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"flow_list","arguments":{}}}""",
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"flow_get","arguments":{"id":"nothing.here"}}}""",
                new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = 5,
                    ["method"] = "tools/call",
                    ["params"] = new JsonObject { ["name"] = "flow_draft_save", ["arguments"] = new JsonObject { ["id"] = "visit.note_wait", ["document"] = document } },
                }.ToJsonString(),
                """{"jsonrpc":"2.0","id":6,"method":"resources/list"}""",
                """{"jsonrpc":"2.0","id":7,"method":"prompts/get","params":{"name":"model_a_process_with_me","arguments":{"language":"es"}}}""",
            };
            foreach (var request in requests)
            {
                await process.StandardInput.WriteLineAsync(request);
            }

            await process.StandardInput.FlushAsync();

            // Read until every request has its answer; only then does the client leave, as a real client does.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var lines = new List<string>();
            while (lines.Count(line => JsonNode.Parse(line)!["id"] is not null) < RequestIds.Length)
            {
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                line.Should().NotBeNull("the server must not close standard output before it has answered");
                lines.Add(line!);
            }

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            var rest = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            rest.Should().BeNullOrWhiteSpace("nothing follows the last answer");

            process.ExitCode.Should().Be(0);
            var ids = new List<int>();
            foreach (var line in lines)
            {
                var message = JsonNode.Parse(line).Should().BeAssignableTo<JsonObject>(line).Subject;
                message["jsonrpc"]!.GetValue<string>().Should().Be("2.0", "every line of standard output is a JSON-RPC message: " + line);
                if (message["id"] is { } id)
                {
                    ids.Add(id.GetValue<int>());
                    message["error"].Should().BeNull("no request ended in a protocol error: " + line);
                }
            }

            ids.Should().BeEquivalentTo(RequestIds);
            var diagnostics = (await standardError).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            diagnostics.Should().ContainSingle().Which.Should().StartWith("brain-mcp: serving");
            File.Exists(Path.Combine(folder, "drafts", "flows", "visit.note_wait.json")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
