using System.Text.Json.Serialization;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.Security;
using Coworkee.Core.Modularity;
using Coworkee.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.AspNetCore;

public static class CoworkeeWebApplicationExtensions
{
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

    public static WebApplication UseCoworkee(this WebApplication app)
    {
        app.UseExceptionHandler();
        foreach (var module in app.Services.GetRequiredService<IReadOnlyList<CoworkeeModule>>().OfType<IWebModule>())
        {
            module.ConfigureApplication(app);
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapOpenApi();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "v1"));
        return app;
    }
}
