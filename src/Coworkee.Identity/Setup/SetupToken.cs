using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Coworkee.Identity.Setup;

public sealed class SetupToken
{
    public SetupToken(IConfiguration configuration)
    {
        var configured = configuration["Coworkee:SetupToken"];
        IsGenerated = string.IsNullOrWhiteSpace(configured);
        Value = IsGenerated ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_') : configured!;
    }

    internal string Value { get; }

    internal bool IsGenerated { get; }

    public bool Matches(string candidate) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Value), Encoding.UTF8.GetBytes(candidate ?? string.Empty));
}
