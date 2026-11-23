using System.Text;

namespace Synapbiz.Brain.Authoring;

/// <summary>
/// A workspace in a folder. Layout: <c>catalog.json</c>; approved documents in <c>flows/&lt;id&gt;.json</c> and <c>rules/&lt;id&gt;.json</c> (read only);
/// drafts in <c>drafts/flows/&lt;id&gt;.json</c> with <c>&lt;id&gt;.draft.json</c> beside each; proposals in <c>proposals/&lt;name&gt;.json</c>. Every path is built from a
/// validated id, never from free text. A path that, once its links are followed, is not inside the folder (for a read) or inside
/// <c>drafts</c> or <c>proposals</c> (for a write) is refused with <see cref="AuthoringStoreException"/>.
/// </summary>
public sealed class FileAuthoringStore : IAuthoringStore
{
    private readonly string root;

    /// <summary>Opens the workspace at a folder; the folder must exist.</summary>
    /// <param name="folder">The folder.</param>
    public FileAuthoringStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException("The workspace folder does not exist.");
        }

        root = Real(full);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListApproved(DocumentKind kind) => Ids(Path.Combine(root, ApprovedFolder(kind)), ".json", exclude: null, readRoot: root);

    /// <inheritdoc />
    public string? ReadApproved(DocumentKind kind, string id)
    {
        StoreIds.Require(id, nameof(id));
        return ReadInside(Path.Combine(root, ApprovedFolder(kind), id + ".json"), root);
    }

    /// <inheritdoc />
    public string? ReadCatalog() => ReadInside(Path.Combine(root, "catalog.json"), root);

    /// <inheritdoc />
    public IReadOnlyList<string> ListDrafts(DocumentKind kind) =>
        Ids(Path.Combine(root, "drafts", ApprovedFolder(kind)), ".json", exclude: ".draft.json", readRoot: Path.Combine(root, "drafts"));

    /// <inheritdoc />
    public StoredDraft? ReadDraft(DocumentKind kind, string id)
    {
        StoreIds.Require(id, nameof(id));
        var area = Path.Combine(root, "drafts");
        var folder = Path.Combine(area, ApprovedFolder(kind));
        var text = ReadInside(Path.Combine(folder, id + ".json"), area);
        return text is null ? null : DraftEnvelope.Read(text, ReadInside(Path.Combine(folder, id + ".draft.json"), area));
    }

    /// <inheritdoc />
    public void WriteDraft(DocumentKind kind, string id, StoredDraft draft)
    {
        StoreIds.Require(id, nameof(id));
        ArgumentNullException.ThrowIfNull(draft);
        var folder = Path.Combine(root, "drafts", ApprovedFolder(kind));
        EnsureFolder(folder, "drafts");
        WriteInside(Path.Combine(folder, id + ".draft.json"), "drafts", DraftEnvelope.Write(kind, id, draft), overwrite: true);
        WriteInside(Path.Combine(folder, id + ".json"), "drafts", draft.Text, overwrite: true);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListProposals() => Ids(Path.Combine(root, "proposals"), ".json", exclude: null, readRoot: Path.Combine(root, "proposals"));

    /// <inheritdoc />
    public string? ReadProposal(string name)
    {
        StoreIds.Require(name, nameof(name));
        return ReadInside(Path.Combine(root, "proposals", name + ".json"), Path.Combine(root, "proposals"));
    }

    /// <inheritdoc />
    public string WriteProposal(string name, string json)
    {
        StoreIds.Require(name, nameof(name));
        ArgumentNullException.ThrowIfNull(json);
        var folder = Path.Combine(root, "proposals");
        EnsureFolder(folder, "proposals");
        WriteInside(Path.Combine(folder, name + ".json"), "proposals", json, overwrite: false);
        return "proposals/" + name + ".json";
    }

    private static string ApprovedFolder(DocumentKind kind) => kind == DocumentKind.Flow ? "flows" : "rules";

    // The path with every link followed, for a folder or file that may not exist yet: the deepest existing ancestor is resolved.
    private static string Real(string path)
    {
        var full = Path.GetFullPath(path);
        var pending = new Stack<string>();
        var current = full;
        while (!File.Exists(current) && !Directory.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (parent is null)
            {
                return full;
            }

            pending.Push(Path.GetFileName(current));
            current = parent;
        }

        string resolved;
        if (Directory.Exists(current))
        {
            resolved = ResolveDirectory(current);
        }
        else
        {
            var file = Path.Combine(ResolveDirectory(Path.GetDirectoryName(current)!), Path.GetFileName(current));
            var target = new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true);
            resolved = target is null ? file : Real(target.FullName);
        }

        return pending.Aggregate(resolved, Path.Combine);
    }

    private static string ResolveDirectory(string directory)
    {
        // Walk the components and resolve any that is a link.
        var parts = directory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = Path.DirectorySeparatorChar.ToString();
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            var target = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                current = target.FullName;
            }
        }

        return current;
    }

    private static bool IsInside(string candidate, string folder)
    {
        var prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static string? ReadInside(string path, string allowedFolder)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (!IsInside(Real(path), Real(allowedFolder)))
        {
            throw new AuthoringStoreException("The file is a link that leaves the workspace.");
        }

        return File.ReadAllText(path, Encoding.UTF8);
    }

    private void EnsureFolder(string folder, string area)
    {
        var areaPath = Path.Combine(root, area);
        if (!IsInside(Real(areaPath) + Path.DirectorySeparatorChar, root))
        {
            throw new AuthoringStoreException("The area is a link that leaves the workspace.");
        }

        _ = Directory.CreateDirectory(folder);
        if (!IsInside(Real(folder) + Path.DirectorySeparatorChar, Real(areaPath)))
        {
            throw new AuthoringStoreException("The folder is a link that leaves its area.");
        }
    }

    private void WriteInside(string path, string area, string text, bool overwrite)
    {
        var areaPath = Real(Path.Combine(root, area));
        if (!IsInside(Real(path), areaPath))
        {
            throw new AuthoringStoreException("The path leaves the area the tools may write.");
        }

        if (File.Exists(path) && new FileInfo(path).LinkTarget is not null)
        {
            throw new AuthoringStoreException("The file is a link; the tools do not write through links.");
        }

        if (!overwrite && File.Exists(path))
        {
            throw new AuthoringStoreException("A proposal with that name exists already.");
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite);
    }

    private static List<string> Ids(string folder, string suffix, string? exclude, string readRoot)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        if (!IsInside(Real(folder) + Path.DirectorySeparatorChar, Real(readRoot)) && !string.Equals(Real(folder), Real(readRoot), StringComparison.Ordinal))
        {
            throw new AuthoringStoreException("The folder is a link that leaves the workspace.");
        }

        return [.. Directory.EnumerateFiles(folder, "*" + suffix)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => exclude is null || !name.EndsWith(exclude, StringComparison.Ordinal))
            .Select(name => name[..^suffix.Length])
            .Where(StoreIds.IsValid)
            .Order(StringComparer.Ordinal)];
    }
}
