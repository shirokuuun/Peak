using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UsageNotch.Core;

// Input can contain project paths and other private context. Persist only the allowlisted quota fields.
var input = await ReadBoundedAsync(Console.In, 256 * 1024);
if (args.Contains("--codex-event"))
{
    try
    {
        using var eventDocument = JsonDocument.Parse(input);
        if (CodexTaskEvents.Parse(eventDocument.RootElement, DateTimeOffset.UtcNow) is { } observation)
            CodexTaskEventWriter.Write(observation, Option("--output") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch", "codex-tasks"));
    }
    catch { /* Observation failure must never block, approve, deny, or continue a Codex turn. */ }
    Console.WriteLine("{}");
    return;
}
string status = "[Claude]";
try
{
    using var document = JsonDocument.Parse(input);
    var root = document.RootElement;
    var five = ReadLimit(root, "five_hour");
    var week = ReadLimit(root, "seven_day");
    var model = root.TryGetProperty("model", out var modelNode) ? GetString(modelNode, "display_name") ?? "Claude" : "Claude";
    var version = GetString(root, "version");
    var outputRoot = Option("--output") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch", "claude");
    Directory.CreateDirectory(outputRoot);
    var session = GetString(root, "session_id") ?? "default";
    var file = Path.Combine(outputRoot, Hash(session)[..16] + ".json");
    // Token/cost changes are evidence of CLI activity; repeated redraws do not renew freshness.
    var activity = root.TryGetProperty("context_window", out var context) ?
        $"{Number(context, "total_input_tokens")}/{Number(context, "total_output_tokens")}" : "";
    if (root.TryGetProperty("cost", out var cost)) activity += "/" + Number(cost, "total_cost_usd");
    var fingerprint = Hash($"{version}|{five}|{week}|{activity}");
    var observedAt = DateTimeOffset.UtcNow;
    var unchanged = false;
    if (File.Exists(file))
    {
        try
        {
            using var old = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            if (GetString(old.RootElement, "fingerprint") == fingerprint &&
                DateTimeOffset.TryParse(GetString(old.RootElement, "observedAt"), out var prior))
            { observedAt = prior; unchanged = true; }
        }
        catch (Exception exception) when (exception is IOException or JsonException) { }
    }
    using var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer))
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("observedAt", observedAt);
        writer.WriteString("version", version);
        writer.WriteString("fingerprint", fingerprint);
        writer.WriteStartObject("rateLimits");
        WriteLimit(writer, "fiveHour", five);
        WriteLimit(writer, "sevenDay", week);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
    var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
        if (!unchanged)
        {
            await File.WriteAllBytesAsync(temporary, buffer.ToArray());
            File.Move(temporary, file, overwrite: true);
        }
    }
    finally { if (File.Exists(temporary)) File.Delete(temporary); }
    status = $"[{model}]" + (five is null ? "" : $" · 5h {Math.Max(0, 100 - five.Used):0}% left") +
             (week is null ? "" : $" · 7d {Math.Max(0, 100 - week.Used):0}% left");
}
catch { /* Bridge failure must not interrupt Claude Code. */ }

// Preserve an existing user status-line command. Its stdout remains the visible status line.
var chainFile = Option("--chain-file");
if (chainFile is not null && File.Exists(chainFile))
{
    try
    {
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(chainFile));
        var savedText = GetString(saved.RootElement, "previousProtected");
        using var previousDocument = JsonDocument.Parse(savedText is not null ? WindowsSecretProtector.Unprotect(savedText) :
            saved.RootElement.TryGetProperty("previous", out var legacy) ? legacy.GetRawText() : "null");
        if (GetString(previousDocument.RootElement, "command") is { Length: > 0 } command)
        {
            var gitBash = new[] { Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe") }
                .FirstOrDefault(path => path is not null && File.Exists(path));
            var info = new ProcessStartInfo(gitBash ?? "powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            if (gitBash is null) { info.ArgumentList.Add("-NoProfile"); info.ArgumentList.Add("-Command"); }
            else info.ArgumentList.Add("-c");
            info.ArgumentList.Add(command);
            using var child = Process.Start(info)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                var output = child.StandardOutput.ReadToEndAsync(timeout.Token);
                var errors = child.StandardError.ReadToEndAsync(timeout.Token);
                await child.StandardInput.WriteAsync(input);
                child.StandardInput.Close();
                await child.WaitForExitAsync(timeout.Token);
                status = await output;
                await errors;
            }
            finally { if (!child.HasExited) child.Kill(entireProcessTree: true); }
        }
    }
    catch { }
}
Console.WriteLine(status.TrimEnd());

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
static string? GetString(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object &&
    node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
static string Number(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object &&
    node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetRawText() : "";
static Limit? ReadLimit(JsonElement root, string name)
{
    if (!root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object ||
        !limits.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object ||
        !value.TryGetProperty("used_percentage", out var used) || used.ValueKind != JsonValueKind.Number ||
        !used.TryGetDouble(out var percent) || !double.IsFinite(percent) || percent < 0) return null;
    long? seconds = value.TryGetProperty("resets_at", out var reset) && reset.ValueKind == JsonValueKind.Number &&
                    reset.TryGetInt64(out var parsed) && parsed is >= -62135596800 and <= 253402300799 ? parsed : null;
    return new(percent, seconds);
}
static void WriteLimit(Utf8JsonWriter writer, string name, Limit? limit)
{
    if (limit is null) { writer.WriteNull(name); return; }
    writer.WriteStartObject(name);
    writer.WriteNumber("usedPercentage", limit.Used);
    if (limit.Reset is { } reset) writer.WriteNumber("resetsAt", reset); else writer.WriteNull("resetsAt");
    writer.WriteEndObject();
}
static async Task<string> ReadBoundedAsync(TextReader reader, int maximum)
{
    var buffer = new char[4096];
    var result = new StringBuilder();
    while (await reader.ReadAsync(buffer) is var count && count > 0)
    {
        if (result.Length + count > maximum) return "{}";
        result.Append(buffer, 0, count);
    }
    return result.ToString();
}
internal sealed record Limit(double Used, long? Reset);
