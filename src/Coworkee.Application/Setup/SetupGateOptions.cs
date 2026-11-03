namespace Coworkee.Application.Setup;

/// <summary>API paths that answer before setup; modules add theirs (e.g. what the setup page needs).</summary>
public sealed class SetupGateOptions
{
    public List<string> AllowedPrefixes { get; } = ["/api/v1/setup"];
}
