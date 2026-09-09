using System.Security.Cryptography;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Izigo.Infrastructure.Services;

// PBKDF2-SHA512 with per-password salt. Iteration count is stored in the hash string
// so old hashes remain verifiable even when the config value changes.
public class PasswordHasher(IConfiguration config) : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

    private int Iterations =>
        int.TryParse(config["Security:Pbkdf2Iterations"], out var i) ? i : 100_000;

    public string Hash(string password)
    {
        var iterations = Iterations;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, HashSize);
        return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string storedHash)
    {
        try
        {
            var parts = storedHash.Split('.');
            if (parts.Length != 3) return false;

            var iterations = int.Parse(parts[0]);
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
