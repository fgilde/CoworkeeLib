namespace Coworkee.Contracts.Configuration;

public enum LoginMode
{
    /// <summary>Email and password only.</summary>
    Internal,

    /// <summary>Only the external providers, e.g. Keycloak; the password form is hidden.</summary>
    External,

    /// <summary>Password form plus a button per external provider.</summary>
    Both,
}
