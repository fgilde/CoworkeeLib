using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.Options;

namespace Coworkee.OData;

internal sealed class ODataQueryLimits(IOptions<CoworkeeODataOptions> options) : IPostConfigureOptions<ODataOptions>
{
    public void PostConfigure(string? name, ODataOptions odata) => odata.SetMaxTop(options.Value.MaxTop);
}
