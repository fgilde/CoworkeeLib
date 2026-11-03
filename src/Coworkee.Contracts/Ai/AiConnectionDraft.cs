namespace Coworkee.Contracts.Ai;

/// <summary>
/// A connection as the AI page edits it, before it is saved. <see cref="ApiKey"/> null takes the stored key while provider and endpoint stay
/// the same, empty sends none. <see cref="Headers"/> are lines "Name: value"; <see cref="AuthHeader"/> sends the key in that header instead of a bearer token.
/// </summary>
public sealed record AiConnectionDraft(
    string Provider, string? Model = null, string? Endpoint = null, string? ApiKey = null, string? Headers = null, string? AuthHeader = null);
