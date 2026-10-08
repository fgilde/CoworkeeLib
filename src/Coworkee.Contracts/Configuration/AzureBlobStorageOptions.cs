namespace Coworkee.Contracts.Configuration;

public sealed class AzureBlobStorageOptions
{
    public string ConnectionString { get; set; } = "UseDevelopmentStorage=true";

    public string Container { get; set; } = "coworkee";
}
