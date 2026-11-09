namespace Coworkee.Contracts.Auditing;

public static class AuditPermissions
{
    public const string GroupName = "Audit";
    public const string View = "Audit.View";

    /// <summary>Sign-ins, sign-outs, lockouts and changes of credentials and access.</summary>
    public const string SecurityLog = "Audit.SecurityLog";

    /// <summary>The request log without bodies.</summary>
    public const string Requests = "Audit.Requests";

    /// <summary>Request and response bodies of the request log; they may hold personal data. Implies <see cref="Requests"/>.</summary>
    public const string RequestBodies = "Audit.Requests.Bodies";
}
