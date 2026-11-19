using System.Security.Cryptography;

namespace GTS.Billing.Application;

internal static class RecoverySecrets
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    // 32 uniformly random symbols from a 32-symbol alphabet = 160 bits.
    internal static string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[39];
        var j = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0 && i % 4 == 0) chars[j++] = '-';
            chars[j++] = Alphabet[bytes[i] & 31];
        }
        CryptographicOperations.ZeroMemory(bytes);
        return new string(chars);
    }

    internal static string Hash(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var digest = Digest(code, salt);
        try { return $"rc1:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(digest)}"; }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    internal static bool Verify(string submitted, string encoded)
    {
        if (submitted.Length > 64) return false;
        var code = submitted.Trim().ToUpperInvariant();
        if (code.Length != 39) return false;
        for (var i = 0; i < code.Length; i++)
            if (i % 5 == 4 ? code[i] != '-' : !Alphabet.Contains(code[i])) return false;
        var parts = encoded.Split(':');
        if (parts.Length != 3 || parts[0] != "rc1" || parts[1].Length != 24 || parts[2].Length != 44) return false;
        Span<byte> salt = stackalloc byte[16];
        Span<byte> expected = stackalloc byte[32];
        if (!Convert.TryFromBase64String(parts[1], salt, out var s) || s != 16 ||
            !Convert.TryFromBase64String(parts[2], expected, out var n) || n != 32) return false;
        var digest = Digest(code, salt);
        try { return CryptographicOperations.FixedTimeEquals(digest, expected); }
        finally { CryptographicOperations.ZeroMemory(digest); }
    }

    private static byte[] Digest(string code, ReadOnlySpan<byte> salt)
    {
        Span<byte> data = stackalloc byte[55];
        salt.CopyTo(data);
        System.Text.Encoding.ASCII.GetBytes(code.AsSpan(), data[16..]);
        try { return SHA256.HashData(data); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
}
