using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/products")]
    public class ProductsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProductsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .OrderBy(c => c.SortOrder)
                .AsNoTracking()
                .ToListAsync();

            return Ok(categories.Select(c => new
            {
                category_id = c.CategoryId,
                name = c.Name,
                description = c.Description,
                icon = c.Icon,
                sort_order = c.SortOrder
            }));
        }

        [HttpGet("")]
        public async Task<IActionResult> ListProducts(
            [FromQuery] int page = 1,
            [FromQuery] int page_size = 12,
            [FromQuery] string? search = null,
            [FromQuery] int? category_id = null,
            [FromQuery] string? product_type = null,
            [FromQuery] decimal? min_price = null,
            [FromQuery] decimal? max_price = null,
            [FromQuery] string? level = null,
            [FromQuery] string sort = "newest")
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 50) page_size = 12;

            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course)
                .Where(p => p.Status == "active");

            // Filter by search term (case-insensitive)
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.Contains(search));
            }

            // Filter by Category
            if (category_id.HasValue)
            {
                query = query.Where(p => p.CategoryId == category_id.Value);
            }

            // Filter by Product Type (course | ebook)
            if (!string.IsNullOrEmpty(product_type))
            {
                query = query.Where(p => p.ProductType == product_type);
            }

            // Filter by Price
            if (min_price.HasValue)
            {
                query = query.Where(p => p.Price >= min_price.Value);
            }
            if (max_price.HasValue)
            {
                query = query.Where(p => p.Price <= max_price.Value);
            }

            // Filter by Level
            if (!string.IsNullOrEmpty(level))
            {
                query = query.Where(p => p.Course != null && p.Course.Level == level);
            }

            // Sort
            query = sort switch
            {
                "oldest" => query.OrderBy(p => p.CreatedAt),
                "price_asc" => query.OrderBy(p => p.Price),
                "price_desc" => query.OrderByDescending(p => p.Price),
                "rating" => query.OrderByDescending(p => p.AverageRating),
                _ => query.OrderByDescending(p => p.CreatedAt), // default is newest
            };

            var total = await query.CountAsync();
            var products = await query
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
                category = p.Category != null ? new
                {
                    category_id = p.Category.CategoryId,
                    name = p.Category.Name,
                    description = p.Category.Description,
                    icon = p.Category.Icon,
                    sort_order = p.Category.SortOrder
                } : null,
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

        [HttpGet("{product_id:int}")]
        public async Task<IActionResult> GetProduct(int product_id)
        {
            var p = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Author)
                .Include(p => p.Course).ThenInclude(c => c!.Modules).ThenInclude(m => m.Lessons)
                .Include(p => p.Ebook)
                .Include(p => p.Reviews).ThenInclude(r => r.User)
                .FirstOrDefaultAsync(p => p.ProductId == product_id && p.Status == "active");

            if (p == null)
            {
                return NotFound(new { detail = "Sản phẩm không tồn tại" });
            }

            // Check if user has access to product contents (enrolled/purchased)
            bool hasAccess = false;
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (int.TryParse(userIdStr, out var userId))
            {
                hasAccess = await _context.UserAccesses
                    .AnyAsync(a => a.UserId == userId && a.ProductId == product_id && a.IsActive);
            }

            var reviews = p.Reviews.Select(r => new
            {
                review_id = r.ReviewId,
                user_id = r.UserId,
                user_name = r.User?.Name ?? "Ẩn danh",
                rating = r.Rating,
                comment = r.Comment,
                created_at = r.CreatedAt
            }).ToList();

            object? courseInfo = null;
            if (p.ProductType == "course" && p.Course != null)
            {
                var modules = p.Course.Modules.OrderBy(m => m.SortOrder).Select(m => new
                {
                    module_id = m.ModuleId,
                    title = m.Title,
                    sort_order = m.SortOrder,
                    lessons = m.Lessons.OrderBy(l => l.SortOrder).Select(l => new
                    {
                        lesson_id = l.LessonId,
                        title = l.Title,
                        duration = l.Duration,
                        sort_order = l.SortOrder,
                        is_preview = l.IsPreview
                    })
                });

                // Decode requirements and what_you_learn from JSON if needed
                List<string> reqs = new();
                List<string> learn = new();
                try
                {
                    if (!string.IsNullOrEmpty(p.Course.Requirements))
                        reqs = JsonSerializer.Deserialize<List<string>>(p.Course.Requirements) ?? new();
                    if (!string.IsNullOrEmpty(p.Course.WhatYouLearn))
                        learn = JsonSerializer.Deserialize<List<string>>(p.Course.WhatYouLearn) ?? new();
                }
                catch { }

                courseInfo = new
                {
                    duration = p.Course.Duration,
                    level = p.Course.Level,
                    total_lessons = p.Course.TotalLessons,
                    requirements = reqs,
                    what_you_learn = learn,
                    modules = modules
                };
            }

            object? ebookInfo = null;
            if (p.ProductType == "ebook" && p.Ebook != null)
            {
                ebookInfo = new
                {
                    file_size = p.Ebook.FileSize,
                    format = p.Ebook.Format,
                    page_count = p.Ebook.PageCount,
                    preview_pages = p.Ebook.PreviewPages
                };
            }

            return Ok(new
            {
                product_id = p.ProductId,
                name = p.Name,
                price = p.Price,
                original_price = p.OriginalPrice,
                thumbnail_url = p.ThumbnailUrl,
                product_type = p.ProductType,
                status = p.Status,
                description = p.Description,
                short_description = p.ShortDescription,
                average_rating = p.AverageRating,
                review_count = p.ReviewCount,
                total_enrolled = p.TotalEnrolled,
                created_at = p.CreatedAt,
                category = p.Category != null ? new
                {
                    category_id = p.Category.CategoryId,
                    name = p.Category.Name,
                    description = p.Category.Description,
                    icon = p.Category.Icon,
                    sort_order = p.Category.SortOrder
                } : null,
                author_name = p.Author?.Name,
                course = courseInfo,
                ebook = ebookInfo,
                reviews = reviews,
                level = p.Course?.Level,
                duration = p.Course?.Duration,
                has_access = hasAccess
            });
        }

        [HttpPost("{product_id:int}/reviews")]
        [Authorize]
        public async Task<IActionResult> CreateReview(int product_id, [FromBody] ReviewCreateDto dto)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            // Must have purchased the product to leave a review
            var hasAccess = await _context.UserAccesses
                .AnyAsync(a => a.UserId == userId && a.ProductId == product_id && a.IsActive);

            if (!hasAccess)
            {
                return BadRequest(new { detail = "Bạn phải mua sản phẩm mới có thể đánh giá" });
            }

            if (dto.Rating < 1 || dto.Rating > 5)
            {
                return BadRequest(new { detail = "Rating phải từ 1-5" });
            }

            var existing = await _context.Reviews
                .AnyAsync(r => r.ProductId == product_id && r.UserId == userId);

            if (existing)
            {
                return Conflict(new { detail = "Bạn đã đánh giá sản phẩm này rồi" });
            }

            var review = new Review
            {
                ProductId = product_id,
                UserId = userId,
                Rating = dto.Rating,
                Comment = dto.Comment,
                CreatedAt = DateTime.UtcNow
            };
            _context.Reviews.Add(review);

            // Update average rating on the Product
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id);
            if (product != null)
            {
                var allReviews = await _context.Reviews.Where(r => r.ProductId == product_id).ToListAsync();
                var total = allReviews.Sum(r => r.Rating) + dto.Rating;
                var count = allReviews.Count + 1;
                product.AverageRating = Math.Round((decimal)total / count, 2);
                product.ReviewCount = count;
            }

            await _context.SaveChangesAsync();

            var user = await _context.Users.AsNoTracking().FirstAsync(u => u.UserId == userId);

            return Ok(new
            {
                review_id = review.ReviewId,
                user_id = review.UserId,
                user_name = user.Name,
                rating = review.Rating,
                comment = review.Comment,
                created_at = review.CreatedAt
            });
        }
    }

    public class ReviewCreateDto
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
    }
}
