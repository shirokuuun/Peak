namespace UsageNotch.Core;

public static class PeakDataPaths
{
    // Store processes virtualize AppData. CLI bridges must share a real per-user
    // folder with the external providers, even when run without package identity.
    public static string Root => ResolveRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), IsStoreBuild);

    public static bool IsStoreBuild =>
#if PEAK_STORE
        true;
#else
        false;
#endif

    public static string ResolveRoot(string profile, string localAppData, bool storeBuild) =>
        storeBuild ? Path.Combine(profile, ".peak", "store") : Path.Combine(localAppData, "UsageNotch");
}
