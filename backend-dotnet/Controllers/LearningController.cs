using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using ELearnVN.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/learning")]
    public class LearningController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IMuxService _muxService;
        private readonly IConfiguration _config;

        public LearningController(AppDbContext context, IMuxService muxService, IConfiguration config)
        {
            _context = context;
            _muxService = muxService;
            _config = config;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        private async Task CheckAccessAsync(int userId, int productId)
        {
            var access = await _context.UserAccesses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == productId && a.IsActive);

            if (access == null)
            {
                throw new UnauthorizedAccessException("Bạn chưa mua hoặc không có quyền truy cập sản phẩm này");
            }
        }

        [HttpGet("my-courses")]
        [Authorize]
        public async Task<IActionResult> GetMyCourses()
        {
            var userId = GetCurrentUserId();
            var accessList = await _context.UserAccesses
                .Where(a => a.UserId == userId && a.IsActive)
                .AsNoTracking()
                .ToListAsync();

            var results = new List<object>();
            foreach (var access in accessList)
            {
                var product = await _context.Products
                    .Include(p => p.Course)
                    .FirstOrDefaultAsync(p => p.ProductId == access.ProductId);

                if (product != null)
                {
                    object? progress = null;
                    if (product.ProductType == "course" && product.Course != null)
                    {
                        var total = product.Course.TotalLessons;
                        var completed = await _context.LearningProgresses
                            .Join(_context.Lessons, p => p.LessonId, l => l.LessonId, (p, l) => new { p, l })
                            .Join(_context.Modules, pl => pl.l.ModuleId, m => m.ModuleId, (pl, m) => new { pl.p, m })
                            .Where(plm => plm.m.CourseId == product.ProductId && plm.p.UserId == userId && plm.p.Completed)
                            .CountAsync();

                        progress = new { completed = completed, total = total };
                    }

                    results.Add(new
                    {
                        product_id = product.ProductId,
                        name = product.Name,
                        thumbnail_url = product.ThumbnailUrl,
                        product_type = product.ProductType,
                        granted_at = access.GrantedAt,
                        progress = progress
                    });
                }
            }

            return Ok(results);
        }

        [HttpGet("course/{product_id:int}")]
        [Authorize]
        public async Task<IActionResult> GetCourseContent(int product_id)
        {
            var userId = GetCurrentUserId();
            try
            {
                await CheckAccessAsync(userId, product_id);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var product = await _context.Products
                .Include(p => p.Course).ThenInclude(c => c!.Modules).ThenInclude(m => m.Lessons)
                .FirstOrDefaultAsync(p => p.ProductId == product_id && p.ProductType == "course");

            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            // Get user progress
            var progressList = await _context.LearningProgresses
                .Join(_context.Lessons, p => p.LessonId, l => l.LessonId, (p, l) => new { p, l })
                .Join(_context.Modules, pl => pl.l.ModuleId, m => m.ModuleId, (pl, m) => new { pl.p, m })
                .Where(plm => plm.m.CourseId == product_id && plm.p.UserId == userId)
                .Select(plm => plm.p)
                .AsNoTracking()
                .ToListAsync();

            var progressMap = progressList.ToDictionary(
                p => p.LessonId,
                p => new { completed = p.Completed, watched_seconds = p.WatchedSeconds }
            );

            var modules = new List<object>();
            if (product.Course != null)
            {
                foreach (var m in product.Course.Modules.OrderBy(mod => mod.SortOrder))
                {
                    var lessons = m.Lessons.OrderBy(l => l.SortOrder).Select(l =>
                    {
                        var signedUrl = !string.IsNullOrEmpty(l.MuxPlaybackId)
                            ? _muxService.GetMuxPlaybackUrl(l.MuxPlaybackId, signed: true)
                            : null;

                        progressMap.TryGetValue(l.LessonId, out var prog);
                        var progressObj = prog ?? new { completed = false, watched_seconds = 0 };

                        return new
                        {
                            lesson_id = l.LessonId,
                            title = l.Title,
                            duration = l.Duration,
                            sort_order = l.SortOrder,
                            stream_url = signedUrl,
                            progress = progressObj
                        };
                    }).ToList();

                    modules.Add(new
                    {
                        module_id = m.ModuleId,
                        title = m.Title,
                        sort_order = m.SortOrder,
                        lessons = lessons
                    });
                }
            }

            return Ok(new
            {
                product_id = product.ProductId,
                name = product.Name,
                thumbnail_url = product.ThumbnailUrl,
                modules = modules,
                level = product.Course?.Level
            });
        }

        [HttpGet("ebook/{product_id:int}")]
        [Authorize]
        public async Task<IActionResult> GetEbookContent(int product_id)
        {
            var userId = GetCurrentUserId();
            var access = await _context.UserAccesses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == product_id && a.IsActive);

            if (access == null)
            {
                return Forbid("Bạn chưa mua hoặc không có quyền truy cập sản phẩm này");
            }

            var product = await _context.Products
                .Include(p => p.Ebook)
                .FirstOrDefaultAsync(p => p.ProductId == product_id && p.ProductType == "ebook");

            if (product == null)
            {
                return NotFound(new { detail = "Ebook không tồn tại" });
            }

            // Mark ebook as accessed
            if (!access.AccessedAt.HasValue)
            {
                access.AccessedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var downloadUrl = _muxService.GetEbookAccessUrl(product_id, userId);

            return Ok(new
            {
                product_id = product_id,
                name = product.Name,
                format = product.Ebook?.Format,
                page_count = product.Ebook?.PageCount,
                download_url = downloadUrl
            });
        }

        [HttpGet("ebook/{product_id:int}/download")]
        public async Task<IActionResult> DownloadEbook(int product_id, [FromQuery] string token)
        {
            var payload = _muxService.VerifyEbookSignedToken(token);
            if (payload == null)
            {
                return Forbid("Token không hợp lệ hoặc đã hết hạn");
            }

            // Extract claims
            payload.TryGetValue("product_id", out var tokenProdIdObj);
            int.TryParse(tokenProdIdObj?.ToString(), out var tokenProdId);

            if (tokenProdId != product_id)
            {
                return Forbid("Token không hợp lệ");
            }

            var ebook = await _context.Ebooks.FirstOrDefaultAsync(e => e.ProductId == product_id);
            if (ebook == null || string.IsNullOrEmpty(ebook.FileKey))
            {
                return NotFound(new { detail = "File ebook không tìm thấy" });
            }

            var uploadDir = _config["Uploads:UploadDir"] ?? "uploads";
            var filePath = Path.Combine(uploadDir, ebook.FileKey);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound(new { detail = "File không tồn tại trên server" });
            }

            var filename = Path.GetFileName(ebook.FileKey);
            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

            return File(fileStream, "application/pdf", filename);
        }

        [HttpPost("progress/{lesson_id:int}")]
        [Authorize]
        public async Task<IActionResult> UpdateProgress(
            int lesson_id,
            [FromQuery] int watched_seconds = 0,
            [FromQuery] bool completed = false)
        {
            var userId = GetCurrentUserId();
            var lesson = await _context.Lessons.FirstOrDefaultAsync(l => l.LessonId == lesson_id);
            if (lesson == null)
            {
                return NotFound(new { detail = $"Bài học {lesson_id} không tồn tại" });
            }

            var progress = await _context.LearningProgresses
                .FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lesson_id);

            if (progress != null)
            {
                progress.WatchedSeconds = Math.Max(progress.WatchedSeconds, watched_seconds);
                if (completed && !progress.Completed)
                {
                    progress.Completed = true;
                    progress.CompletedAt = DateTime.UtcNow;
                }
                progress.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                progress = new LearningProgress
                {
                    UserId = userId,
                    LessonId = lesson_id,
                    WatchedSeconds = watched_seconds,
                    Completed = completed,
                    CompletedAt = completed ? DateTime.UtcNow : null,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.LearningProgresses.Add(progress);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                lesson_id = lesson_id,
                completed = progress.Completed,
                watched_seconds = progress.WatchedSeconds
            });
        }
    }
}
