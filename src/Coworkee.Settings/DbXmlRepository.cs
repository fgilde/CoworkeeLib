using System.Xml.Linq;
using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings;

internal sealed class DbXmlRepository(IServiceScopeFactory scopes) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<DataProtectionKey>()
            .Select(k => k.Xml).ToList()
            .Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>();
        db.Set<DataProtectionKey>().Add(new DataProtectionKey { FriendlyName = friendlyName, Xml = element.ToString(SaveOptions.DisableFormatting) });
        db.SaveChanges();
    }
}
