using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageNotch.Core;

/// <summary>Applies only to Peak-owned observations, never to provider transcripts or configuration.</summary>
public static class ObservationRetention
{
    public static readonly TimeSpan MaximumAge = TimeSpan.FromDays(7);
    public const long MaximumDirectoryBytes = 10 * 1024 * 1024;
    public const int MaximumTaskFileBytes = 512 * 1024;

    public static string MutexName(string directory) => "Local\\UsageNotch.Retention." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())))[..24];

    public static bool IsOwnedFile(string path, bool tasks) => tasks
        ? Path.GetExtension(path) == ".jsonl" && CodexTaskEvents.ValidThreadId(Path.GetFileNameWithoutExtension(path))
        : Path.GetExtension(path) == ".json" && Path.GetFileNameWithoutExtension(path) is { Length: 16 } name &&
            name.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static void Prune(string directory, bool tasks, DateTimeOffset now, long maximumBytes = MaximumDirectoryBytes)
    {
        if (!Directory.Exists(directory) || IsLink(directory)) return;
        using var gate = new Mutex(false, MutexName(directory));
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(500); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            PruneLocked(directory, tasks, now, maximumBytes);
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }

    internal static void PruneLocked(string directory, bool tasks, DateTimeOffset now, long maximumBytes = MaximumDirectoryBytes)
    {
        if (IsLink(directory)) return;
        var cutoff = now - MaximumAge;
        foreach (var path in Directory.EnumerateFiles(directory).Where(p => IsOwnedFile(p, tasks)))
        {
            try
            {
                if (IsLink(path)) continue;
                if (File.GetLastWriteTimeUtc(path) < cutoff.UtcDateTime) { File.Delete(path); continue; }
                if (tasks) CompactTaskFile(path, cutoff);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var files = Directory.EnumerateFiles(directory).Where(p => IsOwnedFile(p, tasks) && !IsLink(p))
            .Select(p => new FileInfo(p)).OrderBy(f => f.LastWriteTimeUtc).ToArray();
        var size = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (size <= maximumBytes) break;
            try { var length = file.Length; file.Delete(); size -= length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static void CompactTaskFile(string path, DateTimeOffset cutoff)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var offset = Math.Max(0, stream.Length - MaximumTaskFileBytes);
        stream.Position = offset;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        if (offset > 0) reader.ReadLine(); // Drop a possibly partial first record.
        var lines = new List<string>();
        var changed = offset > 0;
        while (reader.ReadLine() is { } line)
        {
            try
            {
                var observation = JsonSerializer.Deserialize<CodexTaskEvent>(line);
                if (observation is null || observation.ObservedAt < cutoff) { changed = true; continue; }
                lines.Add(line);
            }
            catch (JsonException) { changed = true; }
        }
        reader.Dispose();
        if (!changed) return;
        if (lines.Count == 0) { File.Delete(path); return; }
        var modified = File.GetLastWriteTimeUtc(path);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            File.Move(temporary, path, true);
            File.SetLastWriteTimeUtc(path, modified);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
