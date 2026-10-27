using System.Security.Cryptography;
using SubscriptionBilling.Application.Abstractions;

namespace SubscriptionBilling.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512 with a random salt per password. Stored as <c>v1$iterations$salt$hash</c> so the cost can be raised
/// later without breaking existing hashes. 210,000 iterations is the OWASP recommendation for PBKDF2-HMAC-SHA512.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Version = "v1";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000;
    private const int MinIterations = 100_000;
    private const int MaxIterations = 5_000_000;

    // A well-formed hash of a throwaway password. Verifying against it spends the same time as a real check.
    private static readonly Lazy<string> DummyHash = new(() => HashCore("dummy-password-for-timing-only", DefaultIterations));

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return HashCore(password, DefaultIterations);
    }

    public bool Verify(string? hash, string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var known = hash is not null;
        var parts = (hash ?? DummyHash.Value).Split('$');
        if (parts.Length != 4
            || parts[0] != Version
            || !int.TryParse(parts[1], out var iterations)
            || iterations is < MinIterations or > MaxIterations)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected) && known;
    }

    private static string HashCore(string password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, HashBytes);
        return string.Join('$', Version, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }
}
