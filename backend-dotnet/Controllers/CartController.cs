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
    [Route("api/cart")]
    [Authorize]
    public class CartController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CartController(AppDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        private async Task<Cart> GetOrCreateCartAsync(int userId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null)
            {
                cart = new Cart
                {
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Carts.Add(cart);
                await _context.SaveChangesAsync();
            }
            return cart;
        }

        private object BuildCartResponse(Cart cart)
        {
            var items = cart.Items.Select(item => new
            {
                cart_item_id = item.CartItemId,
                product_id = item.ProductId,
                product_name = item.Product?.Name,
                product_thumbnail = item.Product?.ThumbnailUrl,
                product_type = item.Product?.ProductType,
                quantity = item.Quantity,
                price = item.Price
            }).ToList();

            var subtotal = cart.Items.Sum(item => item.Price * item.Quantity);

            return new
            {
                cart_id = cart.CartId,
                items = items,
                subtotal = subtotal,
                item_count = items.Count
            };
        }

        [HttpGet("")]
        public async Task<IActionResult> GetCart()
        {
            var userId = GetCurrentUserId();
            var cart = await _context.Carts
                .Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null)
            {
                return Ok(new
                {
                    cart_id = 0,
                    items = new List<object>(),
                    subtotal = 0m,
                    item_count = 0
                });
            }
            return Ok(BuildCartResponse(cart));
        }

        [HttpPost("")]
        public async Task<IActionResult> AddToCart([FromBody] AddToCartDto dto)
        {
            var product = await _context.Products
                .FirstOrDefaultAsync(p => p.ProductId == dto.ProductId && p.Status == "active");

            if (product == null)
            {
                return NotFound(new { detail = "Sản phẩm không tồn tại" });
            }

            var userId = GetCurrentUserId();

            // Check if already purchased
            var alreadyPurchased = await _context.UserAccesses
                .AnyAsync(a => a.UserId == userId && a.ProductId == dto.ProductId && a.IsActive);

            if (alreadyPurchased)
            {
                return Conflict(new { detail = "Bạn đã sở hữu sản phẩm này rồi" });
            }

            var cart = await GetOrCreateCartAsync(userId);

            var existingItem = cart.Items.FirstOrDefault(i => i.ProductId == dto.ProductId);
            if (existingItem != null)
            {
                return Conflict(new { detail = "Sản phẩm đã có trong giỏ hàng" });
            }

            var newItem = new CartItem
            {
                CartId = cart.CartId,
                ProductId = dto.ProductId,
                Quantity = 1,
                Price = product.Price
            };
            _context.CartItems.Add(newItem);
            await _context.SaveChangesAsync();

            // Reload cart to include the new product details
            var reloadedCart = await _context.Carts
                .Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstAsync(c => c.CartId == cart.CartId);

            return Ok(BuildCartResponse(reloadedCart));
        }

        [HttpDelete("{product_id:int}")]
        public async Task<IActionResult> RemoveFromCart(int product_id)
        {
            var userId = GetCurrentUserId();
            var cart = await _context.Carts
                .Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null)
            {
                return NotFound(new { detail = "Giỏ hàng trống" });
            }

            var item = cart.Items.FirstOrDefault(i => i.ProductId == product_id);
            if (item == null)
            {
                return NotFound(new { detail = "Sản phẩm không có trong giỏ hàng" });
            }

            _context.CartItems.Remove(item);
            await _context.SaveChangesAsync();

            // Reload
            var reloadedCart = await _context.Carts
                .Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstAsync(c => c.CartId == cart.CartId);

            return Ok(BuildCartResponse(reloadedCart));
        }

        [HttpDelete("")]
        public async Task<IActionResult> ClearCart()
        {
            var userId = GetCurrentUserId();
            var cart = await _context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart != null && cart.Items.Any())
            {
                _context.CartItems.RemoveRange(cart.Items);
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Giỏ hàng đã được xóa" });
        }
    }

    public class AddToCartDto
    {
        public int ProductId { get; set; }
    }
}
