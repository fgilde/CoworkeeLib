using System.Text.Json;

namespace Coworkee.Contracts.Realtime;

public sealed record RealtimeEnvelope(string Topic, string Type, JsonElement Payload);
