namespace Coworkee.Contracts.Configuration;

/// <summary>A PKCS#12 certificate: a file (<see cref="Path"/>) or its content as Base64 (<see cref="Base64"/>, e.g. from a secret environment variable).</summary>
public sealed class CertificateOptions
{
    public string? Path { get; set; }

    public string? Base64 { get; set; }

    public string? Password { get; set; }
}
