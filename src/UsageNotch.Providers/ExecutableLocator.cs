namespace UsageNotch.Providers;

internal static class ExecutableLocator
{
    public static string? Find(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        // App-server requires a native executable, not a shell wrapper.
        var extensions = executable == "codex" ? new[] { ".exe" } : new[] { ".exe", ".cmd", ".ps1" };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var extension in extensions)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), executable + extension);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }

        if (executable.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
            if (Directory.Exists(npm))
            {
                var native = Directory.EnumerateDirectories(npm, "codex*").SelectMany(root =>
                    Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)).FirstOrDefault();
                if (native is not null) return native;
            }
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                return Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
            }
        }

        return null;
    }
}
