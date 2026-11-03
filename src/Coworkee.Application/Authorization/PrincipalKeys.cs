namespace Coworkee.Application.Authorization;

public static class PrincipalKeys
{
    public static string User(Guid id) => "u:" + id.ToString("N");

    public static string Group(Guid id) => "g:" + id.ToString("N");
}
