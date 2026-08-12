using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/wishlist")]
    [Authorize]
    public class WishlistController : ControllerBase
    {
        private readonly AppDbContext _context;

        public WishlistController(AppDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetWishlist()
        {
            var userId = GetCurrentUserId();
            var items = await _context.Wishlists
                .Include(w => w.Product).ThenInclude(p => p!.Category)
                .Include(w => w.Product).ThenInclude(p => p!.Author)
                .Include(w => w.Product).ThenInclude(p => p!.Course)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.AddedAt)
                .ToListAsync();

            var result = items.Select(w => new
            {
                wishlist_id = w.WishlistId,
                product_id = w.Product!.ProductId,
                name = w.Product.Name,
                price = (double)w.Product.Price,
                original_price = (double?)w.Product.OriginalPrice,
                thumbnail_url = w.Product.ThumbnailUrl,
                product_type = w.Product.ProductType,
                average_rating = (double)w.Product.AverageRating,
                review_count = w.Product.ReviewCount,
                total_enrolled = w.Product.TotalEnrolled,
                category = w.Product.Category != null ? new { name = w.Product.Category.Name, icon = w.Product.Category.Icon } : null,
                author_name = w.Product.Author?.Name,
                level = w.Product.Course?.Level,
                added_at = w.AddedAt
            });

            return Ok(result);
        }

        [HttpPost("{product_id:int}")]
        public async Task<IActionResult> AddToWishlist(int product_id)
        {
            var userId = GetCurrentUserId();
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == product_id && p.Status == "active");
            if (product == null)
            {
                return NotFound(new { detail = "Sản phẩm không tồn tại" });
            }

            var existing = await _context.Wishlists.FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == product_id);
            if (existing != null)
            {
                return Conflict(new { detail = "Sản phẩm đã có trong danh sách yêu thích" });
            }

            var wishlist = new Wishlist
            {
                UserId = userId,
                ProductId = product_id,
                AddedAt = DateTime.UtcNow
            };
            _context.Wishlists.Add(wishlist);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã thêm vào yêu thích", wishlist_id = wishlist.WishlistId });
        }

        [HttpDelete("{product_id:int}")]
        public async Task<IActionResult> RemoveFromWishlist(int product_id)
        {
            var userId = GetCurrentUserId();
            var item = await _context.Wishlists.FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == product_id);
            if (item == null)
            {
                return NotFound(new { detail = "Không tìm thấy trong danh sách yêu thích" });
            }

            _context.Wishlists.Remove(item);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa khỏi yêu thích" });
        }

        [HttpGet("check/{product_id:int}")]
        public async Task<IActionResult> CheckWishlist(int product_id)
        {
            var userId = GetCurrentUserId();
            var item = await _context.Wishlists.AnyAsync(w => w.UserId == userId && w.ProductId == product_id);
            return Ok(new { is_wishlisted = item });
        }
    }
}
