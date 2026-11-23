using System.Text.Json.Nodes;
using Synapbiz.Brain.Process;

namespace Synapbiz.Brain.Process.Hosting;

/// <summary>Writes and reads the payload of an inbox row: a JSON object holding the members of the trigger it stands for.</summary>
internal static class InboxPayloads
{
    internal static string Start(string inputJson) => new JsonObject { ["input"] = inputJson }.ToJsonString();

    internal static string Event(string name, string? correlation, string payloadJson) =>
        new JsonObject { ["name"] = name, ["correlation"] = correlation, ["payload"] = payloadJson }.ToJsonString();

    internal static string ChildCompleted(string childKey, string result, string outputJson) =>
        new JsonObject { ["childKey"] = childKey, ["result"] = result, ["output"] = outputJson }.ToJsonString();

    internal static string ChildFailed(string childKey, string code) =>
        new JsonObject { ["childKey"] = childKey, ["code"] = code }.ToJsonString();

    internal static string Cancel(string reason) => new JsonObject { ["reason"] = reason }.ToJsonString();

    internal static string StartInput(string payload) => Text(Parse(payload), "input");

    /// <summary>The trigger of a non-start inbox row.</summary>
    internal static ProcessTrigger ToTrigger(InboxKind kind, string payload)
    {
        var node = Parse(payload);
        return kind switch
        {
            InboxKind.Event => new EventReceived(Text(node, "name"), node["correlation"]?.GetValue<string>(), Text(node, "payload")),
            InboxKind.ChildCompleted => new ChildCompleted(Text(node, "childKey"), Text(node, "result"), Text(node, "output")),
            InboxKind.ChildFailed => new ChildFailed(Text(node, "childKey"), Text(node, "code")),
            InboxKind.Cancel => new CancelRequested(Text(node, "reason")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "An inbox row of this kind has no trigger."),
        };
    }

    private static JsonObject Parse(string payload) =>
        JsonNode.Parse(payload) as JsonObject ?? throw new InvalidOperationException("An inbox payload is not a JSON object.");

    private static string Text(JsonObject node, string member) =>
        node[member]?.GetValue<string>() ?? throw new InvalidOperationException("An inbox payload lacks the member " + member + ".");
}
