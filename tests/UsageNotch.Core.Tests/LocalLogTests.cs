using System.Text.Json;
using UsageNotch.Core;
using UsageNotch.Providers;

namespace UsageNotch.Core.Tests;

public sealed class LocalLogTests
{
    [Fact] public async Task RecentReportedLogQuotaIsUnverifiedAndPrivateFieldsAreNotNormalized()
    {
        var root = Path.Combine(Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(), "UsageNotch.Tests") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test directory.");
        var directory = Path.Combine(root, "sessions", DateTime.UtcNow.ToString("yyyy/MM/dd"));
        Directory.CreateDirectory(directory);
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow, privatePrompt = "DO-NOT-PERSIST",
                payload = new { type = "token_count", rate_limits = new { limit_id = "codex",
                    primary = new { used_percent = 88, window_minutes = 300, resets_at = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() } } }
            });
            await File.WriteAllTextAsync(Path.Combine(directory, "rollout-test.jsonl"), "truncated-json\n" + line + "\n");
            var result = await new CodexLocalSessionReader(root).ReadLatestAsync("test", default);
            Assert.NotNull(result);
            Assert.Equal(88, Assert.Single(result.Windows).UsedPercent!.Value);
            Assert.False(Assert.Single(result.Sources).AccountVerified);
            Assert.Null(UsageSelection.FindCritical([result]));
            Assert.DoesNotContain("DO-NOT-PERSIST", JsonSerializer.Serialize(result));
        }
        finally { Directory.Delete(root, true); }
    }
}
