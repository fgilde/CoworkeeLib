using Coworkee.Cli.Spec;

namespace Coworkee.Cli.Generation;

/// <summary>Per entity: a list page with create, edit and delete, its API client and a navigation entry in the group of the module.</summary>
internal static class ClientFiles
{
    public static IEnumerable<GeneratedFile> Entity(EntityModel e)
    {
        var client = $"src/{e.App}.Web.Client";
        yield return new($"{client}/Pages/{e.Module}/{e.Plural}Page.razor", Page(e));
        yield return new($"{client}/Pages/{e.Module}/{e.Plural}Page.razor.cs", CodeBehind(e));
        yield return new($"{client}/Api/I{e.Plural}Api.cs", $$"""
            using {{e.App}}.Contracts.{{e.Module}};

            namespace {{e.App}}.Web.Client.Api;

            public interface I{{e.Plural}}Api
            {
                Task SaveAsync(Guid? id, AddEdit{{e.Name}}Request request, CancellationToken cancellationToken = default);

                Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
            }

            """);
        yield return new($"{client}/Api/{e.Plural}Api.cs", $$"""
            using Coworkee.Client.Blazor.Api;
            using {{e.App}}.Contracts;
            using {{e.App}}.Contracts.{{e.Module}};

            namespace {{e.App}}.Web.Client.Api;

            internal sealed class {{e.Plural}}Api(HttpClient http) : ApiClientBase(http), I{{e.Plural}}Api
            {
                private const string Resource = "{{e.ApiRoute}}";

                public Task SaveAsync(Guid? id, AddEdit{{e.Name}}Request request, CancellationToken cancellationToken = default) =>
                    id is { } existing ? SendAsync(HttpMethod.Put, $"{Resource}/{existing}", request, cancellationToken) : SendAsync(HttpMethod.Post, Resource, request, cancellationToken);

                public Task DeleteAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default) =>
                    SendAsync(HttpMethod.Post, $"{Resource}/delete", new IdsRequest(ids), cancellationToken);
            }

            """);
        yield return new($"{client}/Navigation/{e.Plural}Navigation.cs", $$"""
            using Coworkee.Client.Blazor;
            using MudBlazor;
            using {{e.App}}.Contracts.{{e.Module}};

            namespace {{e.App}}.Web.Client.Navigation;

            internal sealed class {{e.Plural}}Navigation : INavigationContributor
            {
                public IEnumerable<CoworkeeNavItem> Items => [new("{{e.Plural}}", "{{e.Route}}", Icons.Material.Outlined.TableRows, {{e.Permissions}}.View, Group: "{{e.Module}}")];
            }

            """);
    }

    /// <summary>The lines Program.cs of the web client needs for an entity.</summary>
    public static string Registration(EntityModel e) => $"""
        builder.Services.AddHttpClient<{e.App}.Web.Client.Api.I{e.Plural}Api, {e.App}.Web.Client.Api.{e.Plural}Api>(client => client.BaseAddress = baseAddress);
        builder.Services.AddSingleton<INavigationContributor, {e.App}.Web.Client.Navigation.{e.Plural}Navigation>();

        """.ReplaceLineEndings("\n");

    private static string Page(EntityModel e) => $$"""
        @page "{{e.Route}}"
        @using {{e.App}}.Contracts.{{e.Module}}
        {{(e.Spec.ClientEntity ? "@using Coworkee.Client.Blazor.ClientEntities\n" : string.Empty)}}@attribute [Authorize(Policy = PermissionPolicy.Prefix + {{e.Permissions}}.View)]

        <PageHeader Title="@L["{{e.Plural}}"]" Description="@L["Manage {{e.PluralWords}}."]" />
        <RealtimeSubscription Topic="type:{{e.Name}}" OnEvent="_ => _table.ReloadAsync()" />
        <CoworkeeDataTable @ref="_table" T="{{e.Name}}Dto" EntitySet="{{e.Plural}}" SearchFields="SearchFields" MultiSelection="true" ExportFileName="{{e.PluralWords.Replace(' ', '-')}}" DescribeItem="e => {{e.Describe}}"
                           CreateText="@L["New {{e.Words}}"]" CreatePermission="@{{e.Permissions}}.Create" EditPermission="@{{e.Permissions}}.Edit" DeletePermission="@{{e.Permissions}}.Delete"
                           OnCreate="CreateAsync" OnEdit="EditAsync" OnDelete="DeleteAsync">
        {{(e.Spec.ClientEntity ? $"    <ToolBarContent>\n        <ClientEntityIndicator EntitySet=\"{e.Plural}\" DataChanged=\"() => _table.ReloadAsync()\" />\n    </ToolBarContent>\n" : string.Empty)}}    <Columns>
        {{string.Concat(e.Properties.Select(p => $"        <PropertyColumn Property=\"e => e.{p.Name}\" Title=\"@L[\"{EntityModel.Sentence(p.Name)}\"]\" />\n"))}}    </Columns>
        </CoworkeeDataTable>

        """;

    private static string CodeBehind(EntityModel e) => $$"""
        using Coworkee.Client.Blazor.Api;
        using Coworkee.Client.Blazor.Components.Data;
        using Coworkee.Client.Blazor.Localization;
        using Microsoft.AspNetCore.Components;
        using MudBlazor;
        using {{e.App}}.Contracts.{{e.Module}};
        using {{e.App}}.Web.Client.Api;

        namespace {{e.App}}.Web.Client.Pages.{{e.Module}};

        public partial class {{e.Plural}}Page
        {
            private static readonly string[] SearchFields = [{{string.Join(", ", e.Texts.Select(p => $"nameof({e.Name}Dto.{p.Name})"))}}];
            private CoworkeeDataTable<{{e.Name}}Dto> _table = null!;

            [Inject] private CoworkeeLocalizer L { get; set; } = null!;

            [Inject] private I{{e.Plural}}Api Api { get; set; } = null!;

            [Inject] private IDialogService Dialogs { get; set; } = null!;

            [Inject] private ISnackbar Snackbar { get; set; } = null!;

            private Task CreateAsync() => EditAsync(null, new AddEdit{{e.Name}}Request());

            private Task EditAsync({{e.Name}}Dto item) =>
                EditAsync(item.Id, new AddEdit{{e.Name}}Request { {{string.Join(", ", e.Properties.Select(p => $"{p.Name} = item.{p.Name}"))}} });

            private async Task EditAsync(Guid? id, AddEdit{{e.Name}}Request model)
            {
                if (await Dialogs.ShowEditAsync(L[id is null ? "New {{e.Words}}" : "Edit {{e.Words}}"], model, saved => Api.SaveAsync(id, saved)))
                {
                    Snackbar.Add(L["{{EntityModel.Sentence(e.Name)}} saved"], Severity.Success);
                }
            }

            private Task DeleteAsync(IReadOnlyCollection<{{e.Name}}Dto> items) => Snackbar.RunAsync(() => Api.DeleteAsync([.. items.Select(i => i.Id)]));
        }

        """;
}
