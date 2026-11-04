namespace VrView.Core;

/// <summary>Where the format of a video comes from, in order of precedence.</summary>
public enum FormatSource
{
    /// <summary>Chosen in the editor for this video only.</summary>
    Video,
    /// <summary>This video uses a named profile.</summary>
    VideoProfile,
    /// <summary>The folder uses a named profile (videos without tags in the name).</summary>
    FolderProfile,
    /// <summary>The folder has its own format (videos without tags in the name).</summary>
    Folder,
    /// <summary>Spherical metadata written by the camera or editor into the file.</summary>
    Metadata,
    /// <summary>Tags in the file name such as "_180_LR".</summary>
    NameTags,
    /// <summary>Guessed from the frame's aspect ratio.</summary>
    Auto,
}

/// <summary>Where the format editor saves changes.</summary>
public enum FormatScope { Video, Folder, Profile }

/// <summary>One line of the "¿De dónde sale este formato?" list.</summary>
public readonly record struct FormatCandidate(FormatSource Source, string Text, bool Available, bool Used);

/// <summary>Everything that can decide the format of a video.</summary>
public sealed record FormatInputs(
    VideoFormat? VideoOverride,
    string? VideoProfile,
    FolderEntry? Folder,
    IReadOnlyDictionary<string, VideoFormat> Profiles,
    string FileName,
    int Width,
    int Height,
    VideoFormat? Metadata);

/// <summary>The format in use, why, and the alternatives.</summary>
public sealed record FormatChoice(VideoFormat Format, FormatSource Source, string? Profile, IReadOnlyList<FormatCandidate> Candidates)
{
    /// <summary>Short note for the player's status line, e.g. "carpeta" or "perfil Cámara 200°".</summary>
    public string Note => Source switch
    {
        FormatSource.Video => "este video",
        FormatSource.VideoProfile or FormatSource.FolderProfile => $"perfil {Profile}",
        FormatSource.Folder => "carpeta",
        FormatSource.Metadata => "metadatos",
        FormatSource.NameTags => "nombre",
        _ => "auto",
    };

    /// <summary>Where the editor saves by default: where the format comes from.</summary>
    public FormatScope DefaultScope => Source switch
    {
        FormatSource.VideoProfile or FormatSource.FolderProfile => FormatScope.Profile,
        FormatSource.Folder => FormatScope.Folder,
        _ => FormatScope.Video,
    };
}

/// <summary>
/// Decides the format of a video: this video's choice → its profile → the folder's profile or
/// format (only for names without format tags) → the file's spherical metadata → the name tags →
/// the frame's aspect ratio.
/// </summary>
public static class FormatResolver
{
    public static FormatChoice Resolve(FormatInputs i)
    {
        bool tags = FormatDetector.HasTags(i.FileName);
        var detected = FormatDetector.Detect(i.FileName, i.Width, i.Height);
        VideoFormat? videoProfile = i.VideoProfile is { } vp && i.Profiles.TryGetValue(vp, out var vpf) ? vpf : null;
        string? folderProfile = i.Folder?.Profile is { } fp && i.Profiles.ContainsKey(fp) ? fp : null;
        VideoFormat? folder = folderProfile is not null ? i.Profiles[folderProfile] : i.Folder?.Format;

        FormatSource source;
        VideoFormat format;
        string? profile = null;
        if (i.VideoOverride is { } v) (format, source) = (v, FormatSource.Video);
        else if (videoProfile is { } vpFormat) (format, source, profile) = (vpFormat, FormatSource.VideoProfile, i.VideoProfile);
        else if (folder is { } ff && !tags)
            (format, source, profile) = (ff, folderProfile is null ? FormatSource.Folder : FormatSource.FolderProfile, folderProfile);
        else if (i.Metadata is { } m) (format, source) = (m, FormatSource.Metadata);
        else (format, source) = (detected, tags ? FormatSource.NameTags : FormatSource.Auto);

        var list = new List<FormatCandidate>
        {
            new(FormatSource.Video,
                i.VideoOverride is { } vo ? $"Este video: {vo.Label}"
                : videoProfile is not null ? $"Este video: perfil «{i.VideoProfile}»"
                : "Este video: sin elegir",
                i.VideoOverride is not null || videoProfile is not null,
                source is FormatSource.Video or FormatSource.VideoProfile),
            new(folderProfile is null ? FormatSource.Folder : FormatSource.FolderProfile,
                folder is null ? "Carpeta: sin elegir"
                : (folderProfile is not null ? $"Carpeta: perfil «{folderProfile}»" : $"Carpeta: {folder.Value.Label}")
                  + (tags ? " (no se aplica: el nombre trae etiquetas)" : ""),
                folder is not null && !tags,
                source is FormatSource.Folder or FormatSource.FolderProfile),
            new(FormatSource.Metadata,
                i.Metadata is { } md ? $"Metadatos del archivo: {md.Label}" : "Metadatos del archivo: no tiene",
                i.Metadata is not null, source == FormatSource.Metadata),
            new(FormatSource.NameTags,
                tags ? $"Etiquetas del nombre: {detected.ShortLabel}" : "Etiquetas del nombre: ninguna",
                tags, source == FormatSource.NameTags),
            new(FormatSource.Auto, $"Automático por proporción ({AspectText(i.Width, i.Height)})",
                !tags, source == FormatSource.Auto),
        };
        return new FormatChoice(format, source, profile, list);
    }

    /// <summary>"2:1", "16:9", "1:1"… or "?" when unknown.</summary>
    public static string AspectText(int width, int height)
    {
        if (width <= 0 || height <= 0) return "?";
        float a = (float)width / height;
        foreach (var (w, h) in new[] { (2, 1), (1, 1), (16, 9), (4, 3), (4, 1), (1, 2), (32, 9), (21, 9) })
            if (MathF.Abs(a - (float)w / h) < 0.02f * w / h) return $"{w}:{h}";
        return a.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ":1";
    }
}
