using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ELearnVN.Backend.Data;
using ELearnVN.Backend.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/certificates")]
    public class CertificatesController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public CertificatesController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        private int ValidateTokenAndGetUserId(string token)
        {
            try
            {
                var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var secretKey = _config["Jwt:Secret"] ?? "jwt-secret-change-me-thirty-two-characters-long";
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(secretKey);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
                var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdStr, out var id))
                {
                    return id;
                }
            }
            catch
            {
                // Token is invalid
            }
            return 0;
        }

        private async Task<(bool Eligible, int Completed, int Total)> CheckCompletionAsync(int userId, int productId)
        {
            var product = await _context.Products
                .Include(p => p.Course)
                .FirstOrDefaultAsync(p => p.ProductId == productId && p.ProductType == "course");

            if (product == null || product.Course == null)
            {
                return (false, 0, 0);
            }

            var total = product.Course.TotalLessons;
            if (total == 0)
            {
                return (false, 0, 0);
            }

            var completed = await _context.LearningProgresses
                .Join(_context.Lessons, p => p.LessonId, l => l.LessonId, (p, l) => new { p, l })
                .Join(_context.Modules, pl => pl.l.ModuleId, m => m.ModuleId, (pl, m) => new { pl.p, m })
                .Where(plm => plm.m.CourseId == productId && plm.p.UserId == userId && plm.p.Completed)
                .CountAsync();

            return (completed >= total, completed, total);
        }

        [HttpGet("check/{product_id:int}")]
        [Authorize]
        public async Task<IActionResult> CheckCertificateEligibility(int product_id)
        {
            var userId = GetCurrentUserId();

            // Check access
            var access = await _context.UserAccesses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == product_id && a.IsActive);

            if (access == null)
            {
                return Forbid("Bạn chưa mua khóa học này");
            }

            var (eligible, completed, total) = await CheckCompletionAsync(userId, product_id);
            var percentage = total > 0 ? (int)Math.Round((double)completed / total * 100) : 0;

            return Ok(new
            {
                eligible = eligible,
                completed_lessons = completed,
                total_lessons = total,
                percentage = percentage
            });
        }

        [HttpGet("{product_id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetCertificate(int product_id, [FromQuery] string? token = null)
        {
            int userId = 0;

            // Try header auth first
            var authenticateResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (authenticateResult.Succeeded && authenticateResult.Principal != null)
            {
                var userIdStr = authenticateResult.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                int.TryParse(userIdStr, out userId);
            }

            // Fallback to query token
            if (userId == 0 && !string.IsNullOrEmpty(token))
            {
                userId = ValidateTokenAndGetUserId(token);
            }

            if (userId == 0)
            {
                return Unauthorized(new { detail = "Chưa xác thực tài khoản" });
            }

            // Check access
            var access = await _context.UserAccesses
                .FirstOrDefaultAsync(a => a.UserId == userId && a.ProductId == product_id && a.IsActive);

            if (access == null)
            {
                return Forbid("Bạn chưa mua khóa học này");
            }

            // Check completion
            var (eligible, completed, total) = await CheckCompletionAsync(userId, product_id);
            if (!eligible)
            {
                return BadRequest(new { detail = $"Bạn mới hoàn thành {completed}/{total} bài học. Cần hoàn thành 100% để nhận chứng chỉ." });
            }

            // Get user and course details
            var user = await _context.Users.FirstAsync(u => u.UserId == userId);
            var product = await _context.Products.FirstAsync(p => p.ProductId == product_id);

            var dateStr = DateTime.UtcNow.ToString("dd/MM/yyyy");
            var svgContent = GenerateCertificateSvg(user.Name, product.Name, dateStr);
            var svgBytes = System.Text.Encoding.UTF8.GetBytes(svgContent);

            var safeName = new string(product.Name.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_').ToArray()).Trim();
            var filename = $"certificate_{safeName}_{userId}.svg";

            return File(svgBytes, "image/svg+xml", filename);
        }

        private string GenerateCertificateSvg(string userName, string courseName, string dateStr)
        {
            return $@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""0 0 1200 800"" width=""1200"" height=""800"">
  <defs>
    <style>
      @import url('https://fonts.googleapis.com/css2?family=Montserrat:wght@400;700&amp;family=Playfair+Display:ital,wght@0,700;1,400&amp;display=swap');
      .title {{ font-family: 'Montserrat', sans-serif; font-weight: 700; fill: #FFFFFF; font-size: 48px; letter-spacing: 4px; }}
      .brand {{ font-family: 'Montserrat', sans-serif; font-weight: 700; fill: #D4AF37; font-size: 24px; letter-spacing: 3px; }}
      .subtitle {{ font-family: 'Playfair Display', serif; font-style: italic; fill: #B4B4C8; font-size: 24px; }}
      .name {{ font-family: 'Montserrat', sans-serif; font-weight: 700; fill: #D4AF37; font-size: 46px; letter-spacing: 2px; }}
      .course {{ font-family: 'Montserrat', sans-serif; font-weight: 700; fill: #FFFFFF; font-size: 32px; }}
      .meta {{ font-family: 'Montserrat', sans-serif; fill: #B4B4C8; font-size: 16px; letter-spacing: 1px; }}
      .gold-text {{ fill: #D4AF37; font-weight: 700; }}
    </style>
    <linearGradient id=""bg-grad"" x1=""0%"" y1=""0%"" x2=""0%"" y2=""100%"">
      <stop offset=""0%"" style=""stop-color:#0A0A23;stop-opacity:1"" />
      <stop offset=""100%"" style=""stop-color:#19143C;stop-opacity:1"" />
    </linearGradient>
  </defs>

  <!-- Background -->
  <rect width=""1200"" height=""800"" fill=""url(#bg-grad)"" />

  <!-- Borders -->
  <rect x=""20"" y=""20"" width=""1160"" height=""760"" fill=""none"" stroke=""#D4AF37"" stroke-width=""3"" />
  <rect x=""30"" y=""30"" width=""1140"" height=""740"" fill=""none"" stroke=""#D4AF37"" stroke-width=""1"" stroke-opacity=""0.3"" />

  <!-- Corners -->
  <circle cx=""50"" cy=""50"" r=""8"" fill=""#D4AF37"" />
  <circle cx=""1150"" cy=""50"" r=""8"" fill=""#D4AF37"" />
  <circle cx=""50"" cy=""750"" r=""8"" fill=""#D4AF37"" />
  <circle cx=""1150"" cy=""750"" r=""8"" fill=""#D4AF37"" />

  <!-- Logo Area -->
  <circle cx=""600"" cy=""115"" r=""45"" fill=""#6366F1"" />
  <text x=""600"" y=""127"" font-size=""40"" text-anchor=""middle"">🎓</text>

  <!-- Brand -->
  <text x=""600"" y=""195"" class=""brand"" text-anchor=""middle"">ELEARNVN</text>

  <!-- Title -->
  <text x=""600"" y=""255"" class=""title"" text-anchor=""middle"">CHỨNG CHỈ HOÀN THÀNH</text>
  <line x1=""200"" y1=""285"" x2=""1000"" y2=""285"" stroke=""#D4AF37"" stroke-width=""2"" />

  <!-- Trao cho -->
  <text x=""600"" y=""340"" class=""subtitle"" text-anchor=""middle"">Trao cho</text>

  <!-- User Name -->
  <text x=""600"" y=""410"" class=""name"" text-anchor=""middle"">{userName}</text>
  <line x1=""300"" y1=""435"" x2=""900"" y2=""435"" stroke=""#D4AF37"" stroke-width=""1"" />

  <!-- Description -->
  <text x=""600"" y=""485"" class=""subtitle"" text-anchor=""middle"">đã hoàn thành xuất sắc khóa học</text>

  <!-- Course Name -->
  <text x=""600"" y=""550"" class=""course"" text-anchor=""middle"">“{courseName}”</text>

  <!-- Bottom Section -->
  <line x1=""200"" y1=""670"" x2=""1000"" y2=""670"" stroke=""#D4AF37"" stroke-width=""1"" />

  <text x=""300"" y=""710"" class=""meta"" text-anchor=""middle"">Ngày cấp: {dateStr}</text>
  <text x=""600"" y=""710"" class=""meta"" text-anchor=""middle"">ELearnVN Platform</text>
  <text x=""900"" y=""710"" class=""meta gold-text"" text-anchor=""middle"">Chứng nhận hoàn thành 100%</text>
</svg>";
        }
    }
}
