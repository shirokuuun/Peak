using System.Security.Cryptography;
using System.Text;

namespace UsageNotch.Core;

public static class WindowsSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UsageNotch.StatusLine.v1");
    public static string Protect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI is required.");
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
    }
    public static string Unprotect(string value)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows DPAPI is required.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), Entropy, DataProtectionScope.CurrentUser));
    }
}
