using System.Text.Json.Nodes;
using Synapbiz.Brain.Workbench;

namespace Synapbiz.Brain.Authoring;

/// <summary>The two shapes every tool answer takes: a success that carries data, and a coded refusal.</summary>
internal static class Results
{
    /// <summary>A success: <c>ok</c>, then <c>say</c>, then the data members.</summary>
    internal static JsonObject Ok(string say, JsonObject data)
    {
        var answer = new JsonObject { ["ok"] = true, ["say"] = say };
        foreach (var (name, value) in data.ToArray())
        {
            data.Remove(name);
            answer[name] = value;
        }

        return answer;
    }

    /// <summary>A refusal: <c>ok</c> false, the code (<c>authoring.&lt;area&gt;.&lt;code&gt;</c>), a hint for the assistant and a sentence for the person.</summary>
    /// <param name="locale">The language of <c>say</c>.</param>
    /// <param name="code">The code without the <c>authoring.</c> prefix, which is also the key suffix of the sentence.</param>
    /// <param name="parameters">The values of the sentence.</param>
    /// <param name="hint">What the assistant should do about it, in English.</param>
    /// <param name="extra">More data to put in the answer.</param>
    internal static JsonObject Refuse(WorkbenchLocale locale, string code, (string Name, string Value)[] parameters, string hint, JsonObject? extra = null)
    {
        var answer = new JsonObject
        {
            ["ok"] = false,
            ["error"] = new JsonObject { ["code"] = "authoring." + code, ["hint"] = hint },
            ["say"] = AuthoringText.Text(locale, "refuse." + code, parameters),
        };
        if (extra is not null)
        {
            foreach (var (name, value) in extra.ToArray())
            {
                extra.Remove(name);
                answer[name] = value;
            }
        }

        return answer;
    }
}
