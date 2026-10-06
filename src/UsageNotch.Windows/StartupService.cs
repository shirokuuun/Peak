using Microsoft.Win32;
using System.IO;

namespace UsageNotch.Windows;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Peak";
    private const string LegacyValueName = "UsageNotch";

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --startup");
        }
        else
        {
            RemoveIfOwned(key, ValueName);
        }
        RemoveIfOwned(key, LegacyValueName);
    }

    public void RemoveOwnedEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        RemoveIfOwned(key, ValueName); RemoveIfOwned(key, LegacyValueName);
    }

    private static void RemoveIfOwned(RegistryKey key, string name)
    {
        if (key.GetValue(name) is not string command) return;
        var executable = command.StartsWith('"') ? command.Split('"').ElementAtOrDefault(1) : command.Split(' ')[0];
        if (Path.GetFileName(executable) is "Peak.exe" or "UsageNotch.exe") key.DeleteValue(name, false);
    }
}
