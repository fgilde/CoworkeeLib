namespace Aspire.Hosting;

/// <summary>User name (or email) and password of a sign-in.</summary>
public sealed record CoworkeeLogin(string UserName, string Password);
