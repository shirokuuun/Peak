using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace UsageNotch.Windows;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Peak";
    private const string LegacyValueName = "UsageNotch";
    public const string StoreTaskId = "PeakStartup";

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (!HasPackageIdentity())
        {
            SetDesktopEnabled(enabled);
            return enabled;
        }
        var task = await StartupTask.GetAsync(StoreTaskId);
        if (!enabled) { task.Disable(); return false; }
        // Windows owns the decision; a Task Manager opt-out must be respected.
        return await task.RequestEnableAsync() == StartupTaskState.Enabled;
    }

    public async Task<bool?> GetPackagedEnabledAsync() => HasPackageIdentity()
        ? (await StartupTask.GetAsync(StoreTaskId)).State == StartupTaskState.Enabled
        : null;

    private void SetDesktopEnabled(bool enabled)
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
        if (HasPackageIdentity())
        {
            StartupTask.GetAsync(StoreTaskId).AsTask().GetAwaiter().GetResult().Disable();
            return;
        }
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

    private static bool HasPackageIdentity()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) == 122; // ERROR_INSUFFICIENT_BUFFER
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, IntPtr fullName);
}
