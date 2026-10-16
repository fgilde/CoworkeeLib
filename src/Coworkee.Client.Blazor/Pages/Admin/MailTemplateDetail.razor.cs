using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Coworkee.Client.Blazor.Pages.Admin;

public partial class MailTemplateDetail
{
    private List<BreadcrumbItem> _breadcrumbs = [];

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    [Parameter] public string Name { get; set; } = string.Empty;

    [Parameter] public string Culture { get; set; } = string.Empty;

    protected override void OnParametersSet() =>
        _breadcrumbs = [new BreadcrumbItem("Mail templates", Links.Href("/admin/mail/templates")), new BreadcrumbItem(Name, null, disabled: true)];
}
