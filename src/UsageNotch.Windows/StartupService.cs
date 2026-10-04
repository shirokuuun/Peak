using Microsoft.Win32;

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
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
    }
}
