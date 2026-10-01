using Coworkee.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coworkee.AuthServer.Pages.Account;

public sealed class LogoutModel(SignInManager<User> signIn) : PageModel
{
    public async Task OnGetAsync() => await signIn.SignOutAsync();
}
