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
    [Route("api/orders")]
    [Authorize]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;

        public OrdersController(AppDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        private decimal ComputeDiscount(Coupon coupon, decimal subtotal)
        {
            if (!coupon.IsActive) return 0;
            if (coupon.ExpiredDate.HasValue && coupon.ExpiredDate.Value < DateTime.UtcNow) return 0;
            if (coupon.MinOrderAmount > subtotal) return 0;

            if (coupon.DiscountType == "percent")
            {
                return Math.Round(subtotal * coupon.Discount / 100m, 0);
            }
            return Math.Min(coupon.Discount, subtotal);
        }

        private void AssertCouponEligible(Coupon coupon, decimal subtotal)
        {
            if (!coupon.IsActive)
            {
                throw new InvalidOperationException("Mã giảm giá không hợp lệ");
            }
            if (coupon.ExpiredDate.HasValue && coupon.ExpiredDate.Value < DateTime.UtcNow)
            {
                throw new InvalidOperationException("Mã giảm giá đã hết hạn");
            }
            if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit.Value)
            {
                throw new InvalidOperationException("Mã giảm giá đã hết lượt sử dụng");
            }
            if (coupon.MinOrderAmount > subtotal)
            {
                throw new InvalidOperationException($"Đơn hàng tối thiểu phải đạt {coupon.MinOrderAmount:N0}đ");
            }
        }

        [HttpPost("validate-coupon")]
        public async Task<IActionResult> ValidateCoupon([FromBody] CouponValidateDto dto)
        {
            var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code == dto.Code);
            if (coupon == null || !coupon.IsActive)
            {
                return NotFound(new { detail = "Mã giảm giá không hợp lệ" });
            }

            try
            {
                AssertCouponEligible(coupon, dto.OrderAmount);
                var discount = ComputeDiscount(coupon, dto.OrderAmount);
                return Ok(new
                {
                    valid = true,
                    discount = discount,
                    discount_type = coupon.DiscountType
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { detail = ex.Message });
            }
        }

        [HttpPost("")]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
        {
            var userId = GetCurrentUserId();
            var cart = await _context.Carts
                .Include(c => c.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.UserId == userId);

            if (cart == null || !cart.Items.Any())
            {
                return BadRequest(new { detail = "Giỏ hàng trống" });
            }

            var subtotal = cart.Items.Sum(item => item.Price * item.Quantity);
            decimal discount = 0;
            Coupon? coupon = null;

            if (!string.IsNullOrEmpty(dto.CouponCode))
            {
                coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code == dto.CouponCode);
                if (coupon == null)
                {
                    return BadRequest(new { detail = "Mã giảm giá không tồn tại" });
                }

                try
                {
                    AssertCouponEligible(coupon, subtotal);
                    discount = ComputeDiscount(coupon, subtotal);
                    coupon.UsedCount += 1;
                }
                catch (InvalidOperationException ex)
                {
                    return BadRequest(new { detail = ex.Message });
                }
            }

            var total = Math.Max(subtotal - discount, 0m);

            var order = new Order
            {
                UserId = userId,
                CouponCode = coupon?.Code,
                Subtotal = subtotal,
                DiscountAmount = discount,
                TotalAmount = total,
                Status = "pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.Orders.Add(order);
            await _context.SaveChangesAsync(); // Generates OrderId

            foreach (var item in cart.Items)
            {
                var orderItem = new OrderItem
                {
                    OrderId = order.OrderId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    Price = item.Price
                };
                _context.OrderItems.Add(orderItem);
            }

            var payment = new Payment
            {
                OrderId = order.OrderId,
                Status = "pending",
                Method = "vnpay", // default payment method
                Amount = total
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            // Reload Order with loaded relations to return
            var reloadedOrder = await _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payment)
                .FirstAsync(o => o.OrderId == order.OrderId);

            return Ok(BuildOrderResponse(reloadedOrder));
        }

        [HttpGet("")]
        public async Task<IActionResult> ListOrders([FromQuery] int page = 1, [FromQuery] int page_size = 10)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 50) page_size = 10;

            var userId = GetCurrentUserId();
            var query = _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payment)
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt);

            var total = await query.CountAsync();
            var orders = await query
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .ToListAsync();

            var result = orders.Select(BuildOrderResponse).ToList();

            return Ok(new
            {
                orders = result,
                total = total,
                page = page,
                page_size = page_size
            });
        }

        [HttpGet("{order_id:int}")]
        public async Task<IActionResult> GetOrder(int order_id)
        {
            var userId = GetCurrentUserId();
            var order = await _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.OrderId == order_id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { detail = "Đơn hàng không tồn tại" });
            }

            return Ok(BuildOrderResponse(order));
        }

        [HttpDelete("{order_id:int}")]
        public async Task<IActionResult> CancelOrder(int order_id)
        {
            var userId = GetCurrentUserId();
            var order = await _context.Orders
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.OrderId == order_id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { detail = "Đơn hàng không tồn tại" });
            }

            if (order.Status != "pending")
            {
                return BadRequest(new { detail = "Chỉ có thể hủy đơn hàng đang chờ thanh toán" });
            }

            order.Status = "cancelled";
            order.UpdatedAt = DateTime.UtcNow;
            if (order.Payment != null)
            {
                order.Payment.Status = "cancelled";
            }

            await _notificationService.NotifyOrderCancelledAsync(order_id, userId);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã hủy đơn hàng thành công" });
        }

        [HttpPost("{order_id:int}/refund-request")]
        public async Task<IActionResult> RequestRefund(int order_id)
        {
            var userId = GetCurrentUserId();
            var order = await _context.Orders
                .Include(o => o.Items).ThenInclude(i => i.Product)
                .Include(o => o.Payment)
                .FirstOrDefaultAsync(o => o.OrderId == order_id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { detail = "Đơn hàng không tồn tại" });
            }

            if (order.Status != "paid")
            {
                return BadRequest(new { detail = "Chỉ có thể hoàn tiền đơn hàng đã thanh toán" });
            }

            // Check 3 days policy
            var paidAt = order.Payment?.PaidAt;
            if (!paidAt.HasValue)
            {
                return BadRequest(new { detail = "Không tìm thấy thông tin thanh toán" });
            }

            if (DateTime.UtcNow - paidAt.Value > TimeSpan.FromDays(3))
            {
                return BadRequest(new { detail = "Đã quá 3 ngày kể từ khi thanh toán, không thể yêu cầu hoàn tiền" });
            }

            // Check products requirements
            foreach (var item in order.Items)
            {
                var product = item.Product;
                if (product == null) continue;

                if (product.ProductType == "course")
                {
                    // Total lessons in course
                    var totalLessons = await _context.Lessons
                        .Join(_context.Modules, l => l.ModuleId, m => m.ModuleId, (l, m) => new { l, m })
                        .Where(lm => lm.m.CourseId == product.ProductId)
                        .CountAsync();

                    // Completed lessons by user
                    var completedLessons = await _context.LearningProgresses
                        .Join(_context.Lessons, p => p.LessonId, l => l.LessonId, (p, l) => new { p, l })
                        .Join(_context.Modules, pl => pl.l.ModuleId, m => m.ModuleId, (pl, m) => new { pl.p, pl.l, m })
                        .Where(plm => plm.m.CourseId == product.ProductId && plm.p.UserId == userId && plm.p.Completed)
                        .CountAsync();

                    if (totalLessons > 0)
                    {
                        var progressPct = (double)completedLessons / totalLessons;
                        if (progressPct >= 0.1)
                        {
                            return BadRequest(new
                            {
                                detail = $"Bạn đã hoàn thành {completedLessons}/{totalLessons} bài học " +
                                         $"({progressPct * 100:F0}%), vượt quá 10% không thể hoàn tiền"
                            });
                        }
                    }
                }
                else if (product.ProductType == "ebook")
                {
                    // If ebook accessed (opened), deny refund
                    var access = await _context.UserAccesses
                        .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == product.ProductId && a.IsActive);

                    if (access != null && access.AccessedAt.HasValue)
                    {
                        return BadRequest(new { detail = $"Bạn đã mở ebook \"{product.Name}\", không thể hoàn tiền" });
                    }
                }
            }

            // Execute refund logic
            order.Status = "refunded";
            order.UpdatedAt = DateTime.UtcNow;
            if (order.Payment != null)
            {
                order.Payment.Status = "refunded";
            }

            // Revoke access
            var accessList = await _context.UserAccesses
                .Where(a => a.UserId == userId && a.OrderId == order_id)
                .ToListAsync();

            foreach (var access in accessList)
            {
                access.IsActive = false;
                access.RevokedAt = DateTime.UtcNow;
            }

            await _notificationService.NotifyRefundRequestedAsync(order_id, userId, order.TotalAmount);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Yêu cầu hoàn tiền thành công. Quyền truy cập đã được thu hồi." });
        }

        private object BuildOrderResponse(Order o)
        {
            var items = o.Items.Select(i => new
            {
                order_item_id = i.OrderItemId,
                product_id = i.ProductId,
                product_name = i.Product?.Name,
                product_thumbnail = i.Product?.ThumbnailUrl,
                product_type = i.Product?.ProductType,
                quantity = i.Quantity,
                price = i.Price
            }).ToList();

            object? payment = null;
            if (o.Payment != null)
            {
                payment = new
                {
                    payment_id = o.Payment.PaymentId,
                    method = o.Payment.Method,
                    status = o.Payment.Status,
                    transaction_id = o.Payment.TransactionId,
                    paid_at = o.Payment.PaidAt,
                    amount = o.Payment.Amount
                };
            }

            return new
            {
                order_id = o.OrderId,
                user_id = o.UserId,
                coupon_code = o.CouponCode,
                subtotal = o.Subtotal,
                discount_amount = o.DiscountAmount,
                total_amount = o.TotalAmount,
                status = o.Status,
                created_at = o.CreatedAt,
                items = items,
                payment = payment
            };
        }
    }

    public class CouponValidateDto
    {
        public string Code { get; set; } = null!;
        public decimal OrderAmount { get; set; }
    }

    public class CreateOrderDto
    {
        public string? CouponCode { get; set; }
    }
}
