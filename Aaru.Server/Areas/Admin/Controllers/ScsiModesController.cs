using System.Threading.Tasks;
using Aaru.CommonTypes.Metadata;
using Aaru.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aaru.Server.Areas.Admin.Controllers
{
    [Area("Admin"), Authorize]
    public sealed class ScsiModesController : Controller
    {
        readonly AaruServerContext _context;

        public ScsiModesController(AaruServerContext context) => _context = context;

        // GET: Admin/ScsiModes
        public async Task<IActionResult> Index() => View(await _context.ScsiMode.ToListAsync());

        // GET: Admin/ScsiModes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if(id == null)
            {
                return NotFound();
            }

            ScsiMode scsiMode = await _context.ScsiMode.FirstOrDefaultAsync(m => m.Id == id);

            if(scsiMode == null)
            {
                return NotFound();
            }

            return View(scsiMode);
        }

        // POST: Admin/ScsiModes/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            ScsiMode scsiMode = await _context.ScsiMode.FindAsync(id);
            _context.ScsiMode.Remove(scsiMode);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}