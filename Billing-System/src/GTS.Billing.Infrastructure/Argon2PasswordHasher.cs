using System.Security.Cryptography;
using System.Text;
using GTS.Billing.Application;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace GTS.Billing.Infrastructure;

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const string Profile = "$argon2id$v=19$m=65536,t=3,p=1$";
    private const string OlderProfile = "$argon2id$v=19$m=19456,t=2,p=1$";

    public string Hash(string password)
    {
        if (password.Length is 0 or > 512) throw new ArgumentException("Password length is out of bounds.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Derive(password, salt, 65536, 3);
        try { return Profile + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash); }
        finally { CryptographicOperations.ZeroMemory(hash); }
    }

    public bool Verify(string password, string encoded)
    {
        if (password.Length is 0 or > 512 || encoded.Length > 128) return false;
        var current = encoded.StartsWith(Profile, StringComparison.Ordinal);
        if (!current && !encoded.StartsWith(OlderProfile, StringComparison.Ordinal)) return false;
        var payload = encoded.AsSpan((current ? Profile : OlderProfile).Length);
        if (payload.Length != 69 || payload[24] != '$') return false;
        var salt = new byte[16];
        Span<byte> target = stackalloc byte[32];
        if (!Convert.TryFromBase64Chars(payload[..24], salt, out var s) || s != 16 ||
            !Convert.TryFromBase64Chars(payload[25..], target, out var t) || t != 32) return false;
        var actual = Derive(password, salt, current ? 65536 : 19456, current ? 3 : 2);
        try { return CryptographicOperations.FixedTimeEquals(actual, target); }
        finally { CryptographicOperations.ZeroMemory(actual); CryptographicOperations.ZeroMemory(target); }
    }   

    public bool NeedsUpgrade(string encoded) => !encoded.StartsWith(Profile, StringComparison.Ordinal);

    private static byte[] Derive(string password, byte[] salt, int memory, int iterations)
    {
        var input = Encoding.UTF8.GetBytes(password);
        var result = new byte[32];
        try
        {
            var generator = new Argon2BytesGenerator();
            generator.Init(new Argon2Parameters.Builder(Argon2Parameters.Argon2id).WithVersion(Argon2Parameters.Version13)
                .WithMemoryAsKB(memory).WithIterations(iterations).WithParallelism(1).WithSalt(salt).Build());
            generator.GenerateBytes(input, result);
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(input); }
    }
}
