using System;
using System.Collections.Generic;
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
    [Route("api/payment")]
    public class PaymentController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IVnPayService _vnPayService;
        private readonly IPayPalService _payPalService;
        private readonly IPaymentService _paymentService;

        public PaymentController(
            AppDbContext context,
            IVnPayService vnPayService,
            IPayPalService payPalService,
            IPaymentService paymentService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _payPalService = payPalService;
            _paymentService = paymentService;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        [HttpPost("create/{order_id:int}")]
        [Authorize]
        public async Task<IActionResult> CreatePayment(int order_id, [FromBody] CreatePaymentDto dto)
        {
            var userId = GetCurrentUserId();
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == order_id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { detail = "Đơn hàng không tồn tại" });
            }

            if (order.Status == "paid")
            {
                return BadRequest(new { detail = "Đơn hàng đã được thanh toán" });
            }

            // Allow retry if order was cancelled
            if (order.Status == "cancelled")
            {
                order.Status = "pending";
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var user = await _context.Users.AsNoTracking().FirstAsync(u => u.UserId == userId);
            var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var orderDesc = $"Thanh toan don hang #{order_id} - {user.Name}";

            var (paymentUrl, txnRef) = _vnPayService.CreatePaymentUrl(
                orderId: order_id,
                amount: order.TotalAmount,
                orderDesc: orderDesc,
                clientIp: clientIp,
                bankCode: dto.BankCode ?? ""
            );

            // Update payment with txn_ref
            var payment = await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == order_id);
            if (payment != null)
            {
                payment.VnpayTxnRef = txnRef;
                payment.Method = "vnpay";
                payment.Status = "pending";
                await _context.SaveChangesAsync();
            }

            // Clear cart
            var cart = await _context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.UserId == userId);
            if (cart != null && cart.Items.Any())
            {
                _context.CartItems.RemoveRange(cart.Items);
                await _context.SaveChangesAsync();
            }

            return Ok(new { payment_url = paymentUrl, order_id = order_id });
        }

        [HttpGet("vnpay-return")]
        public async Task<IActionResult> VnPayReturn()
        {
            var paramsMap = new Dictionary<string, string>();
            foreach (var key in Request.Query.Keys)
            {
                paramsMap[key] = Request.Query[key].ToString();
            }

            var result = await _paymentService.ProcessVnPayReturnAsync(paramsMap);

            if (result.Success)
            {
                return Redirect($"/orders/index.html?order_id={result.OrderId}&status=success");
            }
            else
            {
                var code = result.Code ?? "99";
                return Redirect($"/checkout/index.html?order_id={result.OrderId}&status=failed&code={code}");
            }
        }

        [HttpGet("vnpay-ipn")]
        [HttpPost("vnpay-ipn")]
        public async Task<IActionResult> VnPayIpn()
        {
            var merged = new Dictionary<string, string>();
            foreach (var key in Request.Query.Keys)
            {
                merged[key] = Request.Query[key].ToString();
            }

            if (Request.Method == "POST")
            {
                if (Request.HasFormContentType)
                {
                    var form = await Request.ReadFormAsync();
                    foreach (var key in form.Keys)
                    {
                        merged[key] = form[key].ToString();
                    }
                }
            }

            if (!merged.ContainsKey("vnp_SecureHash"))
            {
                return Ok(new { RspCode = "97", Message = "Missing checksum" });
            }

            var result = await _paymentService.ProcessVnPayReturnAsync(merged);

            if (!result.Success)
            {
                var msg = result.Message ?? "";
                if (msg.Contains("Chữ ký không hợp lệ"))
                {
                    return Ok(new { RspCode = "97", Message = "Invalid checksum" });
                }
                if (msg.Contains("Đơn hàng không tồn tại") || msg.Contains("Không tìm thấy thông tin thanh toán"))
                {
                    return Ok(new { RspCode = "01", Message = msg });
                }
                // Other failure scenarios already updated in DB: confirm success
                return Ok(new { RspCode = "00", Message = "Confirm Success" });
            }

            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }

        [HttpGet("status/{order_id:int}")]
        [Authorize]
        public async Task<IActionResult> GetPaymentStatus(int order_id)
        {
            var userId = GetCurrentUserId();
            var payment = await _context.Payments
                .Include(p => p.Order)
                .FirstOrDefaultAsync(p => p.OrderId == order_id && p.Order!.UserId == userId);

            if (payment == null)
            {
                return NotFound(new { detail = "Thông tin thanh toán không tìm thấy" });
            }

            return Ok(new
            {
                order_id = order_id,
                status = payment.Status,
                method = payment.Method,
                transaction_id = payment.TransactionId,
                paid_at = payment.PaidAt,
                amount = payment.Amount
            });
        }

        // --- PayPal Sandbox Endpoints ---

        [HttpPost("paypal/create/{order_id:int}")]
        [Authorize]
        public async Task<IActionResult> CreatePayPalPayment(int order_id)
        {
            var userId = GetCurrentUserId();
            var order = await _context.Orders
                .FirstOrDefaultAsync(o => o.OrderId == order_id && o.UserId == userId);

            if (order == null)
            {
                return NotFound(new { detail = "Đơn hàng không tồn tại" });
            }

            if (order.Status == "paid")
            {
                return BadRequest(new { detail = "Đơn hàng đã được thanh toán" });
            }

            if (order.Status == "cancelled")
            {
                order.Status = "pending";
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            try
            {
                var result = await _payPalService.CreatePayPalOrderAsync(order_id, order.TotalAmount);

                // Update payment details
                var payment = await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == order_id);
                if (payment != null)
                {
                    payment.Method = "paypal";
                    payment.VnpayTxnRef = result.PaypalOrderId; // Reusing this field to store paypal order ID
                    await _context.SaveChangesAsync();
                }

                // Clear cart
                var cart = await _context.Carts
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.UserId == userId);
                if (cart != null && cart.Items.Any())
                {
                    _context.CartItems.RemoveRange(cart.Items);
                    await _context.SaveChangesAsync();
                }

                return Ok(new
                {
                    approve_url = result.ApproveUrl,
                    paypal_order_id = result.PaypalOrderId,
                    usd_amount = result.UsdAmount,
                    order_id = order_id
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { detail = $"Không thể tạo PayPal order: {ex.Message}" });
            }
        }

        [HttpGet("paypal-return")]
        public async Task<IActionResult> PayPalReturn([FromQuery] int order_id, [FromQuery] string token, [FromQuery] string? PayerID = null)
        {
            // token parameter is the PayPal Order ID
            var order = await _context.Orders.FirstOrDefaultAsync(o => o.OrderId == order_id);
            if (order == null)
            {
                return Redirect($"/checkout/index.html?order_id={order_id}&status=failed&code=not_found");
            }

            if (order.Status == "paid")
            {
                return Redirect($"/orders/index.html?order_id={order_id}&status=success");
            }

            try
            {
                var result = await _payPalService.CapturePayPalOrderAsync(token);

                if (result.Success)
                {
                    var processResult = await _paymentService.ProcessPayPalSuccessAsync(order_id, token, result.CaptureId, result.UsdAmount);
                    if (processResult.Success)
                    {
                        return Redirect($"/orders/index.html?order_id={order_id}&status=success");
                    }
                }

                // Mark failed
                order.Status = "cancelled";
                order.UpdatedAt = DateTime.UtcNow;
                var payment = await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == order_id);
                if (payment != null)
                {
                    payment.Status = "failed";
                    payment.Method = "paypal";
                }
                await _context.SaveChangesAsync();

                return Redirect($"/checkout/index.html?order_id={order_id}&status=failed&code=paypal_failed");
            }
            catch (Exception)
            {
                order.Status = "cancelled";
                order.UpdatedAt = DateTime.UtcNow;
                var payment = await _context.Payments.FirstOrDefaultAsync(p => p.OrderId == order_id);
                if (payment != null)
                {
                    payment.Status = "failed";
                    payment.Method = "paypal";
                }
                await _context.SaveChangesAsync();

                return Redirect($"/checkout/index.html?order_id={order_id}&status=failed&code=capture_failed");
            }
        }
    }

    public class CreatePaymentDto
    {
        public string? BankCode { get; set; }
    }
}
