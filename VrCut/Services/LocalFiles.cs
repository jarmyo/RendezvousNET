namespace VrCut.Services;

public static class LocalFiles
{
    /// <summary>True when the file goes to the Trash / Recycle Bin and can be recovered.</summary>
    public static bool UsesTrash => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    public static async Task MoveToTrashAsync(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Finder keeps "Put Back" working; macOS may ask once to allow VrCut to control Finder.
            string escaped = path.Replace("\\", "\\\\").Replace("\"", "\\\"");
            await ProcessRunner.RunAsync("osascript",
                ["-e", $"tell application \"Finder\" to delete POSIX file \"{escaped}\""]);
        }
        else
        {
            File.Delete(path);
        }
    }

    public static void Rename(string path, string newName)
    {
        string target = Path.Combine(Path.GetDirectoryName(path)!, newName);
        bool caseOnlyChange = string.Equals(path, target, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyChange && (File.Exists(target) || Directory.Exists(target)))
            throw new IOException($"Ya existe un archivo llamado '{newName}'.");

        if (Directory.Exists(path))
            Directory.Move(path, target);
        else
            File.Move(path, target);
    }
}
