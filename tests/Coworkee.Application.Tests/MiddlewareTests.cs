using Coworkee.Application.Messaging;
using Coworkee.Core.Modularity;
using Coworkee.Core.Results;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Coworkee.Application.Tests;

public sealed class MiddlewareTests
{
    [Fact]
    public async Task Invalid_result_request_returns_validation_failure()
    {
        var (dispatcher, _) = Build();

        var result = await dispatcher.SendAsync(new Rename(""), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Details!.ShouldContainKey(nameof(Rename.Name));
    }

    [Fact]
    public async Task Invalid_plain_request_throws_validation_exception()
    {
        var (dispatcher, _) = Build();

        await Should.ThrowAsync<ValidationException>(() => dispatcher.SendAsync(new Shout(""), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Successful_command_saves_unit_of_work()
    {
        var (dispatcher, unitOfWork) = Build();

        await dispatcher.SendAsync(new Rename("ok"), TestContext.Current.CancellationToken);

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Failed_command_and_queries_do_not_save()
    {
        var (dispatcher, unitOfWork) = Build();

        await dispatcher.SendAsync(new Rename("fail"), TestContext.Current.CancellationToken);
        await dispatcher.SendAsync(new Shout("query"), TestContext.Current.CancellationToken);

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Works_without_unit_of_work_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();

        var result = await provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync(new Rename("ok"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    private static (IDispatcher Dispatcher, IUnitOfWork UnitOfWork) Build()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCoworkeeModules<TestModule>(new ConfigurationBuilder().Build());
        services.AddSingleton(unitOfWork);
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<IDispatcher>(), unitOfWork);
    }

    [DependsOn(typeof(CoworkeeApplicationModule))]
    private sealed class TestModule : CoworkeeModule
    {
        public override void ConfigureServices(ModuleServiceContext context) =>
            context.Services.AddMessagingFromAssembly(typeof(MiddlewareTests).Assembly);
    }

    public sealed record Rename(string Name) : ICommand<Result>;

    public sealed record Shout(string Text) : IQuery<string>;

    internal sealed class RenameValidator : AbstractValidator<Rename>
    {
        public RenameValidator() => RuleFor(r => r.Name).NotEmpty();
    }

    internal sealed class ShoutValidator : AbstractValidator<Shout>
    {
        public ShoutValidator() => RuleFor(r => r.Text).NotEmpty();
    }

    internal sealed class RenameHandler : IHandler<Rename, Result>
    {
        public Task<Result> HandleAsync(Rename request, CancellationToken cancellationToken) =>
            Task.FromResult(request.Name == "fail" ? Result.Failure(Error.Conflict("rename.failed", "Failed")) : Result.Success());
    }

    internal sealed class ShoutHandler : IHandler<Shout, string>
    {
        public Task<string> HandleAsync(Shout request, CancellationToken cancellationToken) => Task.FromResult(request.Text.ToUpperInvariant());
    }
}
