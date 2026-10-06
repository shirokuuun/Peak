using System.IO;
using System.Text.Json;
using UsageNotch.Core;

namespace UsageNotch.App;

internal static class PublishingSetup
{
    public static PublishingConfiguration Load()
    {
        try
        {
            return JsonSerializer.Deserialize<PublishingConfiguration>(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "publishing.json")),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        { return new(); }
    }
}
