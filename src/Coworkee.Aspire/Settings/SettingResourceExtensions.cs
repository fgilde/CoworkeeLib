using System.Globalization;
using System.Linq.Expressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Coworkee.Aspire.Settings;

/// <summary>Sets configuration of a service by path, e.g. <c>api.WithSetting(s =&gt; s.Coworkee.ApiAuth.Authority, auth.GetEndpoint("https"))</c>.</summary>
public static class SettingResourceExtensions
{
    public static IResourceBuilder<T> WithSetting<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, string value)
        where T : IResourceWithEnvironment => builder.WithEnvironment(SettingPath.Of(path), value);

    public static IResourceBuilder<T> WithSetting<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, bool value)
        where T : IResourceWithEnvironment => builder.WithEnvironment(SettingPath.Of(path), value ? "true" : "false");

    public static IResourceBuilder<T> WithSetting<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, int value)
        where T : IResourceWithEnvironment => builder.WithEnvironment(SettingPath.Of(path), value.ToString(CultureInfo.InvariantCulture));

    public static IResourceBuilder<T> WithSetting<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, EndpointReference value)
        where T : IResourceWithEnvironment => builder.WithEnvironment(SettingPath.Of(path), value);

    public static IResourceBuilder<T> WithSetting<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, ReferenceExpression value)
        where T : IResourceWithEnvironment => builder.WithEnvironment(SettingPath.Of(path), value);

    public static IResourceBuilder<T> WithSetting<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, IResourceBuilder<ParameterResource> value)
        where T : IResourceWithEnvironment => builder.WithEnvironment(SettingPath.Of(path), value);

    /// <summary>Sets a list setting item by item (Scopes__0, Scopes__1, ...).</summary>
    public static IResourceBuilder<T> WithSettings<T>(this IResourceBuilder<T> builder, Expression<Func<CoworkeeSettings, object?>> path, params string[] values)
        where T : IResourceWithEnvironment
    {
        var prefix = SettingPath.Of(path);
        for (var i = 0; i < values.Length; i++)
        {
            builder.WithEnvironment($"{prefix}__{i}", values[i]);
        }

        return builder;
    }
}
