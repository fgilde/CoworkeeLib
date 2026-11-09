using Bunit;
using Coworkee.Client.Blazor.Auditing;
using Coworkee.Client.Blazor.Data;
using Coworkee.Client.Blazor.Pages.Admin;
using Coworkee.Contracts.Auditing;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class SecurityAndRequestLogTests : ClientTestBase
{
    private static readonly Guid AdaId = Guid.CreateVersion7();
    private readonly FakeODataClient _odata = new();
    private readonly IAuditApi _audit = Substitute.For<IAuditApi>();

    public SecurityAndRequestLogTests()
    {
        Services.AddSingleton<IODataClient>(_odata);
        Services.AddSingleton(_audit);
        Services.AddScoped<TraceViewer>();
        AddAuthorization().SetAuthorized("Ada").SetClaims(new System.Security.Claims.Claim("sub", AdaId.ToString())).SetPolicies(
            Security.PermissionPolicy.For(AuditPermissions.SecurityLog), Security.PermissionPolicy.For(AuditPermissions.Requests));
        Api.GetUserCardsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>()).Returns([new UserCardDto(AdaId, "Ada Lovelace", null)]);
        _audit.GetTraceViewerAsync(Arg.Any<CancellationToken>()).Returns(new TraceViewerDto("https://grafana.test/explore?trace={traceId}"));
        Render<MudPopoverProvider>();
    }

    [Fact]
    public void The_security_log_shows_the_entries_of_the_user_from_the_link_with_details_and_trace()
    {
        _odata.With("SecurityEvents", new SecurityEventDto(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, AdaId, null, null, SecurityActions.LockedOut, false,
            "FailedAttempts", null, "10.0.0.1", "Firefox", "abc123", "{\"until\":\"2030-01-01\"}"));
        var filters = new List<string?>();
        _odata.Queried += (_, query) => filters.Add(query.Filter);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/admin/security-log?user={AdaId}");

        var page = Render<SecurityLog>();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Ada Lovelace"));
        page.Markup.ShouldContain(SecurityActions.LockedOut);
        page.Find("[data-testid='security-log-user-filter']").ShouldNotBeNull();
        filters.ShouldAllBe(f => f != null && f.Contains($"UserId eq {AdaId}"));

        page.Find(".mud-table-body .mud-table-row button").Click();
        page.WaitForAssertion(() => page.Find("[data-testid='trace-link']").GetAttribute("href").ShouldBe("https://grafana.test/explore?trace=abc123"));
        page.Find("[data-testid='security-log-detail']").TextContent.ShouldSatisfyAllConditions(t => t.ShouldContain("Firefox"), t => t.ShouldContain("\"until\": \"2030-01-01\""));
    }

    [Fact]
    public void A_request_opens_with_its_headers_and_bodies_or_says_they_need_a_permission()
    {
        var request = new RequestLogDto(Guid.CreateVersion7(), DateTimeOffset.UtcNow, null, AdaId, null, null, null, "POST", "/api/v1/brands", "?x=1", "/api/v1/brands",
            "AddBrand", 201, 12, null, null, "abc123", null);
        _odata.With("RequestLog", request);
        _audit.GetRequestAsync(request.Id, Arg.Any<CancellationToken>()).Returns(
            new RequestLogDetailDto(request, new Dictionary<string, string> { ["Authorization"] = "***" }, new Dictionary<string, string>(), null, null, true));

        var page = Render<RequestLog>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("/api/v1/brands?x=1"));
        page.Find(".mud-table-body .mud-table-row button").Click();

        page.WaitForAssertion(() => page.Find("[data-testid='request-log-detail']").TextContent.ShouldContain("Authorization: ***"));
        page.Find("[data-testid='request-log-detail']").TextContent.ShouldContain("Bodies need the permission to view request and response bodies.");
        page.Find("[data-testid='trace-link']").ShouldNotBeNull();
    }
}
