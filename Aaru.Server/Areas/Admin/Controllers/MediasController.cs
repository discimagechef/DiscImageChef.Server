using System.Linq;
using System.Threading.Tasks;
using DiscImageChef.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscImageChef.Server.Areas.Admin.Controllers
{
    [Area("Admin"), Authorize]
    public class MediasController : Controller
    {
        readonly DicServerContext _context;

        public MediasController(DicServerContext context) => _context = context;

        // GET: Admin/Medias
        public IActionResult Index(bool? real)
        {
            switch(real)
            {
                case null:
                    return View(_context.Medias.ToList().OrderBy(m => m.PhysicalType).ThenBy(m => m.LogicalType).
                                         ThenBy(m => m.Real));
                default:
                    return View(_context.Medias.Where(m => m.Real == real).ToList().OrderBy(m => m.PhysicalType).
                                         ThenBy(m => m.LogicalType));
            }
        }

        // GET: Admin/Medias/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if(id == null)
            {
                return NotFound();
            }

            Media media = await _context.Medias.FirstOrDefaultAsync(m => m.Id == id);

            if(media == null)
            {
                return NotFound();
            }

            return View(media);
        }

        // POST: Admin/Medias/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            Media media = await _context.Medias.FindAsync(id);
            _context.Medias.Remove(media);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}