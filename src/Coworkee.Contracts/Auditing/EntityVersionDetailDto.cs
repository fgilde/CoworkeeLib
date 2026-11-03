using System.Text.Json;

namespace Coworkee.Contracts.Auditing;

public sealed record EntityVersionDetailDto(int Revision, JsonElement Payload, bool IsDeleted);
