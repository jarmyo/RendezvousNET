namespace VrView.Core;

/// <summary>
/// How the two apps talk to each other: VrBrowser hands web videos to VrView's player, and reads the
/// viewer setup (lenses, field of view, controls…) that is configured once, in VrView.
/// </summary>
public static class AppLinks
{
    public const string ViewerPackage = "net.rendezvous.vrview";
    public const string BrowserPackage = "net.rendezvous.vrbrowser";
    public const string PlayerActivity = ViewerPackage + ".PlayerActivity";

    /// <summary>VrView's read-only provider of shared settings, open only to apps signed with the same key.</summary>
    public const string SettingsAuthority = ViewerPackage + ".settings";
    public const string SettingsPermission = ViewerPackage + ".permission.SHARED_SETTINGS";

    // Player intent extras: one array entry per video.
    public const string ExtraLocations = "vrview.locations";
    public const string ExtraNames = "vrview.names";
    public const string ExtraSizes = "vrview.sizes";
    public const string ExtraStart = "vrview.start";
    public const string ExtraPages = "vrview.pages";
    public const string ExtraKeys = "vrview.keys";
    /// <summary>HTTP headers of each video (cookies, agent, referrer), see <see cref="EncodeHeaders"/>.</summary>
    public const string ExtraHeaders = "vrview.headers";

    /// <summary>Preferences that describe the viewer and the controls: VrBrowser always takes VrView's.</summary>
    public static readonly IReadOnlyList<string> ViewKeys =
    [
        "stereo", "fov", "ipd", "lens", "gaze", "dwell", "controlsSize", "controlsOpacity", "menuShift", "highQuality",
        "denseMesh", "edgeButtons", "edgeOn", "edgeFollow", "lookDown", "tracking", "autoRecenter", "subtitleSize",
        "showFps", "viewerUrl",
    ];

    /// <summary>Web preferences VrView kept before the browser was its own app: VrBrowser takes them once.</summary>
    public static readonly IReadOnlyList<string> WebKeys = ["webBookmarks", "lastWebUrl"];
    public const string WebFormatPrefix = "webFormat.";

    public static bool IsShared(string key) => ViewKeys.Contains(key) || IsWeb(key);

    public static bool IsWeb(string key) => WebKeys.Contains(key) || key.StartsWith(WebFormatPrefix, StringComparison.Ordinal);

    /// <summary>Headers as "Name: value" lines (line breaks inside a value are dropped).</summary>
    public static string EncodeHeaders(IEnumerable<KeyValuePair<string, string>> headers) =>
        string.Join("\n", headers
            .Where(h => h.Key.Length > 0 && !h.Key.Contains(':'))
            .Select(h => $"{Clean(h.Key)}: {Clean(h.Value)}"));

    public static Dictionary<string, string> DecodeHeaders(string? text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? "").Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return headers;
    }

    private static string Clean(string s) => s.Replace("\r", "").Replace("\n", " ").Trim();
}
