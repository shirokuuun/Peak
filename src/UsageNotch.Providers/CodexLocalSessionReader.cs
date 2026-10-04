using System.Text;
using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.Providers;

internal sealed class CodexLocalSessionReader
{
    private readonly string? _root;
    public CodexLocalSessionReader(string? root = null) => _root = root;
    public async Task<ProviderUsage?> ReadLatestAsync(string? toolVersion, CancellationToken cancellationToken)
    {
        var codexRoot = _root ?? Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var sessions = Path.Combine(codexRoot, "sessions");
        if (!Directory.Exists(sessions)) return null;

        // Reconcile only recent date directories; never scan the entire session history.
        var recentDirectories = Enumerable.Range(-1, 15).Select(offset =>
            Path.Combine(sessions, DateTime.UtcNow.AddDays(-offset).ToString("yyyy/MM/dd")))
            .Where(Directory.Exists);
        var files = recentDirectories.SelectMany(directory => Directory.EnumerateFiles(directory, "*.jsonl"))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(12);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = await ReadTailAsync(file.FullName, 512 * 1024, cancellationToken).ConfigureAwait(false);
            foreach (var line in lines.Reverse())
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!root.TryGetProperty("payload", out var payload) ||
                        CodexUsageMapper.GetString(payload, "type") != "token_count" ||
                        !payload.TryGetProperty("rate_limits", out var limits) ||
                        limits.ValueKind != JsonValueKind.Object) continue;

                    var now = DateTimeOffset.UtcNow;
                    var observedAt = root.TryGetProperty("timestamp", out var timestamp) &&
                                     timestamp.ValueKind == JsonValueKind.String &&
                                     DateTimeOffset.TryParse(timestamp.GetString(), out var parsed)
                        ? parsed : new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
                    var windows = new List<UsageWindow>();
                    CodexUsageMapper.MapBucket(limits, CodexUsageMapper.GetString(limits, "limit_id") ?? "codex",
                        "codex-local-log", now, windows);
                    windows = windows.Select(w => w with
                    {
                        Freshness = now - observedAt > TimeSpan.FromMinutes(5)
                            ? Freshness.Stale : w.Freshness
                    }).ToList();

                    var source = new SourceObservation("codex-local-log", SourceKind.LocalLog, toolVersion,
                        now, observedAt, null, observedAt.AddMinutes(5), false);
                    return new ProviderUsage(1, "codex", "unverified-local", "Codex local activity", null,
                        ConnectionState.Ready, [source], windows, [], observedAt,
                        new ProviderError("live_unavailable", "Local log · account unverified · excluded from summary and alerts.", true));
                }
                catch (JsonException) { }
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<string>> ReadTailAsync(string path, int bytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var offset = Math.Max(0, stream.Length - bytes);
        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: false);
        if (offset > 0) await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        return content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
