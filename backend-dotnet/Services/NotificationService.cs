using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Services
{
    public interface INotificationService
    {
        Task CreateAsync(int userId, string type, string title, string? message, string? link = null);
        Task NotifyAllAdminsAsync(string type, string title, string? message, string? link = null);
        Task NotifyPaymentSuccessAsync(int orderId, int userId, decimal amount);
        Task NotifyOrderCancelledAsync(int orderId, int userId);
        Task NotifyRefundRequestedAsync(int orderId, int userId, decimal amount);
        Task NotifyRefundCompletedAsync(int orderId, int userId, decimal amount);
        Task NotifyAuthorApplicationAsync(int applicantUserId, string applicantName);
        Task NotifyAuthorApprovedAsync(int userId);
        Task NotifyAuthorRejectedAsync(int userId);
        Task NotifyCourseSubmittedAsync(int productId, string productName, string authorName);
        Task NotifyCourseApprovedAsync(int userId, int productId, string productName);
        Task NotifyCourseRejectedAsync(int userId, int productId, string productName, string reason);
    }

    public class NotificationService : INotificationService
    {
        private readonly AppDbContext _context;

        public NotificationService(AppDbContext context)
        {
            _context = context;
        }

        public async Task CreateAsync(int userId, string type, string title, string? message, string? link = null)
        {
            var notif = new Notification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Message = message,
                Link = link,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.Notifications.Add(notif);
            await _context.SaveChangesAsync();
        }

        public async Task NotifyAllAdminsAsync(string type, string title, string? message, string? link = null)
        {
            var admins = await _context.Users
                .Where(u => u.Role == "admin" && u.Status == "active")
                .ToListAsync();

            foreach (var admin in admins)
            {
                var notif = new Notification
                {
                    UserId = admin.UserId,
                    Type = type,
                    Title = title,
                    Message = message,
                    Link = link,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Notifications.Add(notif);
            }
            await _context.SaveChangesAsync();
        }

        public async Task NotifyPaymentSuccessAsync(int orderId, int userId, decimal amount)
        {
            var orderLink = $"/orders/index.html?order_id={orderId}&status=success";
            var adminLink = $"/admin/orders.html";

            await CreateAsync(
                userId, "success",
                "💳 Thanh toán thành công!",
                $"Đơn hàng #{orderId} đã được thanh toán thành công ({amount:N0}đ). Nội dung đã được mở khóa!",
                orderLink
            );

            await NotifyAllAdminsAsync(
                "info",
                $"💳 Đơn hàng #{orderId} đã thanh toán",
                $"Người dùng #{userId} vừa thanh toán thành công đơn #{orderId} ({amount:N0}đ).",
                adminLink
            );
        }

        public async Task NotifyOrderCancelledAsync(int orderId, int userId)
        {
            var orderLink = $"/orders/index.html";
            var adminLink = $"/admin/orders.html";

            await CreateAsync(
                userId, "warning",
                "🗑 Đơn hàng đã bị hủy",
                $"Đơn hàng #{orderId} của bạn đã được hủy thành công.",
                orderLink
            );

            await NotifyAllAdminsAsync(
                "warning",
                $"🗑 Đơn hàng #{orderId} bị hủy",
                $"Người dùng #{userId} vừa hủy đơn hàng #{orderId}.",
                adminLink
            );
        }

        public async Task NotifyRefundRequestedAsync(int orderId, int userId, decimal amount)
        {
            var orderLink = $"/orders/index.html";
            var adminLink = $"/admin/orders.html";

            await CreateAsync(
                userId, "info",
                "↩ Yêu cầu hoàn tiền đã được gửi",
                $"Yêu cầu hoàn tiền cho đơn hàng #{orderId} ({amount:N0}đ) đã được ghi nhận. Quyền truy cập đã bị thu hồi.",
                orderLink
            );

            await NotifyAllAdminsAsync(
                "warning",
                $"⚠️ Yêu cầu hoàn tiền đơn #{orderId}",
                $"Người dùng #{userId} vừa yêu cầu hoàn tiền đơn hàng #{orderId} ({amount:N0}đ). Vui lòng xử lý!",
                adminLink
            );
        }

        public async Task NotifyRefundCompletedAsync(int orderId, int userId, decimal amount)
        {
            var orderLink = $"/orders/index.html";
            var adminLink = $"/admin/orders.html";

            await CreateAsync(
                userId, "success",
                "✅ Hoàn tiền thành công",
                $"Đơn hàng #{orderId} ({amount:N0}đ) đã được hoàn tiền. Quyền truy cập đã bị thu hồi.",
                orderLink
            );

            await NotifyAllAdminsAsync(
                "info",
                $"✅ Hoàn tiền đơn #{orderId} hoàn tất",
                $"Đã hoàn tiền thành công cho người dùng #{userId}, đơn hàng #{orderId} ({amount:N0}đ).",
                adminLink
            );
        }

        public async Task NotifyAuthorApplicationAsync(int applicantUserId, string applicantName)
        {
            await NotifyAllAdminsAsync(
                "info",
                $"📝 Đơn xin giảng viên từ {applicantName}",
                $"Người dùng {applicantName} (#{applicantUserId}) vừa gửi đơn xin trở thành Giảng viên. Vui lòng kiểm duyệt!",
                "/admin/course-approvals.html"
            );
        }

        public async Task NotifyAuthorApprovedAsync(int userId)
        {
            await CreateAsync(
                userId, "success",
                "🎉 Chúc mừng! Bạn đã trở thành Giảng viên!",
                "Tài khoản giảng viên của bạn đã được phê duyệt. Hãy vào Instructor Dashboard để tạo khóa học đầu tiên của bạn!",
                "/instructor/courses.html"
            );
        }

        public async Task NotifyAuthorRejectedAsync(int userId)
        {
            await CreateAsync(
                userId, "warning",
                "❌ Đơn giảng viên bị từ chối",
                "Đơn đăng ký làm giảng viên của bạn chưa được chấp thuận. Bạn có thể gửi lại sau khi cập nhật thông tin.",
                "/profile"
            );
        }

        public async Task NotifyCourseSubmittedAsync(int productId, string productName, string authorName)
        {
            await NotifyAllAdminsAsync(
                "info",
                $"📚 Khóa học chờ duyệt: {productName}",
                $"Tác giả {authorName} vừa gửi khóa học “{productName}” (#{productId}) chờ kiểm duyệt.",
                "/admin/course-approvals.html"
            );
        }

        public async Task NotifyCourseApprovedAsync(int userId, int productId, string productName)
        {
            await CreateAsync(
                userId, "success",
                $"✅ Khóa học đã được duyệt!",
                $"Khóa học “{productName}” (#{productId}) đã được phê duyệt và có thể bán trên hệ thống!",
                $"/instructor/courses.html"
            );
        }

        public async Task NotifyCourseRejectedAsync(int userId, int productId, string productName, string reason)
        {
            await CreateAsync(
                userId, "error",
                $"❌ Khóa học bị từ chối",
                $"Khóa học “{productName}” (#{productId}) bị từ chối. Lý do: {reason}",
                $"/instructor/courses.html"
            );
        }
    }
}
