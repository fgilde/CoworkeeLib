using Aspire.Hosting.ApplicationModel;
using Coworkee.Aspire.Modules;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Aspire.Hosting;

public sealed partial class CoworkeeApp
{
    public const string RerunMigrationsCommand = "coworkee-rerun-migrations";
    public const string ResetDatabaseCommand = "coworkee-reset-database";

    private void AddDatabaseCommands(IResourceBuilder<ProjectResource> migrations)
    {
        if (!LocalDevelopment)
        {
            return;
        }

        migrations.WithCommand(RerunMigrationsCommand, "Re-run migrations", context => RunAgainAsync(context, migrations.Resource),
            new CommandOptions { Description = "Runs the migrations and the seed of the demo data again.", IconName = "ArrowClockwise" });
        if (Options.Database is null)
        {
            migrations.WithCommand(ResetDatabaseCommand, "Reset database", context => ResetDatabaseAsync(context, migrations.Resource),
                new CommandOptions { ConfirmationMessage = $"Drop the database '{Name}' with all data and create it again?", IconName = "DatabaseArrowDown" });
        }
    }

    private void AddWebUrls(IResourceBuilder<ProjectResource> web)
    {
        web.WithUrlForEndpoint("https", e => new ResourceUrlAnnotation { Url = $"{e.Url}/swagger", DisplayText = "Swagger" });
        if (_apis.Any(a => CoworkeeModules.Of(a.Resource).Contains(CoworkeeModules.BackgroundJobs)))
        {
            web.WithUrlForEndpoint("https", e => new ResourceUrlAnnotation { Url = $"{e.Url}/admin/jobs", DisplayText = "Jobs" });
        }
    }

    private async Task<ExecuteCommandResult> ResetDatabaseAsync(ExecuteCommandContext context, ProjectResource migrations)
    {
        var server = await Server.Resource.ConnectionStringExpression.GetValueAsync(context.CancellationToken);
        await using (var connection = new NpgsqlConnection(server))
        {
            await connection.OpenAsync(context.CancellationToken);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Database.Resource.DatabaseName.Replace("\"", "\"\"", StringComparison.Ordinal)}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(context.CancellationToken);
        }

        return await RunAgainAsync(context, migrations);
    }

    private static async Task<ExecuteCommandResult> RunAgainAsync(ExecuteCommandContext context, ProjectResource migrations)
    {
        var commands = context.Services.GetRequiredService<ResourceCommandService>();
        var started = await commands.ExecuteCommandAsync(migrations, KnownResourceCommands.StartCommand, context.CancellationToken);
        return started.Success ? started : await commands.ExecuteCommandAsync(migrations, KnownResourceCommands.RestartCommand, context.CancellationToken);
    }
}
