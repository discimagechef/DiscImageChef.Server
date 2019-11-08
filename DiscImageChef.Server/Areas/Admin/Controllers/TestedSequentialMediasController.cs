using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiscImageChef.CommonTypes.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using DiscImageChef.Server.Models;
using Microsoft.AspNetCore.Authorization;

namespace DiscImageChef.Server.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize]
    public class TestedSequentialMediasController : Controller
    {
        private readonly DicServerContext _context;

        public TestedSequentialMediasController(DicServerContext context)
        {
            _context = context;
        }

        // GET: Admin/TestedSequentialMedias
        public async Task<IActionResult> Index()
        {
            return View(await _context.TestedSequentialMedia.ToListAsync());
        }

        // GET: Admin/TestedSequentialMedias/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var testedSequentialMedia = await _context.TestedSequentialMedia
                .FirstOrDefaultAsync(m => m.Id == id);
            if (testedSequentialMedia == null)
            {
                return NotFound();
            }

            return View(testedSequentialMedia);
        }

        // GET: Admin/TestedSequentialMedias/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/TestedSequentialMedias/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,CanReadMediaSerial,Density,Manufacturer,MediaIsRecognized,MediumType,MediumTypeName,Model,ModeSense6Data,ModeSense10Data")] TestedSequentialMedia testedSequentialMedia)
        {
            if (ModelState.IsValid)
            {
                _context.Add(testedSequentialMedia);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(testedSequentialMedia);
        }

        // GET: Admin/TestedSequentialMedias/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var testedSequentialMedia = await _context.TestedSequentialMedia.FindAsync(id);
            if (testedSequentialMedia == null)
            {
                return NotFound();
            }
            return View(testedSequentialMedia);
        }

        // POST: Admin/TestedSequentialMedias/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for
        // more details see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,CanReadMediaSerial,Density,Manufacturer,MediaIsRecognized,MediumType,MediumTypeName,Model,ModeSense6Data,ModeSense10Data")] TestedSequentialMedia testedSequentialMedia)
        {
            if (id != testedSequentialMedia.Id)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(testedSequentialMedia);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TestedSequentialMediaExists(testedSequentialMedia.Id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(testedSequentialMedia);
        }

        // GET: Admin/TestedSequentialMedias/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var testedSequentialMedia = await _context.TestedSequentialMedia
                .FirstOrDefaultAsync(m => m.Id == id);
            if (testedSequentialMedia == null)
            {
                return NotFound();
            }

            return View(testedSequentialMedia);
        }

        // POST: Admin/TestedSequentialMedias/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var testedSequentialMedia = await _context.TestedSequentialMedia.FindAsync(id);
            _context.TestedSequentialMedia.Remove(testedSequentialMedia);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        private bool TestedSequentialMediaExists(int id)
        {
            return _context.TestedSequentialMedia.Any(e => e.Id == id);
        }
    }
}
