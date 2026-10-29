using System.Text;
using Android.Content;
using Android.Provider;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>
/// Reads and writes <see cref="FolderSettings.FileName"/> in the root of a picked folder through the
/// Storage Access Framework. One shared instance per folder, so the list and the player see the
/// same data; changes are written by <see cref="Flush"/>.
/// </summary>
internal sealed class FolderSettingsFile
{
    private static readonly Dictionary<string, FolderSettingsFile> Instances = new();

    private readonly object _lock = new();
    private readonly ContentResolver _resolver;
    private readonly Uri _tree;
    private readonly string _rootId;
    private Uri? _document;
    private bool _dirty;

    public FolderSettings Data { get; private set; } = new();

    /// <summary>False when the folder was granted read-only (picked with an older version of the app).</summary>
    public bool CanWrite { get; }

    private FolderSettingsFile(ContentResolver resolver, string treeUri)
    {
        _resolver = resolver;
        _tree = Uri.Parse(treeUri)!;
        _rootId = DocumentsContract.GetTreeDocumentId(_tree)!;
        CanWrite = resolver.PersistedUriPermissions.Any(p => p.IsWritePermission && p.Uri?.ToString() == treeUri);
        Load();
    }

    public static FolderSettingsFile For(ContentResolver resolver, string treeUri)
    {
        lock (Instances)
        {
            if (!Instances.TryGetValue(treeUri, out var file))
                Instances[treeUri] = file = new FolderSettingsFile(resolver, treeUri);
            return file;
        }
    }

    /// <summary>Writes pending changes and reads the file again (it may have been edited elsewhere).</summary>
    public static FolderSettingsFile Reload(ContentResolver resolver, string treeUri)
    {
        lock (Instances)
        {
            if (Instances.Remove(treeUri, out var old)) old.Flush();
        }
        return For(resolver, treeUri);
    }

    public static void FlushAll()
    {
        List<FolderSettingsFile> all;
        lock (Instances) all = Instances.Values.ToList();
        foreach (var f in all) f.Flush();
    }

    public void MarkDirty() => _dirty = true;

    public void Flush()
    {
        lock (_lock)
        {
            if (!_dirty || !CanWrite) return;
            try
            {
                _document ??= DocumentsContract.CreateDocument(_resolver,
                    DocumentsContract.BuildDocumentUriUsingTree(_tree, _rootId)!, "application/json", FolderSettings.FileName);
                if (_document is null) return;
                byte[] bytes = new UTF8Encoding(false).GetBytes(Data.ToJson());
                using var stream = OpenForWrite(_document);
                stream.Write(bytes, 0, bytes.Length);
                _dirty = false;
            }
            catch (Exception ex) when (ex is Java.Lang.Exception or IOException or InvalidOperationException)
            {
                // InvalidOperationException: the data changed while serializing; the next flush retries.
                Android.Util.Log.Warn("VrView", $"Could not write {FolderSettings.FileName}: {ex.Message}");
            }
        }
    }

    private Stream OpenForWrite(Uri document)
    {
        try
        {
            return _resolver.OpenOutputStream(document, "wt")!;
        }
        catch (Java.Lang.IllegalArgumentException)
        {
            // Providers without truncate mode.
            return _resolver.OpenOutputStream(document, "w")!;
        }
    }

    private void Load()
    {
        try
        {
            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(_tree, _rootId)!;
            using var cursor = _resolver.Query(children,
                new[] { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName },
                null, null, null);
            while (cursor is not null && cursor.MoveToNext())
            {
                if (cursor.GetString(1) != FolderSettings.FileName) continue;
                _document = DocumentsContract.BuildDocumentUriUsingTree(_tree, cursor.GetString(0))!;
                break;
            }
            if (_document is null) return;
            using var input = _resolver.OpenInputStream(_document)!;
            using var reader = new StreamReader(input, Encoding.UTF8);
            Data = FolderSettings.Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IOException)
        {
            Android.Util.Log.Warn("VrView", $"Could not read {FolderSettings.FileName}: {ex.Message}");
        }
    }
}
