using System.Text.Json;
using System.IO;
using UsageNotch.Core;

namespace UsageNotch.Windows;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string DataDirectory { get; }
    public SettingsStore(string? dataDirectory = null) => DataDirectory = dataDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageNotch");

    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public string CachePath => Path.Combine(DataDirectory, "usage-cache.json");
    public string AlertsPath => Path.Combine(DataDirectory, "alert-state.json");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            await using var stream = File.Open(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? new AppSettings()).Normalize();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(SettingsPath, settings.Normalize(), cancellationToken);

    public async Task<IReadOnlyList<ProviderUsage>> LoadCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(CachePath)) return [];
            await using var stream = File.Open(CachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await JsonSerializer.DeserializeAsync<List<ProviderUsage>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public Task SaveCacheAsync(IReadOnlyList<ProviderUsage> providers, CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(CachePath, providers.Select(p => p with { AccountLabel = p.ProviderId + " account" }).ToArray(), cancellationToken);

    public async Task<IReadOnlyDictionary<string, AlertCheckpoint>> LoadAlertsAsync()
    {
        try
        {
            return File.Exists(AlertsPath) ? JsonSerializer.Deserialize<Dictionary<string, AlertCheckpoint>>(
                await File.ReadAllTextAsync(AlertsPath), JsonOptions) ?? new() : new();
        }
        catch { return new Dictionary<string, AlertCheckpoint>(); }
    }

    public Task SaveAlertsAsync(IReadOnlyDictionary<string, AlertCheckpoint> state, CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(AlertsPath, state, cancellationToken);

    private async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
