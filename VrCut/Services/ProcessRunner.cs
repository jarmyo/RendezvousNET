using System.Diagnostics;

namespace VrCut.Services;

public static class ProcessRunner
{
    // GUI apps on macOS do not inherit the shell PATH, so Homebrew folders are probed explicitly.
    private static readonly string[] ExtraDirs =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Android/sdk/platform-tools"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android/Sdk/platform-tools"),
    ];

    private static readonly Dictionary<string, string?> Cache = new();

    public static string? Find(string tool)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(tool, out var cached))
                return cached;

            string exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Concat(ExtraDirs);

            string? found = dirs.Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
            Cache[tool] = found;
            return found;
        }
    }

    public static async Task<string> RunAsync(string tool, IEnumerable<string> args,
        bool allowFailure = false, CancellationToken ct = default)
    {
        var (exitCode, stdout, stderr) = await RunCoreAsync(tool, args, ct);
        if (exitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"{tool} falló ({exitCode}): {stderr.Trim()}");
        return stdout;
    }

    public static async Task<byte[]> RunBinaryAsync(string tool, IEnumerable<string> args, CancellationToken ct = default)
    {
        using var p = Start(tool, args);
        using var ms = new MemoryStream();
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.StandardOutput.BaseStream.CopyToAsync(ms, ct);
        await p.WaitForExitAsync(ct);
        await stderr;
        return ms.ToArray();
    }

    private static async Task<(int, string, string)> RunCoreAsync(string tool, IEnumerable<string> args, CancellationToken ct)
    {
        using var p = Start(tool, args);
        using var reg = ct.Register(() => { try { p.Kill(true); } catch { /* already exited */ } });
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await stdout, await stderr);
    }

    private static Process Start(string tool, IEnumerable<string> args)
    {
        string exe = Find(tool) ?? throw new FileNotFoundException($"No se encontró '{tool}'. Instálalo y vuelve a abrir la app.");
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        var p = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {tool}");
        // ffmpeg reads stdin for interactive keys; closing it prevents hangs and stray input.
        p.StandardInput.Close();
        return p;
    }
}
