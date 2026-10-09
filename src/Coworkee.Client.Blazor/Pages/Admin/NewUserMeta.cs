using Coworkee.Client.Blazor.Localization;
using Coworkee.Contracts.Identity;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The new user dialog: who it is, what it may do and how it signs in the first time.</summary>
public static class NewUserMeta
{
#pragma warning disable BL0005 // MudEx configures the rendered editors through these instances
    public static void Apply(ObjectEditMeta<NewUserForm> meta, CoworkeeLocalizer l, IReadOnlyList<RoleDto> roles)
    {
        string account = l["Account"], access = l["Access"], signIn = l["First sign-in"];
        meta.Property(f => f.Email).WithLabel(l["Email"]).WithGroup(account).WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Email);
        meta.Property(f => f.IsActive).WithLabel(l["Active"]).WithGroup(account);
        meta.Property(f => f.FirstName).WithLabel(l["First name"]).WithGroup(account).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(f => f.LastName).WithLabel(l["Last name"]).WithGroup(account).WithAdditionalAttribute("MaxLength", 100);
        // a custom editor replaces the render data, so it gets its grid item again afterwards
        var rolesProperty = meta.Property(f => f.Roles).WithLabel(l["Roles"]).WithGroup(access);
        rolesProperty.RenderWith<MudExSelect<Guid>, IEnumerable<Guid>?>(s => s.SelectedValues, s =>
        {
            s.ItemCollection = [.. roles.Select(r => r.Id)];
            s.MultiSelection = true;
            s.ToStringFunc = id => roles.FirstOrDefault(r => r.Id == id)?.Name ?? string.Empty;
        });
        rolesProperty.WrapInMudItem(i =>
        {
            i.xs = 12;
            i.md = 12;
        });
        meta.Property(f => f.SendInvitation).WithLabel(l["Send invitation"]).WithGroup(signIn)
            .WithAdditionalAttribute("HelperText", l["Mails a link to choose a password."]).WrapInMudItem(i => i.md = 12);
        meta.Property(f => f.Password).WithLabel(l["Initial password"]).WithGroup(signIn)
            .WithAdditionalAttribute(nameof(MudTextField<string>.InputType), InputType.Password).IgnoreIf<NewUserForm>(f => f.SendInvitation);
        meta.Property(f => f.MustChangePassword).WithLabel(l["Change password at first sign-in"]).WithGroup(signIn).IgnoreIf<NewUserForm>(f => f.SendInvitation);
    }
#pragma warning restore BL0005
}
