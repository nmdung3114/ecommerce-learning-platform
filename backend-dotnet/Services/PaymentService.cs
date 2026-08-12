using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Services
{
    public interface IPaymentService
    {
        Task<PaymentProcessResult> ProcessVnPayReturnAsync(Dictionary<string, string> queryParams);
        Task<PaymentProcessResult> ProcessPayPalSuccessAsync(int orderId, string paypalOrderId, string captureId, decimal usdAmount);
        Task GrantAccessAsync(Order order);
        Task RevokeAccessAsync(int userId, int productId);
        Task<bool> CheckUserHasAccessAsync(int userId, int productId);
    }

    public class PaymentProcessResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = null!;
        public int OrderId { get; set; }
        public string? Code { get; set; }
    }

    public class PaymentService : IPaymentService
    {
        private readonly AppDbContext _context;
        private readonly IVnPayService _vnPayService;
        private readonly INotificationService _notificationService;

        public PaymentService(AppDbContext context, IVnPayService vnPayService, INotificationService notificationService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _notificationService = notificationService;
        }

        public async Task<PaymentProcessResult> ProcessVnPayReturnAsync(Dictionary<string, string> queryParams)
        {
            var result = _vnPayService.VerifyCallback(queryParams);

            if (!result.IsValid)
            {
                return new PaymentProcessResult { Success = false, Message = "Chữ ký không hợp lệ", OrderId = result.OrderId };
            }

            var orderId = result.OrderId;
            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                return new PaymentProcessResult { Success = false, Message = "Đơn hàng không tồn tại", OrderId = orderId };
            }

            if (order.Status == "paid")
            {
                return new PaymentProcessResult { Success = true, Message = "Đơn hàng đã được thanh toán", OrderId = orderId };
            }

            var payment = await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
            if (payment == null)
            {
                return new PaymentProcessResult { Success = false, Message = "Không tìm thấy thông tin thanh toán", OrderId = orderId };
            }

            if (result.IsSuccess)
            {
                order.Status = "paid";
                order.UpdatedAt = DateTime.UtcNow;

                payment.Status = "success";
                payment.TransactionId = result.TransactionId;
                payment.PaidAt = DateTime.UtcNow;
                payment.Amount = result.Amount;
                payment.VnpayResponse = JsonSerializer.Serialize(result.RawParams);

                await GrantAccessAsync(order);
                await _context.SaveChangesAsync();

                // Gửi thông báo thanh toán thành công
                await _notificationService.NotifyPaymentSuccessAsync(order.OrderId, order.UserId, order.TotalAmount);

                return new PaymentProcessResult { Success = true, Message = "Thanh toán thành công", OrderId = orderId };
            }
            else
            {
                order.Status = "cancelled";
                order.UpdatedAt = DateTime.UtcNow;

                payment.Status = "failed";
                payment.VnpayResponse = JsonSerializer.Serialize(result.RawParams);

                await _context.SaveChangesAsync();

                // Gửi thông báo hủy đơn
                await _notificationService.NotifyOrderCancelledAsync(order.OrderId, order.UserId);

                return new PaymentProcessResult
                {
                    Success = false,
                    Message = result.ResponseMessage,
                    OrderId = orderId,
                    Code = result.ResponseCode
                };
            }
        }

        public async Task<PaymentProcessResult> ProcessPayPalSuccessAsync(int orderId, string paypalOrderId, string captureId, decimal usdAmount)
        {
            var order = await _context.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
            {
                return new PaymentProcessResult { Success = false, Message = "Đơn hàng không tồn tại", OrderId = orderId };
            }

            if (order.Status == "paid")
            {
                return new PaymentProcessResult { Success = true, Message = "Đơn hàng đã được thanh toán", OrderId = orderId };
            }

            var payment = await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == orderId);
            if (payment == null)
            {
                // Tạo mới nếu chưa có
                payment = new Payment
                {
                    OrderId = orderId,
                    Method = "paypal",
                    Status = "pending"
                };
                _context.Payments.Add(payment);
            }

            order.Status = "paid";
            order.UpdatedAt = DateTime.UtcNow;

            payment.Status = "success";
            payment.Method = "paypal";
            payment.TransactionId = captureId;
            payment.PaidAt = DateTime.UtcNow;
            payment.Amount = order.TotalAmount; // Giữ giá trị VND gốc
            payment.VnpayResponse = JsonSerializer.Serialize(new { paypal_order_id = paypalOrderId, usd_amount = usdAmount });

            await GrantAccessAsync(order);
            await _context.SaveChangesAsync();

            // Gửi thông báo thanh toán thành công
            await _notificationService.NotifyPaymentSuccessAsync(order.OrderId, order.UserId, order.TotalAmount);

            return new PaymentProcessResult { Success = true, Message = "Thanh toán PayPal thành công", OrderId = orderId };
        }

        public async Task GrantAccessAsync(Order order)
        {
            foreach (var item in order.Items)
            {
                var existing = await _context.UserAccesses
                    .FirstOrDefaultAsync(a => a.UserId == order.UserId && a.ProductId == item.ProductId);

                if (existing != null)
                {
                    existing.IsActive = true;
                    existing.RevokedAt = null;
                    existing.GrantedAt = DateTime.UtcNow;
                }
                else
                {
                    var access = new UserAccess
                    {
                        UserId = order.UserId,
                        ProductId = item.ProductId,
                        OrderId = order.OrderId,
                        IsActive = true,
                        GrantedAt = DateTime.UtcNow
                    };
                    _context.UserAccesses.Add(access);
                }

                // Cập nhật số lượng học viên
                var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == item.ProductId);
                if (product != null)
                {
                    product.TotalEnrolled = (product.TotalEnrolled) + 1;
                }

                // Xóa sản phẩm khỏi giỏ hàng nếu có
                var cart = await _context.Carts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.UserId == order.UserId);
                if (cart != null)
                {
                    var cartItem = cart.Items.FirstOrDefault(ci => ci.ProductId == item.ProductId);
                    if (cartItem != null)
                    {
                        _context.CartItems.Remove(cartItem);
                    }
                }
            }
        }

        public async Task RevokeAccessAsync(int userId, int productId)
        {
            var access = await _context.UserAccesses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == productId && a.IsActive);

            if (access == null)
            {
                throw new KeyNotFoundException("Không tìm thấy thông tin quyền truy cập");
            }

            access.IsActive = false;
            access.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        public async Task<bool> CheckUserHasAccessAsync(int userId, int productId)
        {
            return await _context.UserAccesses
                .AnyAsync(a => a.UserId == userId && a.ProductId == productId && a.IsActive);
        }
    }
}
