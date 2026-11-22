using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coworkee.Identity.Users;

/// <summary>
/// Tells the automatic rehash at sign-in apart from a password change. Identity verifies an old hash (an older format or fewer iterations, as hashes
/// imported from another system often are), gets <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> and stores a new hash of the same
/// password through the same store call as a change. A rehash keeps the password, so it keeps what a change resets: the forced change, the time of
/// the last change, the history and the sessions (<see cref="CoworkeeUserStore"/>), and it is no "password changed" security event.
/// </summary>
internal static class PasswordRehash
{
    private sealed class State
    {
        public byte[]? Verified;
        public string? Hash;
        public bool StampKept;
    }

    private static readonly ConditionalWeakTable<User, State> States = new();

    /// <summary>The password verified against an old hash; a hash of the same password that follows is the rehash.</summary>
    public static void Verified(User user, string password) => States.AddOrUpdate(user, new State { Verified = Digest(password) });

    public static void Hashed(User user, string password, string hash)
    {
        if (States.TryGetValue(user, out var state) && state.Verified is { } verified && CryptographicOperations.FixedTimeEquals(verified, Digest(password)))
        {
            state.Hash = hash;
            state.Verified = null;
        }
    }

    /// <summary>Whether <paramref name="passwordHash"/> is the rehash of the password just verified; anything else ends the rehash state.</summary>
    public static bool IsRehash(User user, string? passwordHash)
    {
        if (States.TryGetValue(user, out var state) && state.Hash is not null && state.Hash == passwordHash)
        {
            return true;
        }

        States.Remove(user);
        return false;
    }

    /// <summary>The security stamp stays once for a rehash: Identity renews it right after storing the new hash.</summary>
    public static bool KeepStamp(User user)
    {
        if (States.TryGetValue(user, out var state) && state.Hash is not null && !state.StampKept)
        {
            state.StampKept = true;
            return true;
        }

        return false;
    }

    /// <summary>Whether the user's current hash came from a rehash in this scope (no "password changed" event).</summary>
    public static bool Rehashed(User user) => States.TryGetValue(user, out var state) && state.Hash is not null && state.Hash == user.PasswordHash;

    private static byte[] Digest(string password) => SHA256.HashData(Encoding.UTF8.GetBytes(password));
}

/// <summary>Identity's hasher, telling <see cref="PasswordRehash"/> about a verification that asks for a rehash and the hash that follows it.</summary>
internal sealed class RehashAwarePasswordHasher(IOptions<PasswordHasherOptions> options) : IPasswordHasher<User>
{
    private readonly PasswordHasher<User> _inner = new(options);

    public string HashPassword(User user, string password)
    {
        var hash = _inner.HashPassword(user, password);
        PasswordRehash.Hashed(user, password, hash);
        return hash;
    }

    public PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
    {
        var result = _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            PasswordRehash.Verified(user, providedPassword);
        }

        return result;
    }
}
