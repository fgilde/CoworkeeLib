namespace Coworkee.Contracts.Configuration;

public sealed class CertificateOptions
{
    public required string Path { get; set; }

    public string? Password { get; set; }
}
