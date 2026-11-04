using Android.Content;
using Android.Provider;
using VrView.Core;
using Uri = Android.Net.Uri;

namespace VrView;

/// <summary>Lists videos inside a folder picked with the Storage Access Framework.</summary>
internal static class FolderScanner
{
    private static readonly string[] Columns =
    {
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnSize,
    };

    /// <summary>Returns the videos sorted in natural order (subfolder entries are prefixed with their path).</summary>
    public static List<MediaItem> Scan(ContentResolver resolver, Uri treeUri, bool recursive, CancellationToken token)
    {
        var result = new List<MediaItem>();
        string rootId = DocumentsContract.GetTreeDocumentId(treeUri)!;
        ScanFolder(resolver, treeUri, rootId, "", recursive, result, token, depth: 0);
        result.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
        return result;
    }

    private static void ScanFolder(ContentResolver resolver, Uri treeUri, string documentId, string prefix,
        bool recursive, List<MediaItem> result, CancellationToken token, int depth)
    {
        token.ThrowIfCancellationRequested();
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId)!;
        using var cursor = resolver.Query(children, Columns, null, null, null);
        if (cursor is null) return;
        while (cursor.MoveToNext())
        {
            string id = cursor.GetString(0) ?? "";
            string name = cursor.GetString(1) ?? "";
            string mime = cursor.GetString(2) ?? "";
            long size = cursor.IsNull(3) ? 0 : cursor.GetLong(3);
            if (mime == DocumentsContract.Document.MimeTypeDir)
            {
                if (recursive && depth < 8 && !name.StartsWith('.'))
                    ScanFolder(resolver, treeUri, id, prefix + name + "/", recursive, result, token, depth + 1);
                continue;
            }
            if (name.StartsWith('.') || !MediaUtil.IsVideo(name, mime)) continue;
            var uri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, id)!;
            result.Add(new MediaItem(uri.ToString()!, prefix + name, size));
        }
    }

    /// <summary>
    /// Files next to a video (same folder) whose names start with the video's base name and end with
    /// one of <paramref name="extensions"/>, e.g. "clip.srt" or "clip.es.srt" for "clip.mp4".
    /// </summary>
    public static List<(string Name, Uri Uri)> FindCompanions(ContentResolver resolver, Uri treeUri, string relativeName, params string[] extensions)
    {
        var result = new List<(string, Uri)>();
        try
        {
            // Walk down from the picked folder by display name.
            string id = DocumentsContract.GetTreeDocumentId(treeUri)!;
            var parts = relativeName.Split('/');
            for (int i = 0; i < parts.Length - 1 && id is not null; i++)
                id = Children(resolver, treeUri, id).FirstOrDefault(c => c.Name == parts[i] && c.IsDir).Id!;
            if (id is null) return result;
            string stem = Path.GetFileNameWithoutExtension(parts[^1]);
            foreach (var c in Children(resolver, treeUri, id))
                if (!c.IsDir && c.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase) &&
                    extensions.Any(e => c.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    result.Add((c.Name, DocumentsContract.BuildDocumentUriUsingTree(treeUri, c.Id)!));
        }
        catch (Java.Lang.Exception) { }
        return result;
    }

    private static List<(string Id, string Name, bool IsDir)> Children(ContentResolver resolver, Uri treeUri, string documentId)
    {
        var list = new List<(string, string, bool)>();
        var children = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId)!;
        using var cursor = resolver.Query(children, Columns, null, null, null);
        if (cursor is null) return list;
        while (cursor.MoveToNext())
            list.Add((cursor.GetString(0) ?? "", cursor.GetString(1) ?? "", cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir));
        return list;
    }

    /// <summary>Display name of the picked folder (falls back to the last path segment).</summary>
    public static string FolderName(ContentResolver resolver, Uri treeUri)
    {
        try
        {
            var doc = DocumentsContract.BuildDocumentUriUsingTree(treeUri, DocumentsContract.GetTreeDocumentId(treeUri))!;
            using var c = resolver.Query(doc, new[] { DocumentsContract.Document.ColumnDisplayName }, null, null, null);
            if (c is not null && c.MoveToFirst()) return c.GetString(0) ?? "";
        }
        catch (Java.Lang.Exception) { }
        catch (System.Exception) { }
        return treeUri.LastPathSegment ?? "";
    }

    /// <summary>Best-effort display name of a single content/file URI.</summary>
    public static string DisplayName(ContentResolver resolver, Uri uri)
    {
        try
        {
            if (uri.Scheme == "content")
            {
                using var c = resolver.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
                if (c is not null && c.MoveToFirst() && !c.IsNull(0)) return c.GetString(0)!;
            }
        }
        catch (Java.Lang.Exception) { }
        return uri.LastPathSegment ?? "video";
    }
}
