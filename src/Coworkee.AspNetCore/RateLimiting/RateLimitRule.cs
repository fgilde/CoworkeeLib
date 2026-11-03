namespace Coworkee.AspNetCore.RateLimiting;

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; } = 100;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}
