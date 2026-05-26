using System;
using System.Security.Cryptography;
using System.Text;

namespace StreamCommand.Services;

/// <summary>
/// DPAPI wrapper — protects credential strings at rest using the current Windows
/// user account as the key.  Credentials survive app reinstalls and OS updates but
/// are tied to the user account, which is correct behaviour for a personal tool.
///
/// Format stored in settings.json:  "dpapi:{base64}"
/// Backward-compatible: plain strings (no prefix) are returned as-is so existing
/// settings continue to work after upgrading to this version.
/// </summary>
public static class CredentialProtection
{
    private const string Prefix = "dpapi:";

    /// <summary>Encrypts <paramref name="plaintext"/> with DPAPI and returns a prefixed base-64 string.</summary>
    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        try
        {
            var raw       = Encoding.UTF8.GetBytes(plaintext);
            var protected_ = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(protected_);
        }
        catch
        {
            // DPAPI unavailable in this execution context — return plaintext unchanged.
            // This should not happen in a Store app running under a real user session.
            return plaintext;
        }
    }

    /// <summary>
    /// Decrypts a DPAPI-protected string produced by <see cref="Protect"/>.
    /// Returns the original plaintext if the input was never protected (backward compat).
    /// Returns an empty string if decryption fails (credential needs re-entry).
    /// </summary>
    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;  // plaintext (old format)
        try
        {
            var base64    = stored[Prefix.Length..];
            var raw       = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return "";   // Cannot decrypt — credential needs to be re-entered
        }
    }

    /// <summary>Returns true if the value has already been protected and can be stored as-is.</summary>
    public static bool IsProtected(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);
}
