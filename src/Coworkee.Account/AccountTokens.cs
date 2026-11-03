using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Coworkee.Account;

public static class AccountTokens
{
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? Decode(string? code)
    {
        try
        {
            return code is null ? null : Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
