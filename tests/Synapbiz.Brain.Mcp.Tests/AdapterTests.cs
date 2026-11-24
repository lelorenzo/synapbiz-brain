using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp.Tests;

/// <summary>The adapter: every authoring tool becomes one MCP tool, the guide becomes resources, the method becomes a prompt.</summary>
public sealed class AdapterTests
{
    private static readonly string[] ToolNames =
    [
        "brain_guide", "brain_catalog", "flow_list", "flow_get", "rule_list", "rule_get", "flow_draft_save", "flow_draft_patch", "flow_draft_simulate",
        "flow_draft_add_scenario", "rule_draft_edit", "rule_simulate", "rule_test", "flow_propose", "rule_propose", "proposal_list", "proposal_get",
    ];

    private static async Task<(JsonObject Answer, CallToolResult Result)> Call(McpClient client, string name, string arguments = "{}")
    {
        var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments)!;
        var result = await client.CallToolAsync(name, input.ToDictionary(item => item.Key, item => (object?)item.Value));
        return ((JsonObject)JsonNode.Parse(result.StructuredContent!.Value.GetRawText())!, result);
    }

    private static Uri Guide(string section) => new("brain://guide/" + section);

    private static string FirstText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Fact]
    public async Task Every_authoring_tool_is_listed_with_its_name_description_and_schema()
    {
        var tools = new AuthoringTools(Fixtures.NoteStore());
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var listed = await connection.Client.ListToolsAsync();

        listed.Select(tool => tool.Name).Should().Equal(ToolNames);
        listed.Select(tool => tool.Name).Should().Equal(tools.All.Select(tool => tool.Name));
        foreach (var tool in listed)
        {
            var source = tools.Find(tool.Name)!;
            tool.Description.Should().Be(source.Description);
            JsonNode.DeepEquals(JsonNode.Parse(tool.JsonSchema.GetRawText()), source.InputSchema).Should().BeTrue(tool.Name + " carries the JSON Schema of the authoring tool");
            tool.JsonSchema.GetProperty("type").GetString().Should().Be("object");
        }
    }

    [Fact]
    public async Task Tools_that_only_read_say_so_and_none_claims_to_reach_outside()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var listed = await connection.Client.ListToolsAsync();

        listed.Where(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true).Select(tool => tool.Name)
            .Should().BeEquivalentTo("brain_guide", "brain_catalog", "flow_list", "flow_get", "rule_list", "rule_get", "rule_simulate", "rule_test", "proposal_list", "proposal_get");
        listed.Should().OnlyContain(tool => tool.ProtocolTool.Annotations!.OpenWorldHint == false && tool.ProtocolTool.Annotations.DestructiveHint == false);
    }

    [Fact]
    public async Task A_success_is_the_say_text_first_then_the_json_and_the_same_json_as_structured_content()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var (answer, result) = await Call(connection.Client, "flow_list");

        result.IsError.Should().NotBe(true);
        answer["ok"]!.GetValue<bool>().Should().BeTrue();
        answer["flows"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()).Should().Equal("visit.note_wait");
        result.Content.Should().HaveCount(2).And.AllBeOfType<TextContentBlock>();
        FirstText(result).Should().Be(answer["say"]!.GetValue<string>());
        JsonNode.DeepEquals(JsonNode.Parse(((TextContentBlock)result.Content[1]).Text), answer).Should().BeTrue("the second text is the whole answer");
    }

    [Fact]
    public async Task A_refusal_is_a_normal_result_with_is_error_true_and_the_code_and_the_say_text()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var (unknown, unknownResult) = await Call(connection.Client, "flow_get", """{"id":"nothing.here"}""");
        var (invalid, invalidResult) = await Call(connection.Client, "flow_get", """{"force":true}""");

        unknownResult.IsError.Should().BeTrue();
        unknown["ok"]!.GetValue<bool>().Should().BeFalse();
        unknown["error"]!["code"]!.GetValue<string>().Should().Be("authoring.flow.not_found");
        unknown["error"]!["hint"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        FirstText(unknownResult).Should().Be(unknown["say"]!.GetValue<string>()).And.NotBeNullOrWhiteSpace();
        ((TextContentBlock)unknownResult.Content[1]).Text.Should().Contain("authoring.flow.not_found");
        invalidResult.IsError.Should().BeTrue("an input the schema refuses is a coded answer too, not a protocol error");
        invalid["error"]!["code"]!.GetValue<string>().Should().Be("authoring.input.invalid");
    }

    [Fact]
    public async Task A_draft_that_is_not_ready_is_refused_by_the_proposal_tool_as_a_result_not_an_exception()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var (answer, result) = await Call(connection.Client, "flow_propose", """{"id":"visit.note_wait","summary":"s","requestedBy":"Front desk lead"}""");

        result.IsError.Should().BeTrue();
        answer["error"]!["code"]!.GetValue<string>().Should().Be("authoring.draft.not_found");
    }

    [Fact]
    public async Task The_language_of_a_call_wins_and_the_default_language_of_the_host_applies_otherwise()
    {
        await using var english = await Fixtures.Connect(Fixtures.Options());
        await using var spanish = await Fixtures.Connect(Fixtures.Options(language: "es"));

        var (enAnswer, _) = await Call(english.Client, "flow_get", """{"id":"nothing.here"}""");
        var (esAnswer, _) = await Call(english.Client, "flow_get", """{"id":"nothing.here","language":"es"}""");
        var (defaultEs, _) = await Call(spanish.Client, "flow_get", """{"id":"nothing.here"}""");
        var (explicitEn, _) = await Call(spanish.Client, "flow_get", """{"id":"nothing.here","language":"en"}""");

        esAnswer["say"]!.GetValue<string>().Should().Contain("proceso");
        defaultEs["say"]!.GetValue<string>().Should().Be(esAnswer["say"]!.GetValue<string>());
        explicitEn["say"]!.GetValue<string>().Should().Be(enAnswer["say"]!.GetValue<string>());
        enAnswer["say"]!.GetValue<string>().Should().NotBe(esAnswer["say"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_draft_and_a_simulation_session_live_across_calls()
    {
        var options = Fixtures.Options();
        await using var connection = await Fixtures.Connect(options);
        var document = JsonNode.Parse(Fixtures.NoteWait)!;

        var (saved, _) = await Call(connection.Client, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = document }.ToJsonString());
        var (started, _) = await Call(connection.Client, "flow_draft_simulate", """{"id":"visit.note_wait","start":{"input":{"case_ref":"c-1001"}}}""");

        saved["ready"]!.GetValue<bool>().Should().BeTrue();
        started["ok"]!.GetValue<bool>().Should().BeTrue();
        started["session"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        options.Store.ListDrafts(DocumentKind.Flow).Should().Equal("visit.note_wait");
    }

    [Fact]
    public async Task The_host_authorization_stops_a_tool_before_it_runs()
    {
        var asked = new List<BrainMcpRequest>();
        var options = Fixtures.Options(authorize: (request, _) =>
        {
            asked.Add(request);
            return ValueTask.FromResult(request.Name != "flow_draft_save");
        });
        await using var connection = await Fixtures.Connect(options);

        var (listed, listedResult) = await Call(connection.Client, "flow_list");
        var (denied, deniedResult) = await Call(connection.Client, "flow_draft_save", new JsonObject { ["id"] = "visit.note_wait", ["document"] = JsonNode.Parse(Fixtures.NoteWait) }.ToJsonString());

        listedResult.IsError.Should().NotBe(true);
        listed["ok"]!.GetValue<bool>().Should().BeTrue();
        deniedResult.IsError.Should().BeTrue();
        denied["error"]!["code"]!.GetValue<string>().Should().Be("mcp.not_authorized");
        FirstText(deniedResult).Should().Be(denied["say"]!.GetValue<string>());
        asked.Select(request => (request.Kind, request.Name)).Should().Equal(("tool", "flow_list"), ("tool", "flow_draft_save"));
        options.Store.ListDrafts(DocumentKind.Flow).Should().BeEmpty("a tool the host did not allow never ran");
    }

    [Fact]
    public async Task The_authorization_also_covers_resources_and_the_prompt()
    {
        var options = Fixtures.Options(authorize: (request, _) => ValueTask.FromResult(request.Kind == "tool"));
        await using var connection = await Fixtures.Connect(options);

        var resource = async () => await connection.Client.ReadResourceAsync(Guide("overview"));
        var prompt = async () => await connection.Client.GetPromptAsync("model_a_process_with_me");

        await resource.Should().ThrowAsync<McpException>();
        await prompt.Should().ThrowAsync<McpException>();
    }

    [Fact]
    public async Task The_authorization_sees_the_user_of_the_request_when_the_transport_has_one()
    {
        ClaimsPrincipal? seen = null;
        await using var connection = await Fixtures.Connect(Fixtures.Options(authorize: (request, _) =>
        {
            seen = request.User;
            return ValueTask.FromResult(true);
        }));

        _ = await Call(connection.Client, "flow_list");

        seen.Should().BeNull("an in-memory connection carries no user; an HTTP host would put its principal here");
    }

    [Fact]
    public async Task Every_section_of_the_guide_is_a_resource_that_reads_back_its_text()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var listed = await connection.Client.ListResourcesAsync();

        listed.Select(resource => resource.Uri).Should().Equal(BrainGuide.Sections.Select(section => "brain://guide/" + section.Id));
        foreach (var section in BrainGuide.Sections)
        {
            var read = await connection.Client.ReadResourceAsync(Guide(section.Id));
            var text = read.Contents.Should().ContainSingle().Which.Should().BeOfType<TextResourceContents>().Subject;
            text.MimeType.Should().Be("text/markdown");
            text.Text.Should().Contain(section.Title).And.Contain(section.Text);
        }
    }

    [Fact]
    public async Task The_examples_resource_carries_the_reference_documents()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var read = await connection.Client.ReadResourceAsync(Guide("examples"));

        var text = ((TextResourceContents)read.Contents[0]).Text;
        foreach (var example in BrainGuide.ExampleDocuments)
        {
            text.Should().Contain("## " + example.Name);
            text.Should().Contain(example.Text.TrimEnd());
        }
    }

    [Fact]
    public async Task A_resource_that_does_not_exist_is_refused()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var read = async () => await connection.Client.ReadResourceAsync(Guide("cooking"));

        await read.Should().ThrowAsync<McpException>();
    }

    [Fact]
    public async Task The_prompt_sets_the_working_method_in_english()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var prompts = await connection.Client.ListPromptsAsync();
        var prompt = await connection.Client.GetPromptAsync("model_a_process_with_me", new Dictionary<string, object?> { ["language"] = "en" });

        prompts.Should().ContainSingle().Which.Name.Should().Be("model_a_process_with_me");
        prompts[0].ProtocolPrompt.Arguments.Should().ContainSingle().Which.Name.Should().Be("language");
        var text = ((TextContentBlock)prompt.Messages.Should().ContainSingle().Which.Content).Text;
        text.Should().Contain(BrainGuide.WorkingMethod).And.Contain("never activate").And.Contain("Speak to the person in English");
    }

    [Fact]
    public async Task The_prompt_sets_the_working_method_in_spanish_and_never_says_flujo()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options());

        var prompt = await connection.Client.GetPromptAsync("model_a_process_with_me", new Dictionary<string, object?> { ["language"] = "es" });

        var text = ((TextContentBlock)prompt.Messages.Single().Content).Text;
        text.Should().Contain("Método de trabajo").And.Contain("proceso").And.Contain("nunca activas");
        text.Should().NotContainEquivalentOf("flujo");
        WithoutToolNames(text).Should().NotContainEquivalentOf("flow", "person-facing words say proceso; only tool names keep the word");
        Steps(text[text.IndexOf("Método de trabajo", StringComparison.Ordinal)..]).Should().Be(Steps(BrainGuide.WorkingMethod), "the Spanish method has the same eight steps as the English one");
    }

    [Fact]
    public async Task The_prompt_follows_the_default_language_of_the_host_and_refuses_another_one()
    {
        await using var connection = await Fixtures.Connect(Fixtures.Options(language: "es"));

        var prompt = await connection.Client.GetPromptAsync("model_a_process_with_me");
        var other = async () => await connection.Client.GetPromptAsync("model_a_process_with_me", new Dictionary<string, object?> { ["language"] = "fr" });

        ((TextContentBlock)prompt.Messages.Single().Content).Text.Should().Contain("Método de trabajo");
        await other.Should().ThrowAsync<McpException>();
    }

    [Fact]
    public async Task Nothing_is_registered_twice_and_the_host_keeps_its_own_tools()
    {
        var own = McpServerTool.Create(() => "ok", new McpServerToolCreateOptions { Name = "host_ping", Description = "The host's own tool." });
        var services = new ServiceCollection();
        services.AddMcpServer().WithTools([own]).WithBrainAuthoring(Fixtures.Options());
        await using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
        var tools = provider.GetServices<McpServerTool>().Select(tool => tool.ProtocolTool.Name).ToList();
        var resources = provider.GetServices<McpServerResource>().Select(resource => resource.ProtocolResource!.Uri).ToList();
        var prompts = provider.GetServices<McpServerPrompt>().Select(prompt => prompt.ProtocolPrompt.Name).ToList();

        tools.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(ToolNames.Append("host_ping"));
        resources.Should().OnlyHaveUniqueItems().And.HaveCount(BrainGuide.Sections.Count);
        prompts.Should().Equal("model_a_process_with_me");
        server.ToolCollection.Should().NotBeNull();
        (await Task.FromResult(server.ToolCollection!.PrimitiveNames)).Should().OnlyHaveUniqueItems().And.HaveCount(18);
    }

    [Fact]
    public void The_list_for_a_host_that_registers_tools_itself_has_the_same_primitives()
    {
        var brain = BrainMcp.Create(Fixtures.Options());

        brain.Tools.Select(tool => tool.ProtocolTool.Name).Should().Equal(ToolNames);
        brain.Resources.Should().HaveCount(BrainGuide.Sections.Count);
        brain.Prompts.Should().ContainSingle();
        var options = brain.ToServerOptions();
        options.ToolCollection!.Count.Should().Be(17);
        options.ResourceCollection!.Count.Should().Be(BrainGuide.Sections.Count);
        options.PromptCollection!.Count.Should().Be(1);
    }

    [Fact]
    public void A_store_is_required_and_the_default_language_must_be_known()
    {
        var withoutStore = () => BrainMcp.Create(null!);
        var badLanguage = () => BrainMcp.Create(new BrainMcpOptions { Store = Fixtures.NoteStore(), DefaultLanguage = "fr" });

        withoutStore.Should().Throw<ArgumentNullException>();
        badLanguage.Should().Throw<ArgumentException>();
    }

    private static string WithoutToolNames(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"`[a-z_]+`", string.Empty);

    private static int Steps(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"(?m)^[1-8]\. ").Count;
}
