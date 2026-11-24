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
    private readonly object gate = new();

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
    public IReadOnlyList<string> ListDrafts(DocumentKind kind)
    {
        lock (gate)
        {
            RequirePlainChain("drafts", ApprovedFolder(kind));
            return Ids(Path.Combine(root, "drafts", ApprovedFolder(kind)), ".json", exclude: ".draft.json", readRoot: Path.Combine(root, "drafts"));
        }
    }

    /// <inheritdoc />
    public StoredDraft? ReadDraft(DocumentKind kind, string id)
    {
        StoreIds.Require(id, nameof(id));
        lock (gate)
        {
            RequirePlainChain("drafts", ApprovedFolder(kind));
            var area = Path.Combine(root, "drafts");
            var folder = Path.Combine(area, ApprovedFolder(kind));
            var text = ReadInside(Path.Combine(folder, id + ".json"), area);
            return text is null ? null : DraftEnvelope.Read(text, ReadInside(Path.Combine(folder, id + ".draft.json"), area));
        }
    }

    /// <inheritdoc />
    public void WriteDraft(DocumentKind kind, string id, StoredDraft draft)
    {
        StoreIds.Require(id, nameof(id));
        ArgumentNullException.ThrowIfNull(draft);
        lock (gate)
        {
            string[] folder = ["drafts", ApprovedFolder(kind)];
            WriteInside(folder, id + ".draft.json", DraftEnvelope.Write(kind, id, draft), overwrite: true);
            WriteInside(folder, id + ".json", draft.Text, overwrite: true);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListProposals()
    {
        lock (gate)
        {
            RequirePlainChain("proposals");
            return Ids(Path.Combine(root, "proposals"), ".json", exclude: null, readRoot: Path.Combine(root, "proposals"));
        }
    }

    /// <inheritdoc />
    public string? ReadProposal(string name)
    {
        StoreIds.Require(name, nameof(name));
        lock (gate)
        {
            RequirePlainChain("proposals");
            return ReadInside(Path.Combine(root, "proposals", name + ".json"), Path.Combine(root, "proposals"));
        }
    }

    /// <inheritdoc />
    public string WriteProposal(string name, string json)
    {
        StoreIds.Require(name, nameof(name));
        ArgumentNullException.ThrowIfNull(json);
        lock (gate)
        {
            WriteInside(["proposals"], name + ".json", json, overwrite: false);
            return "proposals/" + name + ".json";
        }
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

    private static bool IsLink(string path)
    {
        var info = new FileInfo(path);
        return info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) || (new DirectoryInfo(path).Exists && new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    // Every existing folder on the way down from the root to the area (and its subfolder) must be a plain folder, not a link.
    // A folder that does not exist yet is fine: reading it finds nothing.
    private void RequirePlainChain(params string[] names)
    {
        var current = root;
        foreach (var name in names)
        {
            current = Path.Combine(current, name);
            if (!Directory.Exists(current) && !IsLink(current))
            {
                return;
            }

            RequirePlainFolder(current);
        }
    }

    private static void RequirePlainFolder(string folder)
    {
        if (IsLink(folder) || !Directory.Exists(folder))
        {
            throw new AuthoringStoreException("The folder is a link or not a folder; the tools do not use links inside the workspace.");
        }

        if (!string.Equals(Real(folder), folder, StringComparison.Ordinal))
        {
            throw new AuthoringStoreException("The folder does not resolve to its own place in the workspace.");
        }
    }

    // The one place that writes. Walking down from the root, each folder of the write area (created when missing) must be a plain folder at
    // exactly its own place, so the area resolves to root/drafts[/kind] or root/proposals. The target must not be a link; the temporary
    // file has a name nobody can predict and is created new (an existing file or link of that name makes the creation fail); the move
    // replaces the target itself.
    private void WriteInside(string[] folderNames, string fileName, string text, bool overwrite)
    {
        var folder = root;
        foreach (var name in folderNames)
        {
            folder = Path.Combine(folder, name);
            if (!IsLink(folder) && !Directory.Exists(folder))
            {
                if (File.Exists(folder))
                {
                    throw new AuthoringStoreException("A file stands where a folder of the workspace should be.");
                }

                _ = Directory.CreateDirectory(folder);
            }

            RequirePlainFolder(folder);
        }

        var target = Path.Combine(folder, fileName);
        if (IsLink(target))
        {
            throw new AuthoringStoreException("The file is a link; the tools do not write through links.");
        }

        if (Directory.Exists(target))
        {
            throw new AuthoringStoreException("A folder stands where a file of the workspace should be.");
        }

        if (!overwrite && File.Exists(target))
        {
            throw new AuthoringStoreException("A proposal with that name exists already.");
        }

        var temporary = Path.Combine(folder, "." + Path.GetRandomFileName() + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }

            File.Move(temporary, target, overwrite);
        }
        catch (IOException error)
        {
            throw new AuthoringStoreException("The file could not be written: " + error.GetType().Name + ".", error);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
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
