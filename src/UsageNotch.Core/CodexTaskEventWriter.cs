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
        if (ObservationRetention.IsLink(directory)) return;
        using var gate = new Mutex(false, ObservationRetention.MutexName(directory));
        var acquired = false;
        try
        {
            try { acquired = gate.WaitOne(500); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            var path = Path.Combine(directory, observation.ThreadId + ".jsonl");
            if (File.Exists(path) && ObservationRetention.IsLink(path)) return;
            using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                writer.WriteLine(JsonSerializer.Serialize(observation));
            ObservationRetention.PruneLocked(directory, true, observation.ObservedAt);
        }
        finally { if (acquired) gate.ReleaseMutex(); }
    }
}
