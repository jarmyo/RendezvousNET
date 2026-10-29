namespace VrCut.Services;

public sealed record RemoteEntry(string Name, string FullPath, bool IsFolder, long Size, DateTime Modified);

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
        // stat with a glob gives type, size, mtime and name in one round-trip; an empty folder just returns nothing.
        string cmd = $"stat -c '%F|%s|%Y|%n' {Quote(folder)}/*";
        string text = await ProcessRunner.RunAsync("adb", ["shell", cmd], allowFailure: true);

        var result = new List<RemoteEntry>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.TrimEnd('\r').Split('|', 4);
            if (parts.Length != 4 || !long.TryParse(parts[1], out var size) || !long.TryParse(parts[2], out var mtime))
                continue;
            string path = parts[3];
            var modified = DateTimeOffset.FromUnixTimeSeconds(mtime).LocalDateTime;
            result.Add(new RemoteEntry(path[(path.LastIndexOf('/') + 1)..], path, parts[0] == "directory", size, modified));
        }
        return result;
    }

    public static async Task RenameAsync(string path, string newName)
    {
        string target = path[..(path.LastIndexOf('/') + 1)] + newName;
        string exists = await ProcessRunner.RunAsync("adb", ["shell", $"[ -e {Quote(target)} ] && echo yes"], allowFailure: true);
        if (exists.Trim() == "yes")
            throw new IOException($"Ya existe un archivo llamado '{newName}' en el teléfono.");
        await ProcessRunner.RunAsync("adb", ["shell", $"mv {Quote(path)} {Quote(target)}"]);
        await ScanAsync(path);
        await ScanAsync(target);
    }

    public static async Task DeleteAsync(string path)
    {
        await ProcessRunner.RunAsync("adb", ["shell", $"rm -f {Quote(path)}"]);
        await ScanAsync(path);
    }

    public static Task PullAsync(string remote, string local, CancellationToken ct) =>
        ProcessRunner.RunAsync("adb", ["pull", remote, local], ct: ct);

    public static async Task PushAsync(string local, string remote, CancellationToken ct)
    {
        await ProcessRunner.RunAsync("adb", ["push", local, remote], ct: ct);
        await ScanAsync(remote);
    }

    // Keeps the phone's media index (used by many VR players) in sync with added, renamed or removed files.
    private static Task ScanAsync(string remote) =>
        ProcessRunner.RunAsync("adb",
            ["shell", $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d {Quote("file://" + remote)}"],
            allowFailure: true);

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
}
