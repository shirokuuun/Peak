namespace UsageNotch.Providers;

internal static class ExecutableLocator
{
    public static string? Find(string executable, string? searchPath = null, string? userProfile = null,
        string? applicationData = null, string? localApplicationData = null)
    {
        var path = searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roaming = applicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = localApplicationData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        // App-server requires a native executable, not a shell wrapper.
        var extensions = executable == "codex" ? new[] { ".exe" } : new[] { ".exe", ".cmd", ".ps1" };

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        foreach (var extension in extensions)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), executable + extension);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch { }
        }

        // Claude's native installer and common local launchers use this location.
        // A running app may still have the old PATH.
        var launcher = Path.Combine(profile, ".local", "bin", executable + ".exe");
        if (File.Exists(launcher)) return launcher;

        if (executable.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(roaming, "npm", "claude" + extension);
                if (File.Exists(candidate)) return candidate;
            }
        }

        if (executable.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            var standalone = Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            if (File.Exists(standalone)) return standalone;
            var npm = Path.Combine(roaming, "npm", "node_modules", "@openai");
            if (Directory.Exists(npm))
            {
                var native = Directory.EnumerateDirectories(npm, "codex*").SelectMany(root =>
                    Directory.EnumerateFiles(root, "codex.exe", SearchOption.AllDirectories)).FirstOrDefault();
                if (native is not null) return native;
            }
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
