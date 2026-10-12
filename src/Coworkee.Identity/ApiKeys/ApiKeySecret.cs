using System.Security.Cryptography;
using System.Text;
using Coworkee.Contracts.Identity;

namespace Coworkee.Identity.ApiKeys;

/// <summary>cwk_ and 256 random bits; the hash finds the key, a secret this long needs no salt or slow hash.</summary>
internal static class ApiKeySecret
{
    public static (string Secret, string Prefix, string Hash) Create()
    {
        var secret = ApiKeyDefaults.Prefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        return (secret, secret[..(ApiKeyDefaults.Prefix.Length + 6)], Hash(secret));
    }

    public static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    public static bool Matches(string hash, string stored) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(stored));
}
