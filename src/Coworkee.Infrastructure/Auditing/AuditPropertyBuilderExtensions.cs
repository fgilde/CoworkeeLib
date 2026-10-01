using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Coworkee.Infrastructure.Auditing;

public static class AuditPropertyBuilderExtensions
{
    internal const string Sensitive = "Coworkee:Sensitive";
    internal const string NotAudited = "Coworkee:NotAudited";

    public static PropertyBuilder<T> IsSensitive<T>(this PropertyBuilder<T> builder) => builder.HasAnnotation(Sensitive, true);

    public static PropertyBuilder<T> IsNotAudited<T>(this PropertyBuilder<T> builder) => builder.HasAnnotation(NotAudited, true);

    public static void IsNotAudited(this IMutableEntityType entityType) => entityType.SetAnnotation(NotAudited, true);

    internal static bool HasFlag(this IReadOnlyProperty property, string annotation) => property.FindAnnotation(annotation)?.Value is true;
}
