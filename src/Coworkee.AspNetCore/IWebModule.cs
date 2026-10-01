using Microsoft.AspNetCore.Builder;

namespace Coworkee.AspNetCore;

public interface IWebModule
{
    void ConfigureApplication(WebApplication app);
}
