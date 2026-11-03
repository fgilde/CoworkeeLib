using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Social;

public static class SocialServiceCollectionExtensions
{
    /// <summary>Opts entity types in, e.g. <c>services.AddCoworkeeSocial(s => s.Comments&lt;Product&gt;("Products").Tags&lt;Product&gt;("Products"))</c>; may be called by several modules.</summary>
    public static IServiceCollection AddCoworkeeSocial(this IServiceCollection services, Action<SocialOptions> configure)
    {
        if (services.FirstOrDefault(d => d.ServiceType == typeof(SocialOptions))?.ImplementationInstance is not SocialOptions options)
        {
            options = new SocialOptions();
            services.AddSingleton(options);
        }

        configure(options);
        return services;
    }
}
