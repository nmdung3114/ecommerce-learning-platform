using System;
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
    [Route("api/notifications")]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public NotificationsController(AppDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        [HttpGet("")]
        public async Task<IActionResult> GetNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int page_size = 20,
            [FromQuery] bool unread_only = false)
        {
            if (page < 1) page = 1;
            if (page_size < 1 || page_size > 50) page_size = 20;

            var userId = GetCurrentUserId();
            var query = _context.Notifications.Where(n => n.UserId == userId);

            if (unread_only)
            {
                query = query.Where(n => !n.IsRead);
            }

            var total = await query.CountAsync();
            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * page_size)
                .Take(page_size)
                .AsNoTracking()
                .ToListAsync();

            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            var items = notifications.Select(n => new
            {
                notification_id = n.NotificationId,
                type = n.Type,
                title = n.Title,
                message = n.Message,
                link = n.Link,
                is_read = n.IsRead,
                created_at = n.CreatedAt
            });

            return Ok(new
            {
                notifications = items,
                total = total,
                unread_count = unreadCount
            });
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = GetCurrentUserId();
            var count = await _context.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            return Ok(new { unread_count = count });
        }

        [HttpPut("{notification_id:int}/read")]
        public async Task<IActionResult> MarkRead(int notification_id)
        {
            var userId = GetCurrentUserId();
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.NotificationId == notification_id && n.UserId == userId);

            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Đã đánh dấu đã đọc" });
        }

        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = GetCurrentUserId();
            var unreadNotifications = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in unreadNotifications)
            {
                n.IsRead = true;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã đánh dấu tất cả đã đọc" });
        }
    }
}
