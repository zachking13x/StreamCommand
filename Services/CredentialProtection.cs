using System;
using System.Security.Cryptography;
using System.Text;

namespace StreamCommand.Services;

/// <summary>
/// Thrown when a credential could not be encrypted. Callers must NOT fall back to
/// writing the plaintext — drop the secret instead and surface the error.
/// </summary>
public sealed class CredentialProtectionException : Exception
{
    public CredentialProtectionException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// DPAPI wrapper — protects credential strings at rest using the current Windows
/// user account as the key.  Credentials survive app reinstalls and OS updates but
/// are tied to the user account, which is correct behaviour for a personal tool.
///
/// Format stored in settings.json:  "dpapi:{base64}"
///
/// FAIL-CLOSED CONTRACT (audit SC-02):
/// If DPAPI is unavailable this type NEVER returns the plaintext. Previously a
/// catch-all returned the raw secret, which SettingsService then serialized to disk —
/// meaning a crypto/context failure silently wrote Twitch tokens, API keys, and the
/// OBS password to settings.json in the clear. Encryption failure is now a hard error;
/// the caller drops the value and tells the user to re-enter it.
///
/// Legacy plaintext values (no prefix) are still READ for backward compatibility, but
/// are reported via <see cref="LooksLikeLegacyPlaintext"/> so they can be re-written
/// under DPAPI on the next save.
/// </summary>
public static class CredentialProtection
{
    private const string Prefix = "dpapi:";

    /// <summary>
    /// Description of the most recent protection failure, or null when the last
    /// operation succeeded. The Settings UI surfaces this so a silent downgrade is
    /// impossible to miss.
    /// </summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> with DPAPI.
    /// Returns true on success (or when the input is empty — nothing to protect).
    /// Returns false when encryption failed; <paramref name="result"/> is then an
    /// empty string and NEVER the plaintext.
    /// </summary>
    public static bool TryProtect(string plaintext, out string result)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            result = plaintext;
            return true;
        }

        // Already protected — pass through untouched so re-saving doesn't double-encrypt.
        if (IsProtected(plaintext))
        {
            result = plaintext;
            return true;
        }

        try
        {
            var raw       = Encoding.UTF8.GetBytes(plaintext);
            var protected_ = ProtectedData.Protect(raw, null, DataProtectionScope.CurrentUser);
            result    = Prefix + Convert.ToBase64String(protected_);
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            // Fail closed: never hand back the plaintext for persistence.
            LastError = $"Credential encryption unavailable ({ex.GetType().Name}). " +
                        "The value was not saved to disk.";
            result    = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/>, throwing if protection is unavailable.
    /// Prefer <see cref="TryProtect"/> at persistence boundaries.
    /// </summary>
    /// <exception cref="CredentialProtectionException">Encryption failed.</exception>
    public static string Protect(string plaintext)
    {
        if (TryProtect(plaintext, out var result)) return result;
        throw new CredentialProtectionException(
            LastError ?? "Credential encryption failed.", new CryptographicException("DPAPI unavailable"));
    }

    /// <summary>
    /// Decrypts a DPAPI-protected string produced by <see cref="TryProtect"/>.
    /// Returns the original value if it was never protected (legacy plaintext).
    /// Returns an empty string if decryption fails — the credential must be re-entered.
    /// </summary>
    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;  // legacy plaintext

        try
        {
            var base64    = stored[Prefix.Length..];
            var raw       = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            // Usually terminal (copied settings from another user/machine), but record it
            // so the UI can explain the sudden "disconnected" state instead of staying silent.
            LastError = $"A saved credential could not be decrypted ({ex.GetType().Name}). " +
                        "Please reconnect the affected account.";
            return "";
        }
    }

    /// <summary>Returns true if the value has already been protected and can be stored as-is.</summary>
    public static bool IsProtected(string value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="value"/> is a non-empty secret stored WITHOUT the dpapi
    /// prefix — i.e. legacy plaintext on disk that should be re-written encrypted.
    /// </summary>
    public static bool LooksLikeLegacyPlaintext(string value)
        => !string.IsNullOrEmpty(value) && !IsProtected(value);

    /// <summary>Clears <see cref="LastError"/> after the UI has surfaced it.</summary>
    public static void ClearLastError() => LastError = null;
}
