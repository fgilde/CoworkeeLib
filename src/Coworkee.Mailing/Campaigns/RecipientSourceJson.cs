using System.Text.Json;
using Coworkee.Contracts.Mailing;

namespace Coworkee.Mailing;

internal static class RecipientSourceJson
{
    public static string Write(RecipientSourceDto source) => JsonSerializer.Serialize(source, JsonSerializerOptions.Web);

    public static RecipientSourceDto Read(string json) => JsonSerializer.Deserialize<RecipientSourceDto>(json, JsonSerializerOptions.Web)!;
}
