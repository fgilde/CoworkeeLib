using System.ComponentModel.DataAnnotations;
using Coworkee.Client.Blazor.Api;
using Coworkee.Client.Blazor.Components.Data;
using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The permissions administrators define themselves; the role page grants them like the ones in code.</summary>
public partial class CustomPermissions
{
    private IReadOnlyList<CustomPermissionDto> _permissions = [];
    private bool _busy;

    [Inject] private ICoworkeeApi Api { get; set; } = null!;

    [Inject] private CoworkeeLocalizer L { get; set; } = null!;

    [Inject] private IDialogService Dialogs { get; set; } = null!;

    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    [Inject] private Navigation.CoworkeePages Links { get; set; } = null!;

    protected override Task OnInitializedAsync() => RunAsync(() => Task.CompletedTask);

    private async Task CreateAsync()
    {
        if (await Dialogs.ShowEditAsync(L["New permission"], new NewPermission(), form => Api.CreateCustomPermissionAsync(form.ToRequest()), Meta))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task EditAsync(CustomPermissionDto permission)
    {
        var form = new PermissionTexts { DisplayName = permission.DisplayName, Description = permission.Description, Group = permission.Group };
        if (await Dialogs.ShowEditAsync(L["Edit {0}", permission.Name], form, f => Api.UpdateCustomPermissionAsync(permission.Name, f.ToRequest()), Meta))
        {
            await RunAsync(() => Task.CompletedTask);
        }
    }

    private async Task DeleteAsync(CustomPermissionDto permission)
    {
        CustomPermissionUsageDto? usage = null;
        if (await Snackbar.RunAsync(async () => usage = await Api.GetCustomPermissionUsageAsync(permission.Name))
            && usage is not null
            && await CustomPermissionDeleteDialog.ConfirmAsync(Dialogs, L, permission, usage))
        {
            await RunAsync(() => Api.DeleteCustomPermissionAsync(permission.Name));
        }
    }

    private void Meta(ObjectEditMeta<NewPermission> meta)
    {
        meta.Property(f => f.Name).WithLabel(L["Name"]).WithAdditionalAttribute(nameof(MudTextField<string>.HelperText), L["Starts with Custom., e.g. Custom.Reports.Export; it cannot be changed later."]);
        TextsMeta(meta.Property(f => f.DisplayName), meta.Property(f => f.Description), meta.Property(f => f.Group));
    }

    private void Meta(ObjectEditMeta<PermissionTexts> meta) =>
        TextsMeta(meta.Property(f => f.DisplayName), meta.Property(f => f.Description), meta.Property(f => f.Group));

    private void TextsMeta(ObjectEditPropertyMeta displayName, ObjectEditPropertyMeta description, ObjectEditPropertyMeta group)
    {
        displayName.WithLabel(L["Display name"]);
        description.WithLabel(L["Description"]);
        group.WithLabel(L["Group"]).WithAdditionalAttribute(nameof(MudTextField<string>.HelperText), L["Where the permission shows in the role's permission list; empty is Custom."]);
    }

    private async Task RunAsync(Func<Task> action)
    {
        _busy = true;
        try
        {
            await Snackbar.RunAsync(action);
            await Snackbar.RunAsync(async () => _permissions = await Api.GetCustomPermissionsAsync());
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed class NewPermission
    {
        [Required]
        public string Name { get; set; } = CustomPermissionNames.Prefix;

        [Required]
        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Group { get; set; }

        public CreateCustomPermissionRequest ToRequest() => new(Name.Trim(), DisplayName, Description, Group);
    }

    private sealed class PermissionTexts
    {
        [Required]
        public string DisplayName { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Group { get; set; }

        public UpdateCustomPermissionRequest ToRequest() => new(DisplayName, Description, Group);
    }
}
