using System.Linq;
using System.Threading.Tasks;
using DiscImageChef.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscImageChef.Server.Areas.Admin.Controllers
{
    [Area("Admin"), Authorize]
    public class CommandsController : Controller
    {
        readonly DicServerContext _context;

        public CommandsController(DicServerContext context) => _context = context;

        // GET: Admin/Commands
        public async Task<IActionResult> Index() => View(await _context.Commands.OrderBy(c => c.Name).ToListAsync());
    }
}