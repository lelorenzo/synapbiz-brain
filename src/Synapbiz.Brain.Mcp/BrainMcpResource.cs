using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Synapbiz.Brain.Authoring;

namespace Synapbiz.Brain.Mcp;

/// <summary>One section of the Brain guide as an MCP resource at <c>brain://guide/&lt;section&gt;</c>.</summary>
internal sealed class BrainMcpResource : McpServerResource
{
    /// <summary>The scheme and folder every guide resource lives under.</summary>
    internal const string UriPrefix = Scheme + "://guide/";

    private const string Scheme = "brain";

    private readonly BrainMcpOptions options;
    private readonly string text;

    internal BrainMcpResource(GuideSection section, BrainMcpOptions options)
    {
        this.options = options;
        text = Render(section);
        var uri = UriPrefix + section.Id;
        ProtocolResource = new Resource
        {
            Uri = uri,
            Name = "brain_guide_" + section.Id,
            Title = section.Title,
            Description = "Section \"" + section.Title + "\" of the Brain guide for assistants: the same text as the tool brain_guide with section " + section.Id + ".",
            MimeType = "text/markdown",
        };
        ProtocolResourceTemplate = new ResourceTemplate
        {
            UriTemplate = uri,
            Name = ProtocolResource.Name,
            Title = section.Title,
            Description = ProtocolResource.Description,
            MimeType = ProtocolResource.MimeType,
        };
    }

    /// <inheritdoc />
    public override Resource ProtocolResource { get; }

    /// <inheritdoc />
    public override ResourceTemplate ProtocolResourceTemplate { get; }

    /// <inheritdoc />
    public override IReadOnlyList<object> Metadata { get; } = [];

    /// <inheritdoc />
    public override bool IsMatch(string uri) => string.Equals(uri, ProtocolResource.Uri, StringComparison.Ordinal);

    /// <inheritdoc />
    public override async ValueTask<ReadResourceResult> ReadAsync(RequestContext<ReadResourceRequestParams> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (options.Authorize is not null && !await options.Authorize(new BrainMcpRequest("resource", ProtocolResource.Uri, request.User), cancellationToken).ConfigureAwait(false))
        {
            throw new McpException("The host did not allow reading " + ProtocolResource.Uri + ".");
        }

        return new ReadResourceResult
        {
            Contents = [new TextResourceContents { Uri = ProtocolResource.Uri, MimeType = ProtocolResource.MimeType, Text = text }],
        };
    }

    private static string Render(GuideSection section)
    {
        var text = "# " + section.Title + "\n\n" + section.Text;
        if (section.Id != BrainGuide.ExamplesId)
        {
            return text;
        }

        return text + string.Concat(BrainGuide.ExampleDocuments.Select(example => "\n\n## " + example.Name + "\n\n" + example.Title + "\n\n```json\n" + example.Text.TrimEnd() + "\n```"));
    }
}
