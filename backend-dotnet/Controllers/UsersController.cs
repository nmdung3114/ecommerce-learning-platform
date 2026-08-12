using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using ELearnVN.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Authorize]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _config;

        public UsersController(AppDbContext context, INotificationService notificationService, IConfiguration config)
        {
            _context = context;
            _notificationService = notificationService;
            _config = config;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound(new { detail = "Không tìm thấy người dùng" });

            return Ok(user);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UserUpdateDto dto)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound(new { detail = "Không tìm thấy người dùng" });

            if (dto.Name != null) user.Name = dto.Name;
            if (dto.Phone != null) user.Phone = dto.Phone;
            if (dto.AvatarUrl != null) user.AvatarUrl = dto.AvatarUrl;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(user);
        }

        [HttpPost("avatar")]
        public async Task<IActionResult> UploadAvatar(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { detail = "Vui lòng chọn một file ảnh" });
            }

            var allowedTypes = new Dictionary<string, string>
            {
                { "image/jpeg", "jpeg" },
                { "image/png", "png" },
                { "image/webp", "webp" },
                { "image/gif", "gif" }
            };

            if (!allowedTypes.ContainsKey(file.ContentType))
            {
                return BadRequest(new { detail = "Chỉ chấp nhận file ảnh (JPEG, PNG, WebP, GIF)" });
            }

            if (file.Length > 2 * 1024 * 1024)
            {
                return BadRequest(new { detail = "Ảnh không được vượt quá 2MB" });
            }

            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound(new { detail = "Không tìm thấy người dùng" });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var fileBytes = ms.ToArray();
            var base64Str = Convert.ToBase64String(fileBytes);

            var dataUrl = $"data:{file.ContentType};base64,{base64Str}";
            user.AvatarUrl = dataUrl;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(user);
        }

        [HttpPost("upload-cv")]
        public async Task<IActionResult> UploadCv(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { detail = "Vui lòng chọn file CV" });
            }

            var allowedTypes = new Dictionary<string, string>
            {
                { "application/pdf", ".pdf" },
                { "application/msword", ".doc" },
                { "application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx" }
            };

            if (!allowedTypes.ContainsKey(file.ContentType))
            {
                return BadRequest(new { detail = "Chỉ chấp nhận file định dạng PDF hoặc Word (.doc, .docx)" });
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                return BadRequest(new { detail = "Dung lượng file CV không được vượt quá 10MB" });
            }

            var uploadDir = _config["Uploads:UploadDir"] ?? "uploads";
            if (!Directory.Exists(uploadDir))
            {
                Directory.CreateDirectory(uploadDir);
            }

            var ext = allowedTypes[file.ContentType];
            var userId = GetCurrentUserId();
            var filename = $"cv_{userId}_{Guid.NewGuid().ToString("N").Substring(0, 8)}{ext}";
            var filepath = Path.Combine(uploadDir, filename);

            using (var stream = new FileStream(filepath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return Ok(new { url = $"/uploads/{filename}" });
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound(new { detail = "Không tìm thấy người dùng" });

            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                return BadRequest(new { detail = "Tài khoản OAuth không thể đổi mật khẩu" });
            }

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            {
                return BadRequest(new { detail = "Mật khẩu hiện tại không đúng" });
            }

            if (string.IsNullOrEmpty(dto.NewPassword) || dto.NewPassword.Length < 6)
            {
                return BadRequest(new { detail = "Mật khẩu mới phải ít nhất 6 ký tự" });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Đổi mật khẩu thành công" });
        }

        [HttpPost("apply-author")]
        public async Task<IActionResult> ApplyAuthor([FromBody] AuthorApplicationDto dto)
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound(new { detail = "Không tìm thấy người dùng" });

            if (user.Role == "author")
            {
                return BadRequest(new { detail = "Bạn đã là Giảng viên rồi!" });
            }

            if (user.Role == "admin")
            {
                return BadRequest(new { detail = "Tài khoản Admin không cần đăng ký làm Giảng viên" });
            }

            if (user.AuthorApplicationStatus == "pending")
            {
                return BadRequest(new { detail = "Đơn đăng ký của bạn đang chờ xét duyệt. Vui lòng đợi!" });
            }

            user.AuthorApplicationStatus = "pending";

            var appData = new
            {
                specialization = dto.Specialization,
                experience = dto.Experience,
                portfolio_url = dto.PortfolioUrl,
                course_topic = dto.CourseTopic,
                cv_url = dto.CvUrl,
                submitted_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            user.AuthorApplicationData = JsonSerializer.Serialize(appData);
            user.UpdatedAt = DateTime.UtcNow;

            await _notificationService.NotifyAuthorApplicationAsync(user.UserId, user.Name);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đơn đăng ký làm Giảng viên đã được gửi! Admin sẽ xem xét trong thời gian sớm nhất." });
        }

        [HttpGet("author-status")]
        public async Task<IActionResult> GetAuthorStatus()
        {
            var userId = GetCurrentUserId();
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user == null) return NotFound(new { detail = "Không tìm thấy người dùng" });

            return Ok(new
            {
                role = user.Role,
                author_application_status = user.AuthorApplicationStatus
            });
        }
    }

    public class UserUpdateDto
    {
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class ChangePasswordDto
    {
        public string CurrentPassword { get; set; } = null!;
        public string NewPassword { get; set; } = null!;
    }

    public class AuthorApplicationDto
    {
        public string Specialization { get; set; } = null!;
        public string Experience { get; set; } = null!;
        public string? PortfolioUrl { get; set; }
        public string CourseTopic { get; set; } = null!;
        public string CvUrl { get; set; } = null!;
    }
}
