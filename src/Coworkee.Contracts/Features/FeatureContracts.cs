using System.Text.Json.Serialization;

namespace Coworkee.Contracts.Features;

[JsonConverter(typeof(JsonStringEnumConverter<FeatureType>))]
public enum FeatureType
{
    Bool,
    Int,
    String,
}

public static class FeaturePermissions
{
    public const string GroupName = "Features";
    public const string Tenants = "Features.Tenants";
    public const string Editions = "Features.Editions";
}

public static class FeatureTopics
{
    /// <summary>Raised whenever an edition or the features of a tenant change; clients reload their features.</summary>
    public const string Changed = "global:features";
}

public sealed record FeatureDefinitionDto(string Name, string DisplayName, string? Description, FeatureType Type, string? DefaultValue);

public sealed record FeatureGroupDto(string Name, string DisplayName, IReadOnlyList<FeatureDefinitionDto> Features);

public sealed record EditionDto(Guid Id, string Name, string? Description, IReadOnlyDictionary<string, string> Values, int TenantCount);

public sealed record EditionRequest(string Name, string? Description, IReadOnlyDictionary<string, string>? Values);

public sealed record TenantRequest(string Name, string Identifier, bool IsActive, bool AcceptsRegistrations);

/// <summary>A new tenant; with an admin e-mail and password its first administrator is created as well.</summary>
public sealed record CreateTenantRequest(string Name, string Identifier, bool IsActive, bool AcceptsRegistrations, string? AdminEmail, string? AdminPassword);

public sealed record TenantDetailsDto(Guid TenantId, Guid? EditionId, IReadOnlyDictionary<string, string> Overrides, int UserCount);

public sealed record TenantFeaturesRequest(Guid? EditionId, IReadOnlyDictionary<string, string>? Overrides);
