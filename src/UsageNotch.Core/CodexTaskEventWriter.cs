using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UsageNotch.Core;

public static class CodexTaskEventWriter
{
    public static void Write(CodexTaskEvent observation, string directory)
    {
        if (!CodexTaskEvents.ValidThreadId(observation.ThreadId)) return;
        Directory.CreateDirectory(directory);
        var lockId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory) + observation.ThreadId)))[..24];
        using var gate = new Mutex(false, "Local\\UsageNotch.TaskEvent." + lockId);
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(500); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            using var file = new FileStream(Path.Combine(directory, observation.ThreadId + ".jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(file, new UTF8Encoding(false));
            writer.WriteLine(JsonSerializer.Serialize(observation));
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }
}
