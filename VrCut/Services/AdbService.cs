namespace VrCut.Services;

public sealed record RemoteEntry(string Name, string FullPath, bool IsFolder, long Size);

public static class AdbService
{
    public static bool IsAvailable => ProcessRunner.Find("adb") is not null;

    /// <summary>Returns the serial of the first authorized device, or null.</summary>
    public static async Task<string?> GetDeviceAsync()
    {
        string text = await ProcessRunner.RunAsync("adb", ["devices"]);
        foreach (var line in text.Split('\n').Skip(1))
        {
            var parts = line.Split('\t', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1] == "device")
                return parts[0];
        }
        return null;
    }

    public static async Task<List<RemoteEntry>> ListAsync(string folder)
    {
        folder = folder.TrimEnd('/');
        // stat with a glob gives type, size and name in one round-trip; an empty folder just returns nothing.
        string cmd = $"stat -c '%F|%s|%n' {Quote(folder)}/*";
        string text = await ProcessRunner.RunAsync("adb", ["shell", cmd], allowFailure: true);

        var result = new List<RemoteEntry>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('|', 3);
            if (parts.Length != 3 || !long.TryParse(parts[1], out var size))
                continue;
            string path = parts[2];
            result.Add(new RemoteEntry(path[(path.LastIndexOf('/') + 1)..], path, parts[0] == "directory", size));
        }
        return result;
    }

    public static Task PullAsync(string remote, string local, CancellationToken ct) =>
        ProcessRunner.RunAsync("adb", ["pull", remote, local], ct: ct);

    public static async Task PushAsync(string local, string remote, CancellationToken ct)
    {
        await ProcessRunner.RunAsync("adb", ["push", local, remote], ct: ct);
        // Let the phone's media index (and VR players that rely on it) see the new file.
        await ProcessRunner.RunAsync("adb",
            ["shell", $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d {Quote("file://" + remote)}"],
            allowFailure: true, ct: ct);
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
