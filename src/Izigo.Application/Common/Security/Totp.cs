using System.Security.Cryptography;
using System.Text;

namespace Izigo.Application.Common.Security;

/// <summary>
/// Manual RFC 6238 TOTP implementation — no external NuGet required.
/// </summary>
public static class Totp
{
    private const string Base32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    // ── Secret generation ───────────────────────────────────────────────────────

    /// <summary>Generates a 20-byte random secret encoded as Base32.</summary>
    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return ToBase32(bytes);
    }

    // ── URI for QR scanning ─────────────────────────────────────────────────────

    /// <summary>Returns an otpauth:// URI suitable for Google Authenticator / Authy.</summary>
    public static string GetUri(string secret, string email, string issuer)
    {
        var encodedIssuer = Uri.EscapeDataString(issuer);
        var encodedEmail  = Uri.EscapeDataString(email);
        return $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={secret}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
    }

    // ── Verification ────────────────────────────────────────────────────────────

    /// <summary>Validates a 6-digit TOTP code against the current ± 1 time window.</summary>
    public static bool Verify(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !long.TryParse(code, out _))
            return false;

        var secretBytes = FromBase32(secret.ToUpperInvariant().TrimEnd('='));
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        for (var delta = -1; delta <= 1; delta++)
        {
            if (ComputeTotp(secretBytes, t + delta) == code)
                return true;
        }
        return false;
    }

    // ── Recovery codes ──────────────────────────────────────────────────────────

    /// <summary>
    /// Generates 8 recovery codes (8 chars each, alphanumeric).
    /// Returns (plainCodes, hashedCodes) — store the hashes, return plain to user once.
    /// </summary>
    public static (string[] Plain, string[] Hashed) GenerateRecoveryCodes()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // unambiguous chars
        const int count = 8;
        const int length = 8;

        var plain  = new string[count];
        var hashed = new string[count];

        for (var i = 0; i < count; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(length);
            var sb    = new StringBuilder(length);
            foreach (var b in bytes)
                sb.Append(alphabet[b % alphabet.Length]);

            plain[i]  = sb.ToString();
            hashed[i] = HashCode(plain[i]);
        }

        return (plain, hashed);
    }

    /// <summary>Hash a recovery code for storage (SHA-256 hex).</summary>
    public static string HashCode(string code)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(hash).ToLower();
    }

    // ── Internal helpers ────────────────────────────────────────────────────────

    private static string ComputeTotp(byte[] secret, long counter)
    {
        // Build 8-byte big-endian counter
        var msg = new byte[8];
        for (var i = 7; i >= 0; i--)
        {
            msg[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // HMAC-SHA1
        var hmac   = HMACSHA1.HashData(secret, msg);

        // Dynamic truncation
        var offset = hmac[19] & 0x0F;
        var code   = ((hmac[offset]     & 0x7F) << 24)
                   | ((hmac[offset + 1] & 0xFF) << 16)
                   | ((hmac[offset + 2] & 0xFF) << 8)
                   |  (hmac[offset + 3] & 0xFF);

        return (code % 1_000_000).ToString("D6");
    }

    private static string ToBase32(byte[] bytes)
    {
        var sb   = new StringBuilder();
        var bits = 0;
        var acc  = 0;

        foreach (var b in bytes)
        {
            acc  = (acc << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Base32Chars[(acc >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
            sb.Append(Base32Chars[(acc << (5 - bits)) & 0x1F]);

        return sb.ToString();
    }

    private static byte[] FromBase32(string s)
    {
        var output = new List<byte>();
        var bits   = 0;
        var acc    = 0;

        foreach (var c in s)
        {
            var val = Base32Chars.IndexOf(c);
            if (val < 0) continue;
            acc  = (acc << 5) | val;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((acc >> bits) & 0xFF));
            }
        }

        return [.. output];
    }
}
