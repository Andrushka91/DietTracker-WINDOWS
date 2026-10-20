using System.Security.Cryptography;
using System.Text;

namespace DietTracker.Infrastructure.Security;

/// <summary>
/// Protects small secrets (provider tokens) with DPAPI in the current user's scope.
/// </summary>
/// <remarks>
/// DPAPI ties the ciphertext to the Windows user account, so a copied file is useless on another
/// account or machine. The optional entropy context is a fixed application string that prevents
/// another application running as the same user from simply unprotecting the blob.
/// </remarks>
public sealed class DpapiSecretProtector : Core.Interfaces.ISecretProtector
{
    private static readonly byte[] EntropySalt = Encoding.UTF8.GetBytes("DietTracker.Secrets.v1");

    /// <inheritdoc />
    public byte[] Protect(byte[] plaintext, string entropyContext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(plaintext, BuildEntropy(entropyContext), DataProtectionScope.CurrentUser);
    }

    /// <inheritdoc />
    public byte[] Unprotect(byte[] protectedData, string entropyContext)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return ProtectedData.Unprotect(protectedData, BuildEntropy(entropyContext), DataProtectionScope.CurrentUser);
    }

    private static byte[] BuildEntropy(string entropyContext)
    {
        var context = string.IsNullOrEmpty(entropyContext) ? "default" : entropyContext;
        var contextBytes = Encoding.UTF8.GetBytes(context);
        var buffer = new byte[EntropySalt.Length + contextBytes.Length];
        EntropySalt.CopyTo(buffer, 0);
        contextBytes.CopyTo(buffer, EntropySalt.Length);
        return SHA256.HashData(buffer);
    }
}
