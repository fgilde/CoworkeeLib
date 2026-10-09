using Coworkee.Client.Blazor.Localization;
using MudBlazor;
using MudBlazor.Extensions.Components;
using MudBlazor.Extensions.Components.ObjectEdit;
using MudBlazor.Extensions.Components.ObjectEdit.Options;

namespace Coworkee.Client.Blazor.Pages.Admin;

/// <summary>The user form of the user page: account, contact and status in two columns.</summary>
public static class UserFormMeta
{
#pragma warning disable BL0005 // MudEx configures the rendered editors and grid items through these instances
    public static void Apply(ObjectEditMeta<UserForm> meta, CoworkeeLocalizer l, bool readOnly)
    {
        meta.WrapEachInMudItem(i =>
        {
            i.xs = 12;
            i.md = 6;
        });
        string account = l["Account"], contact = l["Contact"], status = l["Status"];
        meta.Property(f => f.UserName).WithLabel(l["User name"]).WithGroup(account).WithAdditionalAttribute("MaxLength", 256);
        meta.Property(f => f.FirstName).WithLabel(l["First name"]).WithGroup(account).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(f => f.LastName).WithLabel(l["Last name"]).WithGroup(account).WithAdditionalAttribute("MaxLength", 100);
        var language = meta.Property(f => f.Language).WithLabel(l["Language"]).WithGroup(account);
        language.RenderWith<MudExSelect<string>, string?>(s => s.Value, s =>
        {
            s.ItemCollection = [string.Empty, .. l.Languages.Select(c => c.Culture)];
            s.ToStringFunc = culture => string.IsNullOrEmpty(culture) ? l["Organisation default"] : l.Languages.FirstOrDefault(c => c.Culture == culture)?.Name ?? culture!;
        });
        language.WrapInMudItem(i =>
        {
            i.xs = 12;
            i.md = 6;
        });
        meta.Property(f => f.PhoneNumber).WithLabel(l["Phone"]).WithGroup(contact).WithAdditionalAttribute("MaxLength", 50);
        meta.Property(f => f.Street).WithLabel(l["Street"]).WithGroup(contact).WithAdditionalAttribute("MaxLength", 200);
        meta.Property(f => f.ZipCode).WithLabel(l["Zip code"]).WithGroup(contact).WithAdditionalAttribute("MaxLength", 20);
        meta.Property(f => f.City).WithLabel(l["City"]).WithGroup(contact).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(f => f.Country).WithLabel(l["Country"]).WithGroup(contact).WithAdditionalAttribute("MaxLength", 100);
        meta.Property(f => f.IsActive).WithLabel(l["Active"]).WithGroup(status);
        meta.Property(f => f.EmailConfirmed).WithLabel(l["Email confirmed"]).WithGroup(status);
        meta.Property(f => f.MustChangePassword).WithLabel(l["Change password at next sign-in"]).WithGroup(status);
        foreach (var property in readOnly ? meta.AllProperties : [])
        {
            property.AsReadOnlyIf<UserForm>(_ => true);
        }
    }
#pragma warning restore BL0005
}
