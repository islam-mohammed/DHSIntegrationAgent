using System.Security.Cryptography;

namespace DHSIntegrationAgent.Infrastructure.Security;

internal interface IKeyProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedBlob);
}

internal sealed class DpapiKeyProtector : IKeyProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is Windows-only.");

        // CurrentUser scope: ties key to the user profile (good default for desktop agent).
        return ProtectedData.Protect(plaintext, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedBlob)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI is Windows-only.");

        return ProtectedData.Unprotect(protectedBlob, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
    }
}
