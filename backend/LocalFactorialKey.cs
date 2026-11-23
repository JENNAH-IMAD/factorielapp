using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Pointage;

internal static class LocalFactorialKey
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PointageFactorial", "production-api-key.protected");

    public static string? Read(IDataProtectionProvider? protection)
    {
        if (!OperatingSystem.IsWindows() || protection == null || !File.Exists(FilePath)) return null;
        try { return protection.CreateProtector("Factorial.Production.ApiKey.v1").Unprotect(File.ReadAllText(FilePath)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException) { return null; }
    }

    public static void Save(IDataProtectionProvider protection, string key)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Utiliser le gestionnaire de secrets du serveur.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var encrypted = protection.CreateProtector("Factorial.Production.ApiKey.v1").Protect(key);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, encrypted);
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
