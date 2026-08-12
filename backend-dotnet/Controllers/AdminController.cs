using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using ELearnVN.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "admin")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IPaymentService _paymentService;

        public AdminController(
            AppDbContext context,
            INotificationService notificationService,
            IPaymentService paymentService)
        {
            _context = context;
            _notificationService = notificationService;
            _paymentService = paymentService;
        }

        // --- Stats Endpoints ---

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var today = DateTime.UtcNow.Date; // Simulating Vietnam timezone's start of day
            var totalUsers = await _context.Users.CountAsync();
            var totalProducts = await _context.Products.CountAsync(p => p.Status == "active");
            var totalOrders = await _context.Orders.CountAsync();
            var totalRevenue = await _context.Payments.Where(p => p.Status == "success").SumAsync(p => p.Amount) ?? 0m;
            var pendingOrders = await _context.Orders.CountAsync(o => o.Status == "pending");
            var paidOrders = await _context.Orders.CountAsync(o => o.Status == "paid");
            var newUsersToday = await _context.Users.CountAsync(u => u.CreatedAt >= today);
            var revenueToday = await _context.Payments.Where(p => p.Status == "success" && p.PaidAt >= today).SumAsync(p => p.Amount) ?? 0m;

            return Ok(new
            {
                total_users = totalUsers,
                total_products = totalProducts,
                total_orders = totalOrders,
                total_revenue = totalRevenue,
                pending_orders = pendingOrders,
                paid_orders = paidOrders,
                new_users_today = newUsersToday,
                revenue_today = revenueToday
            });
        }

        [HttpGet("stats/revenue-chart")]
        public async Task<IActionResult> RevenueChart([FromQuery] int days = 7, [FromQuery] string? period = null)
        {
            var today = DateTime.UtcNow.Date;
            var result = new List<object>();

            if (period == "week")
            {
                // Current week (Mon - today)
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                var startOfWeek = today.AddDays(-1 * diff);
                var numDays = (today - startOfWeek).Days + 1;
                for (int i = 0; i < numDays; i++)
                {
                    var day = startOfWeek.AddDays(i);
                    var nextDay = day.AddDays(1);
                    var revenue = await _context.Payments
                        .Where(p => p.Status == "success" && p.PaidAt >= day && p.PaidAt < nextDay)
                        .SumAsync(p => p.Amount) ?? 0m;

                    result.Add(new { date = day.ToString("dd/MM"), revenue = (double)revenue });
                }
            }
            else if (period == "month")
            {
                // Group by week of the current month
                var firstDay = new DateTime(today.Year, today.Month, 1);
                var lastDay = firstDay.AddMonths(1).AddDays(-1);
                var weekStart = firstDay;
                int weekNum = 1;
                while (weekStart <= today)
                {
                    var weekEnd = weekStart.AddDays(6) > today ? today : weekStart.AddDays(6);
                    var nextOfEnd = weekEnd.AddDays(1);
                    var revenue = await _context.Payments
                        .Where(p => p.Status == "success" && p.PaidAt >= weekStart && p.PaidAt < nextOfEnd)
                        .SumAsync(p => p.Amount) ?? 0m;

                    result.Add(new
                    {
                        date = $"Tuần {weekNum} ({weekStart:dd/MM}–{weekEnd:dd/MM})",
                        revenue = (double)revenue
                    });
                    weekStart = weekStart.AddDays(7);
                    weekNum++;
                }
            }
            else if (period == "year")
            {
                // Group by month of the current year
                for (int m = 1; m <= today.Month; m++)
                {
                    var startOfMonth = new DateTime(today.Year, m, 1);
                    var endOfMonth = startOfMonth.AddMonths(1);
                    var revenue = await _context.Payments
                        .Where(p => p.Status == "success" && p.PaidAt >= startOfMonth && p.PaidAt < endOfMonth)
                        .SumAsync(p => p.Amount) ?? 0m;

                    result.Add(new
                    {
                        date = $"Tháng {m}/{today.Year}",
                        revenue = (double)revenue
                    });
                }
            }
            else
            {
                // Default: by day
                var numDays = Math.Min(Math.Max(days, 7), 90);
                for (int i = numDays - 1; i >= 0; i--)
                {
                    var day = today.AddDays(-i);
                    var nextDay = day.AddDays(1);
                    var revenue = await _context.Payments
                        .Where(p => p.Status == "success" && p.PaidAt >= day && p.PaidAt < nextDay)
                        .SumAsync(p => p.Amount) ?? 0m;

                    result.Add(new { date = day.ToString("dd/MM"), revenue = (double)revenue });
                }
            }

            return Ok(result);
        }

        [HttpGet("stats/top-products")]
        public async Task<IActionResult> TopProducts([FromQuery] int limit = 5)
        {
            var products = await _context.Products
                .Where(p => p.Status == "active")
                .OrderByDescending(p => p.TotalEnrolled)
                .Take(limit)
                .AsNoTracking()
                .ToListAsync();

            return Ok(products.Select(p => new
            {
                product_id = p.ProductId,
                name = p.Name,
                total_enrolled = p.TotalEnrolled,
                average_rating = (double)p.AverageRating,
                product_type = p.ProductType
            }));
        }

        // --- Users Endpoints ---

        [HttpGet("users")]
        public async Task<IActionResult> ListUsers(
            [FromQuery] int page = 1,
            [FromQuery] int page_size = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? role = null,
            [FromQuery] string? status = null)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 100) page_size = 20;

            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(u => u.Name.Contains(search) || u.Email.Contains(search));
            }
            if (!string.IsNullOrEmpty(role))
            {
                query = query.Where(u => u.Role == role);
            }
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(u => u.Status == status);
            }

            var total = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .AsNoTracking()
                .ToListAsync();

            return Ok(new
            {
                users = users,
                total = total,
                page = page,
                page_size = page_size
            });
        }

        [HttpPut("users/{user_id:int}")]
        public async Task<IActionResult> UpdateUser(int user_id, [FromBody] UserAdminUpdateDto dto)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == user_id);
            if (user == null)
            {
                return NotFound(new { detail = "Người dùng không tồn tại" });
            }

            if (dto.Name != null) user.Name = dto.Name;
            if (dto.Role != null)
            {
                if (dto.Role != "learner" && dto.Role != "admin" && dto.Role != "author")
                {
                    return BadRequest(new { detail = "Role không hợp lệ" });
                }
                user.Role = dto.Role;
            }
            if (dto.Status != null)
            {
                if (dto.Status != "active" && dto.Status != "suspended")
                {
                    return BadRequest(new { detail = "Status không hợp lệ" });
                }
                user.Status = dto.Status;
            }
            if (dto.Phone != null) user.Phone = dto.Phone;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(user);
        }

        [HttpDelete("users/{user_id:int}")]
        public async Task<IActionResult> DeleteUser(int user_id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == user_id);
            if (user == null)
            {
                return NotFound(new { detail = "Người dùng không tồn tại" });
            }

            user.Status = "suspended";
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tài khoản đã bị vô hiệu hóa" });
        }

        // --- Products Endpoints ---

        [HttpGet("products")]
        public async Task<IActionResult> AdminListProducts(
            [FromQuery] int page = 1,
            [FromQuery] int page_size = 20,
            [FromQuery] string? search = null,
            [FromQuery] string? product_type = null,
            [FromQuery] string? status = null)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 100) page_size = 20;

            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.Contains(search));
            }
            if (!string.IsNullOrEmpty(product_type))
            {
                query = query.Where(p => p.ProductType == product_type);
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
                .AsNoTracking()
                .ToListAsync();

            var result = products.Select(p => new
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
                category = p.Category,
                author_name = p.Author?.Name,
                level = p.Course?.Level,
                duration = p.Course?.Duration
            });

            return Ok(new
            {
                products = result,
                total = total,
                page = page,
                page_size = page_size
            });
        }

        [HttpPost("products")]
        public async Task<IActionResult> CreateProduct([FromBody] ProductCreateDto dto)
        {
            if (dto.ProductType != "course" && dto.ProductType != "ebook")
            {
                return BadRequest(new { detail = "product_type phải là 'course' hoặc 'ebook'" });
            }

            var adminIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int.TryParse(adminIdStr, out var adminId);

            var product = new Product
            {
                CategoryId = dto.CategoryId,
                Name = dto.Name,
                Price = dto.Price,
                OriginalPrice = dto.OriginalPrice,
                Description = dto.Description,
                ShortDescription = dto.ShortDescription,
                ThumbnailUrl = dto.ThumbnailUrl,
                Status = dto.Status ?? "active",
                ProductType = dto.ProductType,
                AuthorId = adminId,
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

            return Ok(new
            {
                product_id = product.ProductId,
                name = product.Name,
                price = product.Price,
                original_price = product.OriginalPrice,
                thumbnail_url = product.ThumbnailUrl,
                product_type = product.ProductType,
                status = product.Status,
                average_rating = product.AverageRating,
                review_count = product.ReviewCount,
                total_enrolled = product.TotalEnrolled
            });
        }

        [HttpPut("products/{product_id:int}")]
        public async Task<IActionResult> UpdateProduct(int product_id, [FromBody] ProductUpdateDto dto)
        {
            var product = await _context.Products
                .Include(p => p.Course)
                .Include(p => p.Ebook)
                .FirstOrDefaultAsync(p => p.ProductId == product_id);

            if (product == null)
            {
                return NotFound(new { detail = "Sản phẩm không tồn tại" });
            }

            if (dto.Name != null) product.Name = dto.Name;
            if (dto.Price.HasValue) product.Price = dto.Price.Value;
            if (dto.OriginalPrice.HasValue) product.OriginalPrice = dto.OriginalPrice.Value;
            if (dto.Description != null) product.Description = dto.Description;
            if (dto.ShortDescription != null) product.ShortDescription = dto.ShortDescription;
            if (dto.ThumbnailUrl != null) product.ThumbnailUrl = dto.ThumbnailUrl;
            if (dto.Status != null) product.Status = dto.Status;
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

            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                product_id = product.ProductId,
                name = product.Name,
                price = product.Price,
                original_price = product.OriginalPrice,
                thumbnail_url = product.ThumbnailUrl,
                product_type = product.ProductType,
                status = product.Status,
                average_rating = product.AverageRating,
                review_count = product.ReviewCount,
                total_enrolled = product.TotalEnrolled
            });
        }

        [HttpDelete("products/{product_id:int}")]
        public async Task<IActionResult> DeleteProduct(int product_id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product == null)
            {
                return NotFound(new { detail = "Sản phẩm không tồn tại" });
            }

            product.Status = "archived";
            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Sản phẩm đã được ẩn (archived)" });
        }

        [HttpDelete("products/{product_id:int}/hard")]
        public async Task<IActionResult> HardDeleteProduct(int product_id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product == null)
            {
                return NotFound(new { detail = "Sản phẩm không tồn tại" });
            }

            var hasPurchases = await _context.OrderItems.AnyAsync(oi => oi.ProductId == product_id);
            if (hasPurchases)
            {
                return BadRequest(new { detail = "Không thể xóa hẳn vì sản phẩm này đã có lượt mua. Vui lòng dùng chức năng 'Ẩn'." });
            }

            // Remove dependents manually
            var cartItems = await _context.CartItems.Where(ci => ci.ProductId == product_id).ToListAsync();
            _context.CartItems.RemoveRange(cartItems);

            var wishlists = await _context.Wishlists.Where(w => w.ProductId == product_id).ToListAsync();
            _context.Wishlists.RemoveRange(wishlists);

            var reviews = await _context.Reviews.Where(r => r.ProductId == product_id).ToListAsync();
            _context.Reviews.RemoveRange(reviews);

            var accesses = await _context.UserAccesses.Where(a => a.ProductId == product_id).ToListAsync();
            _context.UserAccesses.RemoveRange(accesses);

            if (product.ProductType == "course")
            {
                var course = await _context.Courses
                    .Include(c => c!.Modules).ThenInclude(m => m.Lessons)
                    .FirstOrDefaultAsync(c => c.ProductId == product_id);

                if (course != null)
                {
                    foreach (var m in course.Modules)
                    {
                        _context.Lessons.RemoveRange(m.Lessons);
                    }
                    _context.Modules.RemoveRange(course.Modules);
                    _context.Courses.Remove(course);
                }
            }
            else
            {
                var ebook = await _context.Ebooks.FirstOrDefaultAsync(e => e.ProductId == product_id);
                if (ebook != null)
                {
                    _context.Ebooks.Remove(ebook);
                }
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Sản phẩm đã bị xóa vĩnh viễn khỏi hệ thống" });
        }

        // --- Course Content (Modules & Lessons) Management ---

        [HttpGet("courses/{product_id:int}/content")]
        public async Task<IActionResult> GetCourseContentAdmin(int product_id)
        {
            var product = await _context.Products
                .Include(p => p.Course).ThenInclude(c => c!.Modules).ThenInclude(m => m.Lessons)
                .FirstOrDefaultAsync(p => p.ProductId == product_id && p.ProductType == "course");

            if (product == null || product.Course == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
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

            return Ok(new
            {
                product_id = product_id,
                name = product.Name,
                total_lessons = product.Course.TotalLessons,
                modules = modules
            });
        }

        [HttpPost("courses/{product_id:int}/modules")]
        public async Task<IActionResult> CreateModule(int product_id, [FromQuery] string title, [FromQuery] int sort_order = 0)
        {
            var course = await _context.Courses.FirstOrDefaultAsync(c => c.ProductId == product_id);
            if (course == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
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
                sort_order = module.SortOrder
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

            var courseId = module.CourseId;
            // Lessons will cascade delete automatically due to EF Core configuration
            _context.Modules.Remove(module);
            await _context.SaveChangesAsync();

            await RecalcTotalLessons(courseId);
            return Ok(new { message = "Đã xóa module" });
        }

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
            var lesson = await _context.Lessons.FirstOrDefaultAsync(l => l.LessonId == lesson_id);
            if (lesson == null)
            {
                return NotFound(new { detail = "Bài học không tồn tại" });
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

        // --- Course Approval Endpoints ---

        [HttpGet("courses/pending")]
        public async Task<IActionResult> ListPendingCourses([FromQuery] int page = 1, [FromQuery] int page_size = 20)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 100) page_size = 20;

            var query = _context.Products
                .Include(p => p.Author)
                .Include(p => p.Category)
                .Include(p => p.Course)
                .Where(p => p.Status == "pending_approval");

            var total = await query.CountAsync();
            var products = await query
                .OrderByDescending(p => p.UpdatedAt)
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .AsNoTracking()
                .ToListAsync();

            var result = products.Select(p => new
            {
                product_id = p.ProductId,
                name = p.Name,
                product_type = p.ProductType,
                status = p.Status,
                price = (double)p.Price,
                thumbnail_url = p.ThumbnailUrl,
                author_id = p.AuthorId,
                author_name = p.Author?.Name,
                author_email = p.Author?.Email,
                category = p.Category?.Name,
                level = p.Course?.Level,
                total_lessons = p.Course?.TotalLessons ?? 0,
                created_at = p.CreatedAt,
                updated_at = p.UpdatedAt
            });

            return Ok(new { courses = result, total = total, page = page, page_size = page_size });
        }

        [HttpPost("courses/{product_id:int}/approve")]
        public async Task<IActionResult> ApproveCourse(int product_id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            if (product.Status != "pending_approval")
            {
                return BadRequest(new { detail = $"Chỉ duyệt được khóa học đang chờ duyệt (hiện tại: {product.Status})" });
            }

            product.Status = "active";
            product.RejectionReason = null;
            product.UpdatedAt = DateTime.UtcNow;

            if (product.AuthorId.HasValue)
            {
                await _notificationService.NotifyCourseApprovedAsync(product.AuthorId.Value, product_id, product.Name);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Đã duyệt khóa học '{product.Name}'. Khóa học đã được đăng công khai!" });
        }

        [HttpPost("courses/{product_id:int}/reject")]
        public async Task<IActionResult> RejectCourse(int product_id, [FromQuery] string reason)
        {
            if (string.IsNullOrEmpty(reason) || string.IsNullOrEmpty(reason.Trim()))
            {
                return BadRequest(new { detail = "Phải nhập lý do từ chối" });
            }

            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            if (product.Status != "pending_approval")
            {
                return BadRequest(new { detail = $"Chỉ từ chối khóa học đang chờ duyệt (hiện tại: {product.Status})" });
            }

            product.Status = "rejected";
            product.RejectionReason = reason.Trim();
            product.UpdatedAt = DateTime.UtcNow;

            if (product.AuthorId.HasValue)
            {
                await _notificationService.NotifyCourseRejectedAsync(product.AuthorId.Value, product_id, product.Name, reason.Trim());
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã từ chối khóa học. Tác giả đã được thông báo để chỉnh sửa." });
        }

        [HttpPost("courses/{product_id:int}/unpublish")]
        public async Task<IActionResult> UnpublishCourse(int product_id)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product == null)
            {
                return NotFound(new { detail = "Khóa học không tồn tại" });
            }

            product.Status = "inactive";
            product.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Đã gỡ khóa học '{product.Name}' khỏi hệ thống." });
        }

        // --- Orders Management Endpoints ---

        [HttpGet("orders")]
        public async Task<IActionResult> AdminListOrders(
            [FromQuery] int page = 1,
            [FromQuery] int page_size = 20,
            [FromQuery] string? status = null,
            [FromQuery] int? user_id = null)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 100) page_size = 20;

            var query = _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payment)
                .Include(o => o.User)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }
            if (user_id.HasValue)
            {
                query = query.Where(o => o.UserId == user_id.Value);
            }

            var total = await query.CountAsync();
            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .ToListAsync();

            var result = orders.Select(o => new
            {
                order_id = o.OrderId,
                user_id = o.UserId,
                user_name = o.User?.Name,
                user_email = o.User?.Email,
                coupon_code = o.CouponCode,
                subtotal = o.Subtotal,
                discount_amount = o.DiscountAmount,
                total_amount = o.TotalAmount,
                status = o.Status,
                created_at = o.CreatedAt,
                items = o.Items.Select(i => new
                {
                    order_item_id = i.OrderItemId,
                    product_id = i.ProductId,
                    product_name = i.Product?.Name,
                    product_thumbnail = i.Product?.ThumbnailUrl,
                    product_type = i.Product?.ProductType,
                    quantity = i.Quantity,
                    price = i.Price
                }),
                payment = o.Payment != null ? new
                {
                    payment_id = o.Payment.PaymentId,
                    method = o.Payment.Method,
                    status = o.Payment.Status,
                    transaction_id = o.Payment.TransactionId,
                    paid_at = o.Payment.PaidAt,
                    amount = o.Payment.Amount
                } : null
            });

            return Ok(new { orders = result, total = total, page = page, page_size = page_size });
        }

        [HttpPost("orders/{order_id:int}/refund")]
        public async Task<IActionResult> RefundOrder(int order_id)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.OrderId == order_id);

            if (order == null)
            {
                return NotFound(new { detail = "Đơn hàng không tồn tại" });
            }

            if (order.Status != "paid")
            {
                return BadRequest(new { detail = "Chỉ có thể hoàn tiền đơn hàng đã thanh toán" });
            }

            order.Status = "refunded";
            order.UpdatedAt = DateTime.UtcNow;
            if (order.Payment != null)
            {
                order.Payment.Status = "refunded";
            }

            // Revoke access to all products
            var accessList = await _context.UserAccesses
                .Where(a => a.UserId == order.UserId && a.OrderId == order_id)
                .ToListAsync();

            foreach (var access in accessList)
            {
                access.IsActive = false;
                access.RevokedAt = DateTime.UtcNow;
            }

            await _notificationService.NotifyRefundCompletedAsync(order_id, order.UserId, order.TotalAmount);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Hoàn tiền và thu hồi quyền truy cập thành công" });
        }

        [HttpPost("access/revoke/{user_id:int}/{product_id:int}")]
        public async Task<IActionResult> AdminRevokeAccess(int user_id, int product_id)
        {
            try
            {
                await _paymentService.RevokeAccessAsync(user_id, product_id);
                return Ok(new { message = "Thu hồi quyền truy cập thành công" });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { detail = ex.Message });
            }
        }

        // --- Categories Endpoints ---

        [HttpPost("categories")]
        public async Task<IActionResult> CreateCategory([FromQuery] string name, [FromQuery] string? description = null, [FromQuery] string? icon = null)
        {
            var existing = await _context.Categories.AnyAsync(c => c.Name == name);
            if (existing)
            {
                return Conflict(new { detail = "Danh mục đã tồn tại" });
            }

            var cat = new Category
            {
                Name = name,
                Description = description,
                Icon = icon
            };
            _context.Categories.Add(cat);
            await _context.SaveChangesAsync();

            return Ok(cat);
        }

        // --- Coupons Endpoints ---

        [HttpGet("coupons")]
        public async Task<IActionResult> ListCoupons()
        {
            var coupons = await _context.Coupons
                .OrderBy(c => c.Code)
                .AsNoTracking()
                .ToListAsync();

            return Ok(coupons.Select(c => new
            {
                code = c.Code,
                discount = (double)c.Discount,
                discount_type = c.DiscountType,
                min_order_amount = (double)c.MinOrderAmount,
                usage_limit = c.UsageLimit,
                used_count = c.UsedCount,
                is_active = c.IsActive,
                expired_date = c.ExpiredDate
            }));
        }

        [HttpPost("coupons")]
        public async Task<IActionResult> CreateCoupon(
            [FromQuery] string code,
            [FromQuery] decimal discount,
            [FromQuery] string discount_type = "fixed",
            [FromQuery] DateTime? expired_date = null,
            [FromQuery] int? usage_limit = null,
            [FromQuery] decimal min_order_amount = 0)
        {
            var existing = await _context.Coupons.AnyAsync(c => c.Code == code);
            if (existing)
            {
                return Conflict(new { detail = "Mã coupon đã tồn tại" });
            }

            var coupon = new Coupon
            {
                Code = code,
                Discount = discount,
                DiscountType = discount_type,
                ExpiredDate = expired_date,
                UsageLimit = usage_limit,
                MinOrderAmount = min_order_amount,
                IsActive = true
            };
            _context.Coupons.Add(coupon);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Tạo coupon thành công", code = code });
        }

        [HttpDelete("coupons/{code}")]
        public async Task<IActionResult> DeleteCoupon(string code)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code == code);
            if (coupon == null)
            {
                return NotFound(new { detail = "Coupon không tồn tại" });
            }

            _context.Coupons.Remove(coupon);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa coupon" });
        }

        // --- Author Applications Endpoints ---

        [HttpGet("author-applications")]
        public async Task<IActionResult> ListAuthorApplications([FromQuery] int page = 1, [FromQuery] int page_size = 20)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 100) page_size = 20;

            var query = _context.Users
                .Where(u => u.AuthorApplicationStatus == "pending");

            var total = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.UpdatedAt)
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .AsNoTracking()
                .ToListAsync();

            var result = users.Select(u => new
            {
                user_id = u.UserId,
                name = u.Name,
                email = u.Email,
                avatar_url = u.AvatarUrl,
                phone = u.Phone,
                created_at = u.CreatedAt,
                author_application_data = u.AuthorApplicationData
            }).ToList();

            return Ok(new { applications = result, total = total, page = page, page_size = page_size });
        }

        [HttpPost("author-applications/{user_id:int}/approve")]
        public async Task<IActionResult> ApproveAuthor(int user_id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == user_id);
            if (user == null)
            {
                return NotFound(new { detail = "Người dùng không tồn tại" });
            }

            if (user.AuthorApplicationStatus != "pending")
            {
                return BadRequest(new { detail = "Không có đơn đăng ký đang chờ duyệt" });
            }

            user.Role = "author";
            user.AuthorApplicationStatus = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _notificationService.NotifyAuthorApprovedAsync(user_id);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Đã phê duyệt {user.Name} làm Giảng viên!" });
        }

        [HttpPost("author-applications/{user_id:int}/reject")]
        public async Task<IActionResult> RejectAuthor(int user_id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == user_id);
            if (user == null)
            {
                return NotFound(new { detail = "Người dùng không tồn tại" });
            }

            if (user.AuthorApplicationStatus != "pending")
            {
                return BadRequest(new { detail = "Không có đơn đăng ký đang chờ duyệt" });
            }

            user.AuthorApplicationStatus = "rejected";
            user.UpdatedAt = DateTime.UtcNow;

            await _notificationService.NotifyAuthorRejectedAsync(user_id);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã từ chối đơn đăng ký giảng viên" });
        }
    }

    // DTOs
    public class UserAdminUpdateDto
    {
        public string? Name { get; set; }
        public string? Role { get; set; }
        public string? Status { get; set; }
        public string? Phone { get; set; }
    }

    public class ProductCreateDto
    {
        public int? CategoryId { get; set; }
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string? Description { get; set; }
        public string? ShortDescription { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? Status { get; set; }
        public string ProductType { get; set; } = null!; // course | ebook

        // Course specific
        public int? Duration { get; set; }
        public string? Level { get; set; }
        public string? Requirements { get; set; }
        public string? WhatYouLearn { get; set; }

        // Ebook specific
        public decimal? FileSize { get; set; }
        public string? Format { get; set; }
        public int? PageCount { get; set; }
    }

    public class ProductUpdateDto
    {
        public string? Name { get; set; }
        public decimal? Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string? Description { get; set; }
        public string? ShortDescription { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? Status { get; set; }
        public int? CategoryId { get; set; }

        // Course specific
        public int? Duration { get; set; }
        public string? Level { get; set; }
        public string? Requirements { get; set; }
        public string? WhatYouLearn { get; set; }

        // Ebook specific
        public decimal? FileSize { get; set; }
        public string? Format { get; set; }
        public int? PageCount { get; set; }
    }
}
