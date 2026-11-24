using FluentAssertions;
using Synapbiz.Brain.Mcp.Tool;

namespace Synapbiz.Brain.Mcp.Tests;

/// <summary>The arguments, exit codes and one-line errors of <c>brain-mcp</c>, driven in process.</summary>
public sealed class ProgramTests
{
    private static async Task<(int Code, string Error, byte[] Output)> Run(params string[] args)
    {
        using var error = new StringWriter();
        using var output = new MemoryStream();
        var code = await BrainMcpProgram.RunAsync(args, Stream.Null, output, error, CancellationToken.None);
        return (code, error.ToString(), output.ToArray());
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Theory]
    [InlineData("--catalog", "x.json")]
    [InlineData("--workspace")]
    [InlineData("--workspace", "--catalog", "x.json")]
    [InlineData("--workspace", "folder", "--language", "fr")]
    [InlineData("--workspace", "folder", "--unknown")]
    [InlineData("folder")]
    public async Task Wrong_arguments_exit_with_2_and_say_why_in_one_line_on_the_error_stream(params string[] args)
    {
        var (code, error, output) = await Run(args);

        code.Should().Be(BrainMcpProgram.Usage);
        Lines(error).Should().ContainSingle().Which.Should().StartWith("brain-mcp: ").And.Contain("--help");
        output.Should().BeEmpty("standard output is the protocol and nothing else");
    }

    [Fact]
    public async Task A_missing_workspace_exits_with_3_and_names_the_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "brain-mcp-missing-" + Guid.NewGuid().ToString("N"));

        var (code, error, output) = await Run("--workspace", folder);

        code.Should().Be(BrainMcpProgram.WorkspaceUnreadable);
        Lines(error).Should().ContainSingle().Which.Should().Contain(folder);
        output.Should().BeEmpty();
    }

    [Fact]
    public async Task A_workspace_that_is_a_file_exits_with_3()
    {
        var file = Path.GetTempFileName();
        try
        {
            var (code, error, _) = await Run("--workspace", file);

            code.Should().Be(BrainMcpProgram.WorkspaceUnreadable);
            Lines(error).Should().ContainSingle();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task A_catalog_that_is_missing_or_not_a_catalog_exits_with_4()
    {
        var folder = Fixtures.MakeWorkspace();
        var broken = Path.Combine(folder, "broken.json");
        await File.WriteAllTextAsync(broken, "this is not json");
        var wrongFormat = Path.Combine(folder, "other.json");
        await File.WriteAllTextAsync(wrongFormat, """{ "$spec": "brain.other@1" }""");
        try
        {
            foreach (var catalog in new[] { Path.Combine(folder, "absent.json"), broken, wrongFormat })
            {
                var (code, error, output) = await Run("--workspace", folder, "--catalog", catalog);

                code.Should().Be(BrainMcpProgram.CatalogUnreadable, catalog);
                Lines(error).Should().ContainSingle().Which.Should().Contain("catalog");
                output.Should().BeEmpty();
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    public async Task Help_and_version_exit_with_0_and_leave_standard_output_empty(string flag)
    {
        var (code, error, output) = await Run(flag);

        code.Should().Be(BrainMcpProgram.Success);
        error.Should().NotBeNullOrWhiteSpace();
        output.Should().BeEmpty();
    }

    [Fact]
    public async Task A_server_whose_client_has_gone_exits_with_0_and_writes_nothing_but_protocol_to_standard_output()
    {
        var folder = Fixtures.MakeWorkspace();
        try
        {
            var (code, error, output) = await Run("--workspace", folder, "--language", "es");

            code.Should().Be(BrainMcpProgram.Success);
            Lines(error).Should().ContainSingle().Which.Should().Contain("serving").And.Contain("language es");
            output.Should().BeEmpty("no message was exchanged, so nothing was written");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
