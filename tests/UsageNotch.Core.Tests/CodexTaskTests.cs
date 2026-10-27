using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using UsageNotch.Core;
using UsageNotch.Providers;
using UsageNotch.Windows;

namespace UsageNotch.Core.Tests;

public sealed class CodexTaskTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";
    private static readonly DateTimeOffset At = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static CodexTaskEvent Event(string name, int second, string key = "a", string turn = "turn1") =>
        new(1, Id, turn, name, At.AddSeconds(second), "notch", "Bash", key);

    [Fact] public void HookInputPersistsNoSecretsCommandsPromptsOrTranscripts()
    {
        using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { session_id = Id,
            transcript_path = "C:/private/rollout-" + Id + ".jsonl", cwd = "D:/private/notch", hook_event_name = "PermissionRequest",
            tool_name = "Bash", tool_input = new { command = "echo DO-NOT-STORE-TOKEN", description = "PRIVATE-REASON" },
            prompt = "PRIVATE-PROMPT", last_assistant_message = "PRIVATE-OUTPUT", turn_id = "turn1" }));
        var observation = CodexTaskEvents.Parse(input.RootElement, At);
        Assert.NotNull(observation);
        var saved = JsonSerializer.Serialize(observation);
        foreach (var secret in new[] { "DO-NOT-STORE-TOKEN", "PRIVATE-REASON", "PRIVATE-PROMPT", "PRIVATE-OUTPUT", "C:/private", "D:/private" })
            Assert.DoesNotContain(secret, saved);
        Assert.Equal("notch", observation.Project);
        Assert.Equal(24, observation.OperationKey!.Length);
    }

    [Fact] public void ConcurrentToolCompletionDoesNotClearAnotherApprovalAndNoticeKeyIsStable()
    {
        var before = CodexTaskEvents.Reduce([Event("UserPromptSubmit", 0), Event("PreToolUse", 1),
            Event("PermissionRequest", 2)], "Chat", At.AddSeconds(3));
        var during = CodexTaskEvents.Reduce([Event("UserPromptSubmit", 0), Event("PreToolUse", 1),
            Event("PermissionRequest", 2), Event("PreToolUse", 3, "b"), Event("PostToolUse", 4, "b")], "Chat", At.AddSeconds(5));
        Assert.Equal(CodexTaskState.ApprovalRequested, during.State);
        Assert.Equal(before.AttentionKey, during.AttentionKey);
        var after = CodexTaskEvents.Reduce([Event("PermissionRequest", 2), Event("PostToolUse", 5)], "Chat", At.AddSeconds(6));
        Assert.Equal(CodexTaskState.Working, after.State);
        Assert.False(after.NeedsAttention);
    }

    [Fact] public void NewTurnClearsOldRequestAndLateOldCompletionCannotFinishNewTurn()
    {
        var result = CodexTaskEvents.Reduce([Event("PermissionRequest", 0), Event("UserPromptSubmit", 1, turn: "turn2"),
            Event("Stop", 2, turn: "turn1"), Event("PreToolUse", 3, turn: "turn2")], "Chat", At.AddSeconds(4));
        Assert.Equal(CodexTaskState.Working, result.State);
        Assert.False(result.NeedsAttention);
    }

    [Theory] [InlineData("Stop", CodexTaskState.Idle)] [InlineData("Interrupt", CodexTaskState.Interrupted)]
    [InlineData("SessionEnd", CodexTaskState.Idle)]
    public void EndingEventsClearRequestedApproval(string name, CodexTaskState expected)
    {
        var result = CodexTaskEvents.Reduce([Event("PermissionRequest", 0), Event(name, 1)], "Chat", At.AddSeconds(2));
        Assert.Equal(expected, result.State);
        Assert.False(result.NeedsAttention);
    }

    [Fact] public void OldApprovalObservationBecomesUnknownRatherThanAnActiveAlert()
    {
        var result = CodexTaskEvents.Reduce([Event("PermissionRequest", 0)], "Chat", At.AddMinutes(6));
        Assert.Equal(CodexTaskState.Stale, result.State);
        Assert.False(result.NeedsAttention);
    }

    [Fact] public void OperationFingerprintMatchesRegardlessOfJsonPropertyOrder()
    {
        using var first = JsonDocument.Parse($$$"""{"session_id":"{{{Id}}}","hook_event_name":"PreToolUse","tool_name":"Bash","tool_input":{"a":1,"b":2}}""");
        using var second = JsonDocument.Parse($$$"""{"session_id":"{{{Id}}}","hook_event_name":"PostToolUse","tool_name":"Bash","tool_input":{"b":2,"a":1}}""");
        Assert.Equal(CodexTaskEvents.Parse(first.RootElement, At)!.OperationKey, CodexTaskEvents.Parse(second.RootElement, At)!.OperationKey);
    }

    [Theory]
    [InlineData("Bash")]
    [InlineData("apply_patch")]
    public void ApprovalDescriptionDoesNotPreventMatchingToolCompletion(string tool)
    {
        CodexTaskEvent Parse(string name, int second, object args)
        {
            using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { session_id = Id, turn_id = "turn1",
                hook_event_name = name, tool_name = tool, tool_input = args }));
            return CodexTaskEvents.Parse(input.RootElement, At.AddSeconds(second))!;
        }
        var before = Parse("PreToolUse", 0, new { command = "PRIVATE-COMMAND" });
        var request = Parse("PermissionRequest", 1, new { command = "PRIVATE-COMMAND", description = "PRIVATE-REASON" });
        var completion = Parse("PostToolUse", 2, new { command = "PRIVATE-COMMAND", description = (string?)null });

        Assert.Equal(before.OperationKey, request.OperationKey);
        Assert.Equal(request.OperationKey, completion.OperationKey);
        var waiting = CodexTaskEvents.Reduce([before, request], "Chat", At.AddSeconds(2));
        Assert.True(waiting.NeedsAttention);
        var finished = CodexTaskEvents.Reduce([before, request, completion], "Chat", At.AddSeconds(3));
        Assert.Equal(CodexTaskState.Working, finished.State);
        Assert.False(finished.NeedsAttention);
        Assert.Null(finished.AttentionKey);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(request));
    }

    [Theory]
    [InlineData("Bash", "first command", "second command", "same reason", "same reason")]
    [InlineData("mcp__example__run", "same command", "same command", "first argument", "second argument")]
    public void DistinctOperationsKeepSeparateFingerprints(string tool, string firstCommand, string secondCommand,
        string firstDescription, string secondDescription)
    {
        CodexTaskEvent Parse(string command, string description)
        {
            using var input = JsonDocument.Parse(JsonSerializer.Serialize(new { session_id = Id,
                hook_event_name = "PermissionRequest", tool_name = tool, tool_input = new { command, description } }));
            return CodexTaskEvents.Parse(input.RootElement, At)!;
        }
        Assert.NotEqual(Parse(firstCommand, firstDescription).OperationKey, Parse(secondCommand, secondDescription).OperationKey);
    }

    [Fact] public void UnrelatedActivityDoesNotKeepAnOldApprovalAlive()
    {
        var task = CodexTaskEvents.Reduce([Event("PermissionRequest", 0), Event("PreToolUse", 300, "other"),
            Event("PostToolUse", 301, "other")], "Chat", At.AddSeconds(302));
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.False(task.NeedsAttention);
        Assert.Null(task.AttentionKey);
    }

    [Fact] public void ExpiringOneRequestPreservesAnotherFreshRequest()
    {
        var task = CodexTaskEvents.Reduce([Event("PermissionRequest", 0), Event("PermissionRequest", 300, "other")],
            "Chat", At.AddSeconds(302));
        Assert.Equal(CodexTaskState.ApprovalRequested, task.State);
        Assert.Equal("turn1|other", task.AttentionKey);
    }

    [Fact] public void LocalLogActivityNeverFabricatesAnApprovalOrUsesToolContents()
    {
        var lines = new[] { "partial-json", JsonSerializer.Serialize(new { timestamp = At,
            type = "event_msg", payload = new { type = "task_started", privatePrompt = "PRIVATE" } }),
            JsonSerializer.Serialize(new { timestamp = At.AddSeconds(1), type = "response_item",
                payload = new { type = "custom_tool_call", name = "request_permissions", input = "PRIVATE" } }) };
        var task = CodexTaskMonitor.ParseLocal(Id, "Chat", "notch", lines, At.AddSeconds(2));
        Assert.NotNull(task);
        Assert.Equal(CodexTaskState.Working, task.State);
        Assert.False(task.NeedsAttention);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(task));
    }

    [Fact] public void CatalogRuntimeNotLoadedIsNotTreatedAsAnIdleExistingChat()
    {
        using var response = JsonDocument.Parse($$$"""{"data":[{"id":"{{{Id}}}","name":"Actual chat name","preview":"PRIVATE PROMPT","cwd":"D:/work/notch","status":{"type":"notLoaded"}}]}""");
        var metadata = Assert.Single(CodexTaskMetadata.Parse(response.RootElement));
        Assert.Equal("Actual chat name", metadata.Title);
        Assert.Equal("notch", metadata.Project);
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(metadata));
    }

    [Fact] public void HookSetupPreservesOtherHooksIsIdempotentAndDisconnectOnlyRemovesOwnedEntries()
    {
        using var temp = new TaskTemp();
        var config = Path.Combine(temp.Path, "hooks.json");
        var source = Path.Combine(temp.Path, "source"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "UsageNotch.ClaudeBridge.exe"), "test-only");
        File.WriteAllText(config, """{"description":"keep","hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo keep"}]}]}}""");
        var setup = new CodexTaskBridgeSetup(config, Path.Combine(temp.Path, "bridge"));
        setup.Install(source); setup.Install(source);
        Assert.True(setup.IsInstalled());
        var installed = JsonNode.Parse(File.ReadAllText(config))!;
        Assert.Equal(2, installed["hooks"]!["Stop"]!.AsArray().Count);
        setup.Uninstall();
        Assert.False(setup.IsInstalled());
        var result = JsonNode.Parse(File.ReadAllText(config))!;
        Assert.Equal("keep", result["description"]!.GetValue<string>());
        Assert.Equal("echo keep", result["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact] public async Task WindowsHookCommandWorksWithSpacesAndUnicodeAndReturnsNoDecision()
    {
        using var temp = new TaskTemp();
        var config = Path.Combine(temp.Path, "hooks.json");
        var project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/UsageNotch.ClaudeBridge/bin/Release/net10.0"));
        Assert.True(File.Exists(Path.Combine(project, "UsageNotch.ClaudeBridge.exe")));
        var setup = new CodexTaskBridgeSetup(config, Path.Combine(temp.Path, "bridge with spaces")); setup.Install(project);
        var settings = JsonNode.Parse(File.ReadAllText(config))!;
        var command = settings["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["commandWindows"]!.GetValue<string>();
        var observerDirectory = Path.Combine(temp.Path, "events");
        // Override only the test command output path. Real hooks use their default local directory.
        command = command.Replace("--codex-event --observer", "--codex-event --output '" + observerDirectory.Replace("'", "''") + "' --observer");
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        // cmd does not use the CommandLineToArgvW escaping used by ArgumentList for nested quotes.
        start.Arguments = "/d /s /c \"" + command + "\"";
        using var process = Process.Start(start)!;
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new { session_id = Id, hook_event_name = "PermissionRequest",
            turn_id = "turn1", cwd = "D:/work/日本語", tool_name = "Bash", tool_input = new { command = "SECRET-DO-NOT-STORE" } }));
        process.StandardInput.Close();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("{}", stdout.Trim());
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
        var saved = File.ReadAllText(Path.Combine(observerDirectory, Id + ".jsonl"));
        var observation = JsonSerializer.Deserialize<CodexTaskEvent>(saved)!;
        Assert.Equal("日本語", observation.Project);
        Assert.DoesNotContain("SECRET-DO-NOT-STORE", saved);
        Assert.DoesNotContain("decision", stdout, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TaskTemp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UsageNotch.Tests", Guid.NewGuid().ToString("N"));
        public TaskTemp() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            var full = System.IO.Path.GetFullPath(Path);
            if (!full.StartsWith(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UsageNotch.Tests") + System.IO.Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid test path.");
            Directory.Delete(full, true);
        }
    }
}
