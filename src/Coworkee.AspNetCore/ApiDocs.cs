using Coworkee.Application.Authorization;
using Coworkee.Contracts.ApiDocs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Coworkee.AspNetCore;

/// <summary>Swagger UI for users with <see cref="ApiDocsPermissions.View"/>; the OpenAPI document itself stays anonymous for SDK generators.</summary>
internal static class ApiDocs
{
    private const string Scheme = "Bearer";

    // through the BFF the document names the API host as server: requests are sent to the page origin, with the BFF's CSRF header
    // one line without backslashes: Swashbuckle puts it into a JSON string
    private const string RequestInterceptor =
        "request => { const url = new URL(request.url, location.href); request.url = location.origin + url.pathname + url.search; request.headers['X-CSRF'] = '1'; return request; }";

    private const string Theme = """
        <style>
          html.dark { filter: invert(88%) hue-rotate(180deg); background: #fff; }
          html.dark img { filter: invert(100%) hue-rotate(180deg); }
          #theme-toggle { position: fixed; top: 14px; right: 16px; z-index: 10; width: 32px; height: 32px; border: 0; border-radius: 50%; font-size: 18px; cursor: pointer; background: #fff; }
        </style>
        <script>
          (() => {
            const key = 'swagger-dark', root = document.documentElement;
            const apply = dark => root.classList.toggle('dark', dark);
            const stored = localStorage.getItem(key);
            apply(stored ? stored === '1' : matchMedia('(prefers-color-scheme: dark)').matches);
            addEventListener('DOMContentLoaded', () => {
              const button = Object.assign(document.createElement('button'), { id: 'theme-toggle', title: 'Dark mode', textContent: '◐' });
              button.onclick = () => localStorage.setItem(key, apply(!root.classList.contains('dark')) ? '1' : '0');
              document.body.append(button);
            });
          })();
        </script>
        """;

    public static void AddBearerScheme(OpenApiOptions options) => options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[Scheme] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(Scheme, document)] = [] }];
        return Task.CompletedTask;
    });

    public static void UseSwaggerUi(WebApplication app)
    {
        app.UseWhen(context => context.Request.Path.StartsWithSegments("/swagger"), swagger => swagger.Use(RequireViewAsync));
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "v1");
            options.DocumentTitle = app.Environment.ApplicationName;
            options.HeadContent = Theme;
            options.UseRequestInterceptor(RequestInterceptor);
        });
    }

    private static async Task RequireViewAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!await context.RequestServices.GetRequiredService<IPermissionChecker>().IsGrantedAsync(ApiDocsPermissions.View, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }
}

internal sealed class ApiDocsPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(ApiDocsPermissions.GroupName, "API documentation").Add(ApiDocsPermissions.View, "View API documentation");
}
