using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DiscImageChef.Server.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class RegisterModel : PageModel
    {
        public IActionResult OnGetAsync(string returnUrl = null) => RedirectToPage("Login");

        public IActionResult OnPostAsync(string returnUrl = null) => RedirectToPage("Login");
    }
}