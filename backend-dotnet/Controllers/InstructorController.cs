using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using ELearnVN.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/instructor")]
    [Authorize(Roles = "admin,author")]
    public class InstructorController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;

        public InstructorController(AppDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        private bool IsAdmin()
        {
            return User.IsInRole("admin");
        }

        private void AssertOwner(Product product)
        {
            if (IsAdmin()) return;
            var userId = GetCurrentUserId();
            if (product.AuthorId != userId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền thao tác sản phẩm này");
            }
        }

        private object BuildListItem(Product p)
        {
            return new
            {
                product_id = p.ProductId,
                name = p.Name,
                price = p.Price,
                original_price = p.OriginalPrice,
                thumbnail_url = p.ThumbnailUrl,
                product_type = p.ProductType,
                status = p.Status,
                average_rating = p.AverageRating,
                review_count = p.ReviewCount,
                total_enrolled = p.TotalEnrolled,
                category = p.Category != null ? new
                {
                    category_id = p.Category.CategoryId,
                    name = p.Category.Name
                } : null,
                author_name = p.Author?.Name,
                level = p.Course?.Level,
                duration = p.Course?.Duration
            };
        }

        [HttpGet("courses")]
        public async Task<IActionResult> ListCourses(
            [FromQuery] int page = 1,
            [FromQuery] int page_size = 20,
            [FromQuery] string? status = null)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 100) page_size = 20;

            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course)
                .AsQueryable();

            if (!IsAdmin())
            {
                var userId = GetCurrentUserId();
                query = query.Where(p => p.AuthorId == userId);
            }

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(p => p.Status == status);
            }

            var total = await query.CountAsync();
            var products = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .ToListAsync();

            var result = products.Select(BuildListItem).ToList();

            return Ok(new
            {
                products = result,
                total = total,
                page = page,
                page_size = page_size
            });
        }

        [HttpPost("courses")]
        public async Task<IActionResult> CreateCourse([FromBody] ProductCreateDto dto)
        {
            if (dto.ProductType != "course" && dto.ProductType != "ebook")
            {
                return BadRequest(new { detail = "product_type phải là 'course' hoặc 'ebook'" });
            }

            var userId = GetCurrentUserId();

            var product = new Product
            {
                CategoryId = dto.CategoryId,
                Name = dto.Name,
                Price = dto.Price,
                OriginalPrice = dto.OriginalPrice,
                Description = dto.Description,
                ShortDescription = dto.ShortDescription,
                ThumbnailUrl = dto.ThumbnailUrl,
                Status = "draft", // Always draft upon creation
                ProductType = dto.ProductType,
                AuthorId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            if (dto.ProductType == "course")
            {
                var course = new Course
                {
                    ProductId = product.ProductId,
                    Duration = dto.Duration ?? 0,
                    Level = dto.Level,
                    Requirements = dto.Requirements,
                    WhatYouLearn = dto.WhatYouLearn
                };
                _context.Courses.Add(course);
            }
            else
            {
                var ebook = new Ebook
                {
                    ProductId = product.ProductId,
                    FileSize = dto.FileSize,
                    Format = dto.Format,
                    PageCount = dto.PageCount
                };
                _context.Ebooks.Add(ebook);
            }

            await _context.SaveChangesAsync();

            var reloaded = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course)
                .FirstAsync(p => p.ProductId == product.ProductId);

            return Ok(BuildListItem(reloaded));
        }

        [HttpGet("courses/{product_id:int}")]
        public async Task<IActionResult> GetCourse(int product_id)
        {
            var product = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course).ThenInclude(c => c!.Modules).ThenInclude(m => m.Lessons)
                .Include(p => p.Ebook)
                .FirstOrDefaultAsync(p => p.ProductId == product_id);

            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            try
            {
                AssertOwner(product);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var modules = new List<object>();
            if (product.Course != null)
            {
                foreach (var m in product.Course.Modules.OrderBy(mod => mod.SortOrder))
                {
                    var lessons = m.Lessons.OrderBy(l => l.SortOrder).Select(l => new
                    {
                        lesson_id = l.LessonId,
                        title = l.Title,
                        mux_playback_id = l.MuxPlaybackId ?? "",
                        mux_asset_id = l.MuxAssetId ?? "",
                        duration = l.Duration,
                        sort_order = l.SortOrder,
                        is_preview = l.IsPreview
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
                price = product.Price,
                original_price = product.OriginalPrice,
                description = product.Description,
                short_description = product.ShortDescription,
                thumbnail_url = product.ThumbnailUrl,
                status = product.Status,
                rejection_reason = product.RejectionReason,
                product_type = product.ProductType,
                category_id = product.CategoryId,
                author_id = product.AuthorId,
                total_enrolled = product.TotalEnrolled,
                average_rating = (double)(product.AverageRating),
                course = product.Course != null ? new
                {
                    duration = product.Course.Duration,
                    level = product.Course.Level,
                    total_lessons = product.Course.TotalLessons,
                    requirements = product.Course.Requirements,
                    what_you_learn = product.Course.WhatYouLearn,
                    modules = modules
                } : null,
                ebook = product.Ebook != null ? new
                {
                    file_size = (double?)product.Ebook.FileSize,
                    format = product.Ebook.Format,
                    page_count = product.Ebook.PageCount
                } : null
            });
        }

        [HttpPut("courses/{product_id:int}")]
        public async Task<IActionResult> UpdateCourse(int product_id, [FromBody] ProductUpdateDto dto)
        {
            var product = await _context.Products
                .Include(p => p.Course)
                .Include(p => p.Ebook)
                .FirstOrDefaultAsync(p => p.ProductId == product_id);

            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            try
            {
                AssertOwner(product);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            if (product.Status == "pending_approval")
            {
                return BadRequest(new { detail = "Khóa học đang chờ duyệt, không thể chỉnh sửa lúc này." });
            }

            bool wasActive = product.Status == "active";

            if (dto.Name != null) product.Name = dto.Name;
            if (dto.Price.HasValue) product.Price = dto.Price.Value;
            if (dto.OriginalPrice.HasValue) product.OriginalPrice = dto.OriginalPrice.Value;
            if (dto.Description != null) product.Description = dto.Description;
            if (dto.ShortDescription != null) product.ShortDescription = dto.ShortDescription;
            if (dto.ThumbnailUrl != null) product.ThumbnailUrl = dto.ThumbnailUrl;
            if (dto.CategoryId.HasValue) product.CategoryId = dto.CategoryId.Value;

            if (product.ProductType == "course" && product.Course != null)
            {
                if (dto.Duration.HasValue) product.Course.Duration = dto.Duration.Value;
                if (dto.Level != null) product.Course.Level = dto.Level;
                if (dto.Requirements != null) product.Course.Requirements = dto.Requirements;
                if (dto.WhatYouLearn != null) product.Course.WhatYouLearn = dto.WhatYouLearn;
            }
            else if (product.ProductType == "ebook" && product.Ebook != null)
            {
                if (dto.FileSize.HasValue) product.Ebook.FileSize = dto.FileSize.Value;
                if (dto.Format != null) product.Ebook.Format = dto.Format;
                if (dto.PageCount.HasValue) product.Ebook.PageCount = dto.PageCount.Value;
            }

            if (wasActive)
            {
                product.Status = "pending_approval";
                product.RejectionReason = null;
            }

            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var reloaded = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course)
                .FirstAsync(p => p.ProductId == product.ProductId);

            return Ok(BuildListItem(reloaded));
        }

        [HttpPost("courses/{product_id:int}/submit")]
        public async Task<IActionResult> SubmitCourse(int product_id)
        {
            var product = await _context.Products
                .Include(p => p.Course)
                .Include(p => p.Author)
                .FirstOrDefaultAsync(p => p.ProductId == product_id);

            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            try
            {
                AssertOwner(product);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            if (product.Status != "draft" && product.Status != "rejected")
            {
                return BadRequest(new { detail = $"Chỉ có thể gửi duyệt khóa học ở trạng thái Nháp hoặc Bị từ chối (hiện tại: {product.Status})" });
            }

            if (product.ProductType == "course")
            {
                var lessonCount = await _context.Lessons
                    .Join(_context.Modules, l => l.ModuleId, m => m.ModuleId, (l, m) => new { l, m })
                    .Where(lm => lm.m.CourseId == product_id)
                    .CountAsync();

                if (lessonCount == 0)
                {
                    return BadRequest(new { detail = "Khóa học phải có nhất 1 bài học trước khi gửi duyệt" });
                }
            }

            product.Status = "pending_approval";
            product.RejectionReason = null;
            product.UpdatedAt = DateTime.UtcNow;

            await _notificationService.NotifyCourseSubmittedAsync(product_id, product.Name, product.Author?.Name ?? "Instructor");
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã gửi khóa học lên kiểm duyệt thành công. Admin sẽ sớm xem xét!" });
        }

        // --- Modules Endpoints ---

        [HttpGet("courses/{product_id:int}/modules")]
        public async Task<IActionResult> GetModules(int product_id)
        {
            var product = await _context.Products
                .Include(p => p.Course).ThenInclude(c => c!.Modules).ThenInclude(m => m.Lessons)
                .FirstOrDefaultAsync(p => p.ProductId == product_id);

            if (product == null || product.Course == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            try
            {
                AssertOwner(product);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            var modules = product.Course.Modules.OrderBy(m => m.SortOrder).Select(m => new
            {
                module_id = m.ModuleId,
                title = m.Title,
                sort_order = m.SortOrder,
                lessons = m.Lessons.OrderBy(l => l.SortOrder).Select(l => new
                {
                    lesson_id = l.LessonId,
                    title = l.Title,
                    mux_playback_id = l.MuxPlaybackId ?? "",
                    mux_asset_id = l.MuxAssetId ?? "",
                    duration = l.Duration,
                    sort_order = l.SortOrder,
                    is_preview = l.IsPreview
                })
            });

            return Ok(modules);
        }

        [HttpPost("courses/{product_id:int}/modules")]
        public async Task<IActionResult> CreateModule(int product_id, [FromQuery] string title, [FromQuery] int sort_order = 0)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            try
            {
                AssertOwner(product);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }

            if (product.Status == "pending_approval")
            {
                return BadRequest(new { detail = "Không thể chỉnh sửa khi đang chờ duyệt" });
            }

            var module = new Module
            {
                CourseId = product_id,
                Title = title,
                SortOrder = sort_order
            };
            _context.Modules.Add(module);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                module_id = module.ModuleId,
                title = module.Title,
                sort_order = module.SortOrder,
                lessons = new List<object>()
            });
        }

        [HttpPut("modules/{module_id:int}")]
        public async Task<IActionResult> UpdateModule(int module_id, [FromQuery] string? title = null, [FromQuery] int? sort_order = null)
        {
            var module = await _context.Modules.FirstOrDefaultAsync(m => m.ModuleId == module_id);
            if (module == null)
            {
                return NotFound(new { detail = "Module không tồn tại" });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == module.CourseId);
            if (product != null)
            {
                try
                {
                    AssertOwner(product);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Forbid(ex.Message);
                }

                if (product.Status == "pending_approval")
                {
                    return BadRequest(new { detail = "Không thể chỉnh sửa khi đang chờ duyệt" });
                }
            }

            if (title != null) module.Title = title;
            if (sort_order.HasValue) module.SortOrder = sort_order.Value;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật module thành công" });
        }

        [HttpDelete("modules/{module_id:int}")]
        public async Task<IActionResult> DeleteModule(int module_id)
        {
            var module = await _context.Modules.FirstOrDefaultAsync(m => m.ModuleId == module_id);
            if (module == null)
            {
                return NotFound(new { detail = "Module không tồn tại" });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == module.CourseId);
            if (product != null)
            {
                try
                {
                    AssertOwner(product);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Forbid(ex.Message);
                }

                if (product.Status == "pending_approval")
                {
                    return BadRequest(new { detail = "Không thể chỉnh sửa khi đang chờ duyệt" });
                }
            }

            var courseId = module.CourseId;
            _context.Modules.Remove(module);
            await _context.SaveChangesAsync();

            await RecalcTotalLessons(courseId);
            return Ok(new { message = "Đã xóa module" });
        }

        // --- Lessons Endpoints ---

        [HttpPost("modules/{module_id:int}/lessons")]
        public async Task<IActionResult> CreateLesson(
            int module_id,
            [FromQuery] string title,
            [FromQuery] string? mux_playback_id = "",
            [FromQuery] string? mux_asset_id = "",
            [FromQuery] int duration = 0,
            [FromQuery] int sort_order = 0,
            [FromQuery] bool is_preview = false)
        {
            var module = await _context.Modules.FirstOrDefaultAsync(m => m.ModuleId == module_id);
            if (module == null)
            {
                return NotFound(new { detail = "Module không tồn tại" });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == module.CourseId);
            if (product != null)
            {
                try
                {
                    AssertOwner(product);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Forbid(ex.Message);
                }

                if (product.Status == "pending_approval")
                {
                    return BadRequest(new { detail = "Không thể thêm bài học khi đang chờ duyệt" });
                }
            }

            var lesson = new Lesson
            {
                ModuleId = module_id,
                Title = title,
                MuxPlaybackId = string.IsNullOrEmpty(mux_playback_id) ? null : mux_playback_id,
                MuxAssetId = string.IsNullOrEmpty(mux_asset_id) ? null : mux_asset_id,
                Duration = duration,
                SortOrder = sort_order,
                IsPreview = is_preview
            };
            _context.Lessons.Add(lesson);
            await _context.SaveChangesAsync();

            await RecalcTotalLessons(module.CourseId);

            return Ok(new
            {
                lesson_id = lesson.LessonId,
                title = lesson.Title,
                mux_playback_id = lesson.MuxPlaybackId ?? "",
                duration = lesson.Duration,
                sort_order = lesson.SortOrder,
                is_preview = lesson.IsPreview
            });
        }

        [HttpPut("lessons/{lesson_id:int}")]
        public async Task<IActionResult> UpdateLesson(
            int lesson_id,
            [FromQuery] string? title = null,
            [FromQuery] string? mux_playback_id = null,
            [FromQuery] string? mux_asset_id = null,
            [FromQuery] int? duration = null,
            [FromQuery] int? sort_order = null,
            [FromQuery] bool? is_preview = null)
        {
            var lesson = await _context.Lessons
                .Include(l => l.Module)
                .FirstOrDefaultAsync(l => l.LessonId == lesson_id);

            if (lesson == null)
            {
                return NotFound(new { detail = "Bài học không tồn tại" });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == lesson.Module!.CourseId);
            if (product != null)
            {
                try
                {
                    AssertOwner(product);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Forbid(ex.Message);
                }

                if (product.Status == "pending_approval")
                {
                    return BadRequest(new { detail = "Không thể chỉnh sửa khi đang chờ duyệt" });
                }
            }

            if (title != null) lesson.Title = title;
            if (mux_playback_id != null) lesson.MuxPlaybackId = string.IsNullOrEmpty(mux_playback_id) ? null : mux_playback_id;
            if (mux_asset_id != null) lesson.MuxAssetId = string.IsNullOrEmpty(mux_asset_id) ? null : mux_asset_id;
            if (duration.HasValue) lesson.Duration = duration.Value;
            if (sort_order.HasValue) lesson.SortOrder = sort_order.Value;
            if (is_preview.HasValue) lesson.IsPreview = is_preview.Value;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                lesson_id = lesson.LessonId,
                title = lesson.Title,
                mux_playback_id = lesson.MuxPlaybackId ?? "",
                duration = lesson.Duration
            });
        }

        [HttpDelete("lessons/{lesson_id:int}")]
        public async Task<IActionResult> DeleteLesson(int lesson_id)
        {
            var lesson = await _context.Lessons
                .Include(l => l.Module)
                .FirstOrDefaultAsync(l => l.LessonId == lesson_id);

            if (lesson == null)
            {
                return NotFound(new { detail = "Bài học không tồn tại" });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == lesson.Module!.CourseId);
            if (product != null)
            {
                try
                {
                    AssertOwner(product);
                }
                catch (UnauthorizedAccessException ex)
                {
                    return Forbid(ex.Message);
                }

                if (product.Status == "pending_approval")
                {
                    return BadRequest(new { detail = "Không thể xóa bài học khi đang chờ duyệt" });
                }
            }

            var courseId = lesson.Module?.CourseId ?? 0;
            _context.Lessons.Remove(lesson);
            await _context.SaveChangesAsync();

            if (courseId > 0)
            {
                await RecalcTotalLessons(courseId);
            }

            return Ok(new { message = "Đã xóa bài học" });
        }

        private async Task RecalcTotalLessons(int courseId)
        {
            var count = await _context.Lessons
                .Join(_context.Modules, l => l.ModuleId, m => m.ModuleId, (l, m) => new { l, m })
                .Where(lm => lm.m.CourseId == courseId)
                .CountAsync();

            var course = await _context.Courses.FirstOrDefaultAsync(c => c.ProductId == courseId);
            if (course != null)
            {
                course.TotalLessons = count;
                await _context.SaveChangesAsync();
            }
        }
    }
}
