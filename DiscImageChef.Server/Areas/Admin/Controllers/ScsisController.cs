using System.Linq;
using System.Threading.Tasks;
using DiscImageChef.CommonTypes.Metadata;
using DiscImageChef.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DiscImageChef.Server.Areas.Admin.Controllers
{
    [Area("Admin"), Authorize]
    public class ScsisController : Controller
    {
        readonly DicServerContext _context;

        public ScsisController(DicServerContext context) => _context = context;

        // GET: Admin/Scsis
        public async Task<IActionResult> Index() => View(await _context.Scsi.ToListAsync());

        // GET: Admin/Scsis/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if(id == null)
            {
                return NotFound();
            }

            Scsi scsi = await _context.Scsi.FirstOrDefaultAsync(m => m.Id == id);

            if(scsi == null)
            {
                return NotFound();
            }

            return View(scsi);
        }

        // GET: Admin/Scsis/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if(id == null)
            {
                return NotFound();
            }

            Scsi scsi = await _context.Scsi.FindAsync(id);

            if(scsi == null)
            {
                return NotFound();
            }

            return View(scsi);
        }

        // POST: Admin/Scsis/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id, [Bind(
                "Id,InquiryData,SupportsModeSense6,SupportsModeSense10,SupportsModeSubpages,ModeSense6Data,ModeSense10Data,ModeSense6CurrentData,ModeSense10CurrentData,ModeSense6ChangeableData,ModeSense10ChangeableData")]
            Scsi scsi)
        {
            if(id != scsi.Id)
            {
                return NotFound();
            }

            if(ModelState.IsValid)
            {
                try
                {
                    _context.Update(scsi);
                    await _context.SaveChangesAsync();
                }
                catch(DbUpdateConcurrencyException)
                {
                    if(!ScsiExists(scsi.Id))
                    {
                        return NotFound();
                    }

                    throw;
                }

                return RedirectToAction(nameof(Index));
            }

            return View(scsi);
        }

        // GET: Admin/Scsis/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if(id == null)
            {
                return NotFound();
            }

            Scsi scsi = await _context.Scsi.FirstOrDefaultAsync(m => m.Id == id);

            if(scsi == null)
            {
                return NotFound();
            }

            return View(scsi);
        }

        // POST: Admin/Scsis/Delete/5
        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            Scsi scsi = await _context.Scsi.FindAsync(id);
            _context.Scsi.Remove(scsi);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        bool ScsiExists(int id) => _context.Scsi.Any(e => e.Id == id);
    }
}