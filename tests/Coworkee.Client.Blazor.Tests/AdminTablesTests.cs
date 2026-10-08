using Bunit;
using Coworkee.Client.Blazor.Data.Admin;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class AdminTablesTests : ClientTestBase
{
    private static readonly Guid AdaId = Guid.CreateVersion7();
    private readonly FakeODataClient _odata = new();

    public AdminTablesTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        Services.AddScoped<Security.PermissionStore>();
        AddAuthorization().SetAuthorized("Ada").SetPolicies(
            Security.PermissionPolicy.For(IdentityPermissions.Users.View), Security.PermissionPolicy.For(AuditPermissions.View));
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void Users_show_the_roles_of_the_visible_rows_from_one_lookup()
    {
        var editors = new RoleDto(Guid.CreateVersion7(), "Editors", null, false);
        _odata.With("Users", new UserRow(AdaId, "ada@acme.test", "Ada", "Lovelace", true, null, DateTimeOffset.UtcNow));
        Api.GetRolesAsync(Arg.Any<CancellationToken>()).Returns([editors]);
        Api.GetUsersRolesAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, IReadOnlyList<RoleRefDto>> { [AdaId] = [new RoleRefDto(editors.Id, editors.Name)] });

        var page = Render<Users>();

        page.WaitForAssertion(() => page.Find("[data-user='ada@acme.test']").ShouldNotBeNull());
        page.WaitForAssertion(() => page.Markup.ShouldContain("Editors"));
        page.Markup.ShouldContain("Ada Lovelace");
        Api.Received(1).GetUsersRolesAsync(Arg.Is<IReadOnlyList<Guid>>(ids => ids.Single() == AdaId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_audit_log_names_who_changed_something()
    {
        _odata.With("AuditEntries", new AuditEntryDto(Guid.CreateVersion7(), "Brand", "42", "Modified", AdaId, null, DateTimeOffset.UtcNow, null,
            [new AuditChangeDto("Name", "Old", "New")]));
        Api.GetUserCardsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([new Contracts.Identity.UserCardDto(AdaId, "Ada Lovelace", null)]);

        var page = Render<AuditLog>();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Ada Lovelace"));
        page.Markup.ShouldContain("Modified");
    }
}
