using System.Globalization;
using Microsoft.AspNetCore.Localization;
using System.Text.Json.Serialization;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.Security;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Coworkee.AspNetCore;

public static class CoworkeeWebApplicationExtensions
{
    /// <summary>OpenAPI document and Swagger UI outside Development (they describe every endpoint, so production keeps them off by default).</summary>
    public const string OpenApiSetting = "Coworkee:OpenApi:Enabled";

    public static WebApplicationBuilder AddCoworkee<TRoot>(this WebApplicationBuilder builder)
        where TRoot : CoworkeeModule
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddCoworkeeModules<TRoot>(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<CoworkeeExceptionHandler>();
        builder.Services.AddOpenApi("v1");
        return builder;
    }

    /// <summary>Messages (validation and others) follow Accept-Language; number and date formats stay English.</summary>
    private static readonly RequestLocalizationOptions RequestCultures = new()
    {
        DefaultRequestCulture = new RequestCulture("en"),
        SupportedCultures = [new CultureInfo("en")],
        SupportedUICultures = CultureInfo.GetCultures(CultureTypes.AllCultures).Where(c => c.Name.Length > 0).ToList(),
    };

    public static WebApplication UseCoworkee(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseRequestLocalization(RequestCultures);
        foreach (var module in app.Services.GetRequiredService<IReadOnlyList<CoworkeeModule>>().OfType<IWebModule>())
        {
            module.ConfigureApplication(app);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>(OpenApiSetting))
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
        }

        return app;
    }

    /// <summary>For worker hosts (background jobs only): the modules' services run, but no module endpoints, dashboards or OpenAPI are mapped.</summary>
    public static WebApplication UseCoworkeeWorker(this WebApplication app)
    {
        app.UseExceptionHandler();
        return app;
    }
}
