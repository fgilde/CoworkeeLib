using System.Security.Claims;
using Coworkee.Application.Authorization;
using Coworkee.Core.Modularity;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.SourceGenerators.Tests.Sample;

internal sealed class TodoModule : CoworkeeModule
{
    public const string UserHeader = "X-Test-User";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddSingleton<TodoStore>();
        context.Services.AddSingleton<TestPermissions>();
        context.Services.AddSingleton<IPermissionChecker>(sp => sp.GetRequiredService<TestPermissions>());
        context.Services.AddScoped<IValidator<SaveTodo>, SaveTodoValidator>();
    }

    /// <summary>Signs the request in when it carries <see cref="UserHeader"/>.</summary>
    public static void UseTestUser(WebApplication app) => app.Use((context, next) =>
    {
        if (context.Request.Headers.TryGetValue(UserHeader, out var user))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "Test"));
        }

        return next(context);
    });
}

internal sealed class TestPermissions : IPermissionChecker
{
    public HashSet<string> Granted { get; } = [];

    public Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>(Granted);

    public Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken) => Task.FromResult(Granted.Contains(permission));

    public Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken) => IsGrantedAsync(permission, cancellationToken);

    public Task<IReadOnlyCollection<Guid>> GetGrantedResourcesAsync(string permission, string resourceType, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<Guid>>([]);
}
