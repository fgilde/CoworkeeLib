using Coworkee.Contracts.Configuration;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Application.Setup;
using Coworkee.Identity.Setup;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application;

namespace MyApp.Infrastructure;

[DependsOn(typeof(MyAppApplicationModule), typeof(CoworkeeIdentityModule))]
public sealed class MyAppInfrastructureModule : CoworkeeModule
{
    public const string ConnectionStringName = "myapp";

#if (database == "sqlServer")
    public const string DatabaseProvider = DatabaseProviders.SqlServer;
#elif (database == "sqlite")
    public const string DatabaseProvider = DatabaseProviders.Sqlite;
#else
    public const string DatabaseProvider = DatabaseProviders.Postgres;
#endif

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddCoworkeeDbContext<MyAppDbContext>((provider, options) =>
            options.UseCoworkeeDatabase(provider.GetRequiredService<IConfiguration>(), ConnectionStringName));
        context.Services.AddHealthChecks().AddDbContextCheck<MyAppDbContext>("database");
        context.Services.Configure<SetupGateOptions>(options => options.AllowedPrefixes.Add("/api/v1/system"));
        context.Services.PostConfigure<Coworkee.Contracts.Configuration.BackgroundJobOptions>(options => options.ConnectionStringName = ConnectionStringName);
#if (customTheme)
        context.Services.Configure<Coworkee.Theming.ThemingOptions>(themes =>
        {
#if (brandColors)
            themes.DefaultTheme = "COWORKEE_APP_TITLE";
            themes.Themes.Add(new Coworkee.Theming.AppTheme("COWORKEE_APP_TITLE", "COWORKEE_THEME", new Dictionary<string, string>
            {
#if (hasPrimary)
                ["Primary"] = "COWORKEE_PRIMARY",
#endif
#if (hasSecondary)
                ["Secondary"] = "COWORKEE_SECONDARY",
#endif
            }));
#else
            themes.DefaultTheme = "COWORKEE_THEME";
#endif
        });
#endif
    }
}
