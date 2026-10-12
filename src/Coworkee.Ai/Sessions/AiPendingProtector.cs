using System.Security.Cryptography;
using System.Text.Json;
using Coworkee.Contracts.Ai;
using Microsoft.AspNetCore.DataProtection;

namespace Coworkee.Ai.Sessions;

/// <summary>What a waiting message shows (redacted inputs) and its calls with their full arguments, encrypted with Data Protection.</summary>
internal sealed record AiPendingState(IReadOnlyList<AiPendingToolDto> Tools, string Calls);

/// <summary>
/// Keeps the arguments of tool calls that wait for the user out of the database in clear text.
/// ponytail: the API's Data Protection keys must be shared between instances (and survive restarts) or waiting calls expire.
/// </summary>
internal sealed class AiPendingProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Coworkee.Ai.PendingToolCalls.v1");

    public string Create(IReadOnlyList<AiPendingCall> calls) => Serialize(new AiPendingState(
        [.. calls.Where(c => c.Output is null).Select(c => new AiPendingToolDto(c.Tool, c.Input, c.Destructive))],
        _protector.Protect(JsonSerializer.Serialize(calls, JsonSerializerOptions.Web))));

    public string Update(string pending, IReadOnlyList<AiPendingCall> calls) =>
        Serialize(Read(pending) with { Calls = _protector.Protect(JsonSerializer.Serialize(calls, JsonSerializerOptions.Web)) });

    /// <summary>The calls, or null when they can no longer be read (Data Protection keys gone).</summary>
    public List<AiPendingCall>? Calls(string pending)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AiPendingCall>>(_protector.Unprotect(Read(pending).Calls), JsonSerializerOptions.Web);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static AiPendingState Read(string pending) => JsonSerializer.Deserialize<AiPendingState>(pending, JsonSerializerOptions.Web)!;

    private static string Serialize(AiPendingState state) => JsonSerializer.Serialize(state, JsonSerializerOptions.Web);
}
