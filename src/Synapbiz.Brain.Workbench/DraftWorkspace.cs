using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nuvora.Decisions.Contracts.Tables;
using static Synapbiz.Brain.Workbench.DraftText;

namespace Synapbiz.Brain.Workbench;

/// <summary>The parsed text of a rule document and the edits applied to it. One workspace serves one <see cref="RuleDrafter.Apply"/> call.</summary>
internal sealed partial class DraftWorkspace
{
    internal const string BaseNotReadable = "workbench.draft.baseNotReadable";
    internal const string RowNotFound = "workbench.draft.rowNotFound";
    internal const string RowKeyTaken = "workbench.draft.rowKeyTaken";
    internal const string RowKeyNotValid = "workbench.draft.rowKeyNotValid";
    internal const string ConditionNotFound = "workbench.draft.conditionNotFound";
    internal const string NoDefaultRow = "workbench.draft.noDefaultRow";
    internal const string CaseNotFound = "workbench.draft.caseNotFound";
    internal const string CaseNameTaken = "workbench.draft.caseNameTaken";
    internal const string CaseNotValid = "workbench.draft.caseNotValid";
    internal const string MoveOntoItself = "workbench.draft.moveOntoItself";
    internal const string LabelNotValid = "workbench.draft.labelNotValid";

    private const string RuleFormat = "brain.rule@1";

    private readonly JsonObject root;
    private readonly JsonObject body;
    private readonly JsonArray rows;
    private readonly long version;
    private readonly HashSet<JsonObject> touched = [];
    private readonly HashSet<JsonObject> added = [];
    private int editIndex;

    private DraftWorkspace(JsonObject root, JsonObject body, JsonArray rows, long version)
    {
        this.root = root;
        this.body = body;
        this.rows = rows;
        this.version = version;
    }

    /// <summary>Parses the base document; an error when it is not a <c>brain.rule@1</c> object with a version and a <c>body.rows</c> array.</summary>
    internal static (DraftWorkspace? Workspace, WorkbenchError? Error) Open(string json)
    {
        if (TryParse(json) is JsonObject document
            && Text(document["$spec"]) == RuleFormat
            && document["body"] is JsonObject body
            && body["rows"] is JsonArray rows
            && Whole(document["version"]) is { } version)
        {
            return (new DraftWorkspace(document, body, rows, version), null);
        }

        return (null, new WorkbenchError(BaseNotReadable, string.Empty, $"The base is not a {RuleFormat} document with a version and a body.rows array."));
    }

    /// <summary>Applies one edit; null when it was applied, otherwise the error that stops the run.</summary>
    internal WorkbenchError? Apply(RuleEdit edit, int index)
    {
        editIndex = index;
        return edit switch
        {
            SetCondition e => SetCondition(e),
            ClearCondition e => ClearCondition(e),
            SetOutcome e => SetOutcome(e),
            AddRow e => AddRow(e),
            RemoveRow e => RemoveRow(e),
            MoveRow e => MoveRow(e),
            SetDefaultOutcome e => SetDefaultOutcome(e),
            SetCaseExpectation e => SetCaseExpectation(e),
            AddCase e => AddCase(e),
            RemoveCase e => RemoveCase(e),
            SetRowLabel e => SetRowLabel(e),
            _ => throw new ArgumentOutOfRangeException(nameof(edit), edit, "Unknown edit."),
        };
    }

    /// <summary>Bumps the version of the document and of each touched row, once, and writes the draft.</summary>
    internal (string? Text, WorkbenchError? Error) Finish()
    {
        foreach (var row in touched.Where(row => !added.Contains(row)))
        {
            if (Whole(row["version"]) is not { } current)
            {
                return (null, new WorkbenchError(BaseNotReadable, PathOf(row) + "/version", "A row that an edit touched has no readable version."));
            }

            row["version"] = current + 1;
        }

        root["version"] = version + 1;
        return (Format(root), null);
    }

    // ---- row edits ---------------------------------------------------------------------------

    private WorkbenchError? SetCondition(SetCondition edit)
    {
        if (FindRow(edit.RowKey) is not { } row)
        {
            return NoRow(edit.RowKey);
        }

        if (ConditionsOf(row) is not { } conditions)
        {
            return NotReadable(row, "conditions");
        }

        var node = TypedValueJson.Condition(edit.Condition);
        var at = ConditionIndexes(conditions, edit.Condition.FactName);
        if (at.Count == 0)
        {
            conditions.Add(node);
        }
        else
        {
            RemoveAt(conditions, at.Skip(1));
            conditions[at[0]] = node;
        }

        touched.Add(row);
        return null;
    }

    private WorkbenchError? ClearCondition(ClearCondition edit)
    {
        if (FindRow(edit.RowKey) is not { } row)
        {
            return NoRow(edit.RowKey);
        }

        if (ConditionsOf(row) is not { } conditions)
        {
            return NotReadable(row, "conditions");
        }

        var at = ConditionIndexes(conditions, edit.Fact);
        if (at.Count == 0)
        {
            return Error(ConditionNotFound, $"The row '{edit.RowKey}' has no condition on the fact '{edit.Fact}'.");
        }

        RemoveAt(conditions, at);
        touched.Add(row);
        return null;
    }

    private WorkbenchError? SetOutcome(SetOutcome edit)
    {
        if (FindRow(edit.RowKey) is not { } row)
        {
            return NoRow(edit.RowKey);
        }

        return SetField(row, edit.Field, edit.Value);
    }

    private WorkbenchError? SetDefaultOutcome(SetDefaultOutcome edit) =>
        body["defaultRow"] is JsonObject row
            ? SetField(row, edit.Field, edit.Value)
            : Error(NoDefaultRow, "The rule has no default row.");

    private WorkbenchError? SetField(JsonObject row, string field, DecisionValue? value)
    {
        if (row["outcome"] is not JsonObject outcome)
        {
            return NotReadable(row, "outcome");
        }

        if (value is null)
        {
            outcome.Remove(field);
        }
        else
        {
            outcome[field] = TypedValueJson.Plain(value);
        }

        touched.Add(row);
        return null;
    }

    private WorkbenchError? AddRow(AddRow edit)
    {
        if (!RowId().IsMatch(edit.RowKey))
        {
            return Error(RowKeyNotValid, $"'{edit.RowKey}' is not a valid row key: it must start with a letter, digit or underscore and use only letters, digits, '_', '.' and '-'.");
        }

        if (IsUsed(edit.RowKey))
        {
            return Error(RowKeyTaken, $"The row key '{edit.RowKey}' is already used by a row.");
        }

        var position = 0;
        if (edit.AfterRowKey is not null)
        {
            var after = IndexOfRow(edit.AfterRowKey);
            if (after < 0)
            {
                return NoRow(edit.AfterRowKey);
            }

            position = after + 1;
        }

        var row = new JsonObject
        {
            ["id"] = edit.RowKey,
            ["version"] = 1,
            ["legacyKey"] = edit.RowKey,
            ["conditions"] = new JsonArray([.. edit.Conditions.Select(condition => (JsonNode)TypedValueJson.Condition(condition))]),
            ["outcome"] = TypedValueJson.Outcome(edit.Outcome),
        };
        rows.Insert(position, row);
        added.Add(row);
        return null;
    }

    private WorkbenchError? RemoveRow(RemoveRow edit)
    {
        var index = IndexOfRow(edit.RowKey);
        if (index < 0)
        {
            return NoRow(edit.RowKey);
        }

        var id = Text(rows[index]!["id"])!;
        rows.RemoveAt(index);
        if (root["meta"] is JsonObject meta && meta["display"] is JsonObject display)
        {
            foreach (var locale in display.Select(entry => entry.Value).OfType<JsonObject>())
            {
                (locale["rows"] as JsonObject)?.Remove(id);
            }
        }

        return null;
    }

    private WorkbenchError? MoveRow(MoveRow edit)
    {
        var index = IndexOfRow(edit.RowKey);
        if (index < 0)
        {
            return NoRow(edit.RowKey);
        }

        if (string.Equals(edit.RowKey, edit.AfterRowKey, StringComparison.Ordinal))
        {
            return Error(MoveOntoItself, $"The row '{edit.RowKey}' cannot be moved after itself.");
        }

        var after = edit.AfterRowKey is null ? -1 : IndexOfRow(edit.AfterRowKey);
        if (edit.AfterRowKey is not null && after < 0)
        {
            return NoRow(edit.AfterRowKey);
        }

        var row = rows[index]!;
        rows.RemoveAt(index);
        rows.Insert((after > index ? after - 1 : after) + 1, row);
        return null;
    }

    // ---- test cases --------------------------------------------------------------------------

    private WorkbenchError? SetCaseExpectation(SetCaseExpectation edit)
    {
        if (FindCase(edit.CaseName) is not { } golden)
        {
            return NoCase(edit.CaseName);
        }

        var expect = new JsonObject();
        if (edit.ExpectNoDecision)
        {
            expect["noDecision"] = true;
            golden["expect"] = expect;
            return null;
        }

        var collect = Text(body["hitPolicy"]) == "collect";
        if (edit.Results is not null && !collect)
        {
            return Error(CaseNotValid, $"The test case '{edit.CaseName}' expects a list of results but the rule's hit policy is not collect.");
        }

        if (edit.RowKey is not null)
        {
            if (RowIdOf(edit.RowKey) is not { } id)
            {
                return NoRow(edit.RowKey);
            }

            expect["row"] = id;
        }

        if (collect)
        {
            var results = edit.Results ?? [edit.Outcome];
            expect["outcome"] = new JsonArray([.. results.Select(result => (JsonNode?)TypedValueJson.Outcome(result))]);
        }
        else
        {
            expect["outcome"] = TypedValueJson.Outcome(edit.Outcome);
        }

        golden["expect"] = expect;
        return null;
    }

    private WorkbenchError? AddCase(AddCase edit)
    {
        var proposed = edit.Case;
        if (FindCase(proposed.Name) is not null)
        {
            return Error(CaseNameTaken, $"A test case named '{proposed.Name}' already exists.");
        }

        var facts = new JsonObject();
        foreach (var (name, value) in proposed.Facts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            facts[name] = TypedValueJson.Tagged(value);
        }

        var expect = new JsonObject();
        if (proposed.ExpectNoDecision)
        {
            expect["noDecision"] = true;
        }
        else
        {
            var outcome = proposed.ExpectedOutcomeJson is null ? null : TryParse(proposed.ExpectedOutcomeJson);
            if (outcome is not (JsonObject or JsonArray))
            {
                return Error(CaseNotValid, $"The test case '{proposed.Name}' expects a decision but its expected outcome is not a JSON object or array.");
            }

            if (proposed.ExpectedRowId is not null)
            {
                expect["row"] = proposed.ExpectedRowId;
            }

            expect["outcome"] = outcome;
        }

        var golden = new JsonObject { ["name"] = proposed.Name, ["facts"] = facts, ["expect"] = expect };
        if (body["goldens"] is not JsonArray goldens)
        {
            goldens = [];
            body["goldens"] = goldens;
        }

        goldens.Add(golden);
        return null;
    }

    private WorkbenchError? RemoveCase(RemoveCase edit)
    {
        if (body["goldens"] is not JsonArray goldens || FindCase(edit.CaseName) is not { } golden)
        {
            return NoCase(edit.CaseName);
        }

        goldens.Remove(golden);
        return null;
    }

    // ---- labels ------------------------------------------------------------------------------

    private WorkbenchError? SetRowLabel(SetRowLabel edit)
    {
        if (FindRow(edit.RowKey) is not { } row)
        {
            return NoRow(edit.RowKey);
        }

        if (edit.Text is not null && string.IsNullOrWhiteSpace(edit.Text))
        {
            return Error(LabelNotValid, "A row label must not be empty or white space; pass null to clear it.");
        }

        var create = edit.Text is not null;
        var tag = edit.Locale == WorkbenchLocale.Es ? "es" : "en";
        var id = Text(row["id"])!;
        if (!TryChild(root, "meta", create, out var meta)
            || !TryChild(meta, "display", create, out var display)
            || !TryChild(display, tag, create, out var locale)
            || !TryChild(locale, "rows", create, out var labels))
        {
            return Error(BaseNotReadable, "The display labels of the document are not objects.");
        }

        if (edit.Text is null)
        {
            labels?.Remove(id);
        }
        else
        {
            labels![id] = edit.Text;
        }

        return null;
    }

    /// <summary>Reads an object member; creates it when asked and it is absent. False when the member exists and is not an object.</summary>
    private static bool TryChild(JsonObject? owner, string name, bool create, out JsonObject? child)
    {
        child = null;
        if (owner is null)
        {
            return true;
        }

        switch (owner[name])
        {
            case JsonObject existing:
                child = existing;
                return true;
            case null:
                if (create)
                {
                    child = [];
                    owner[name] = child;
                }

                return true;
            default:
                return false;
        }
    }

    // ---- lookups -----------------------------------------------------------------------------

    private int IndexOfRow(string key)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index] is JsonObject row && Text(row["legacyKey"]) == key && Text(row["id"]) is not null)
            {
                return index;
            }
        }

        return -1;
    }

    private JsonObject? FindRow(string key) => IndexOfRow(key) is var index and >= 0 ? (JsonObject)rows[index]! : null;

    private string? RowIdOf(string key)
    {
        var row = FindRow(key) ?? (body["defaultRow"] is JsonObject fallback && Text(fallback["legacyKey"]) == key ? fallback : null);
        return row is null ? null : Text(row["id"]);
    }

    private bool IsUsed(string key) =>
        rows.Append(body["defaultRow"]).OfType<JsonObject>()
            .Any(row => Text(row["legacyKey"]) == key || Text(row["id"]) == key);

    private JsonObject? FindCase(string name) =>
        (body["goldens"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(golden => Text(golden["name"]) == name);

    private static JsonArray? ConditionsOf(JsonObject row)
    {
        if (row["conditions"] is JsonArray existing)
        {
            return existing;
        }

        if (row["conditions"] is not null)
        {
            return null;
        }

        var created = new JsonArray();
        row["conditions"] = created;
        return created;
    }

    private static List<int> ConditionIndexes(JsonArray conditions, string fact) =>
        [.. conditions.Select((node, index) => (node, index))
            .Where(entry => entry.node is JsonObject condition && Text(condition["fact"]) == fact)
            .Select(entry => entry.index)];

    private static void RemoveAt(JsonArray array, IEnumerable<int> indexes)
    {
        foreach (var index in indexes.OrderByDescending(index => index))
        {
            array.RemoveAt(index);
        }
    }

    private string PathOf(JsonObject row)
    {
        var index = rows.IndexOf(row);
        return index >= 0 ? "/body/rows/" + index.ToString(CultureInfo.InvariantCulture) : "/body/defaultRow";
    }

    // ---- errors ------------------------------------------------------------------------------

    private WorkbenchError Error(string code, string message) =>
        new(code, "/edits/" + editIndex.ToString(CultureInfo.InvariantCulture), message);

    private WorkbenchError NoRow(string key) => Error(RowNotFound, $"The rule has no row with the key '{key}'.");

    private WorkbenchError NoCase(string name) => Error(CaseNotFound, $"The rule has no test case named '{name}'.");

    private WorkbenchError NotReadable(JsonObject row, string member) =>
        new(BaseNotReadable, PathOf(row) + "/" + member, $"The row's {member} is not readable.");

    [GeneratedRegex(@"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RowId();
}
