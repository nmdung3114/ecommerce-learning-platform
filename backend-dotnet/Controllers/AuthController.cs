using System;
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
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITokenService _tokenService;

        public AuthController(AppDbContext context, ITokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password) || string.IsNullOrEmpty(dto.Name))
            {
                return BadRequest(new { detail = "Email, mật khẩu và tên là bắt buộc" });
            }

            var existing = await _context.Users.AnyAsync(u => u.Email == dto.Email);
            if (existing)
            {
                return Conflict(new { detail = "Email đã được sử dụng" });
            }

            if (dto.Password.Length < 6)
            {
                return BadRequest(new { detail = "Mật khẩu phải ít nhất 6 ký tự" });
            }

            var user = new User
            {
                Email = dto.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Name = dto.Name,
                Phone = dto.Phone,
                Role = "learner",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var token = _tokenService.GenerateToken(user);
            return Ok(new TokenResponseDto
            {
                AccessToken = token,
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                AuthorApplicationStatus = user.AuthorApplicationStatus
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password))
            {
                return BadRequest(new { detail = "Email và mật khẩu là bắt buộc" });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                return Unauthorized(new { detail = "Email hoặc mật khẩu không đúng" });
            }

            if (user.Status != "active")
            {
                return Unauthorized(new { detail = "Tài khoản đã bị tạm khóa" });
            }

            var token = _tokenService.GenerateToken(user);
            return Ok(new TokenResponseDto
            {
                AccessToken = token,
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                AvatarUrl = user.AvatarUrl,
                AuthorApplicationStatus = user.AuthorApplicationStatus
            });
        }

        [HttpPost("oauth/callback")]
        public async Task<IActionResult> OAuthCallback([FromBody] OAuthCallbackDto dto)
        {
            if (string.IsNullOrEmpty(dto.Email))
            {
                return BadRequest(new { detail = "Email là bắt buộc" });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
            if (user != null)
            {
                user.OauthProvider = dto.Provider;
                user.OauthId = dto.OauthId;
                if (!string.IsNullOrEmpty(dto.AvatarUrl) && string.IsNullOrEmpty(user.AvatarUrl))
                {
                    user.AvatarUrl = dto.AvatarUrl;
                }
                user.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                user = new User
                {
                    Email = dto.Email,
                    Name = dto.Name ?? dto.Email.Split('@')[0],
                    OauthProvider = dto.Provider,
                    OauthId = dto.OauthId,
                    AvatarUrl = dto.AvatarUrl,
                    Role = "learner",
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
            }

            await _context.SaveChangesAsync();

            var token = _tokenService.GenerateToken(user);
            return Ok(new TokenResponseDto
            {
                AccessToken = token,
                UserId = user.UserId,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                AvatarUrl = user.AvatarUrl,
                AuthorApplicationStatus = user.AuthorApplicationStatus
            });
        }

        [HttpPost("google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleTokenDto dto)
        {
            // Trình giả lập Google Token trong sandbox hoặc tích hợp Google API thực
            // Vì frontend gửi Mock Google Token, chúng ta sẽ parse hoặc giả lập 
            // thông tin từ ID Token hoặc fallback về mock callback.
            // Để đơn giản và an toàn với sandbox, nếu token thực không được verify,
            // chúng ta sẽ parse claims (hoặc mock) dựa trên token gửi lên.
            if (string.IsNullOrEmpty(dto.IdToken))
            {
                return BadRequest(new { detail = "ID Token là bắt buộc" });
            }

            try
            {
                // Thử parse JWT để lấy email và name mà không cần gọi Google API verify (cho offline dev/sandbox)
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                if (handler.CanReadToken(dto.IdToken))
                {
                    var jwtToken = handler.ReadJwtToken(dto.IdToken);
                    var email = jwtToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
                    var name = jwtToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? email?.Split('@')[0] ?? "Google User";
                    var googleSub = jwtToken.Subject ?? jwtToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                    var avatarUrl = jwtToken.Claims.FirstOrDefault(c => c.Type == "picture")?.Value;

                    if (string.IsNullOrEmpty(email))
                    {
                        return BadRequest(new { detail = "Email Google chưa được xác minh" });
                    }

                    var user = await _context.Users.FirstOrDefaultAsync(u => u.OauthProvider == "google" && u.OauthId == googleSub)
                               ?? await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

                    if (user != null)
                    {
                        if (string.IsNullOrEmpty(user.OauthProvider)) user.OauthProvider = "google";
                        if (string.IsNullOrEmpty(user.OauthId)) user.OauthId = googleSub;
                        if (!string.IsNullOrEmpty(avatarUrl) && string.IsNullOrEmpty(user.AvatarUrl)) user.AvatarUrl = avatarUrl;
                        user.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        user = new User
                        {
                            Email = email,
                            Name = name,
                            OauthProvider = "google",
                            OauthId = googleSub,
                            AvatarUrl = avatarUrl,
                            Role = "learner",
                            Status = "active",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.Users.Add(user);
                    }

                    await _context.SaveChangesAsync();

                    var token = _tokenService.GenerateToken(user);
                    return Ok(new TokenResponseDto
                    {
                        AccessToken = token,
                        UserId = user.UserId,
                        Name = user.Name,
                        Email = user.Email,
                        Role = user.Role,
                        AvatarUrl = user.AvatarUrl,
                        AuthorApplicationStatus = user.AuthorApplicationStatus
                    });
                }
            }
            catch (Exception ex)
            {
                return Unauthorized(new { detail = $"Token Google không hợp lệ: {ex.Message}" });
            }

            return Unauthorized(new { detail = "Token Google không hợp lệ" });
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> GetMe()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            var user = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound(new { detail = "Không tìm thấy người dùng" });
            }

            return Ok(new
            {
                user_id = user.UserId,
                email = user.Email,
                name = user.Name,
                phone = user.Phone,
                role = user.Role,
                status = user.Status,
                author_application_status = user.AuthorApplicationStatus,
                avatar_url = user.AvatarUrl,
                oauth_provider = user.OauthProvider,
                oauth_id = user.OauthId,
                created_at = user.CreatedAt,
                updated_at = user.UpdatedAt
            });
        }
    }

    // DTOs
    public class RegisterDto
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Phone { get; set; }
    }

    public class LoginDto
    {
        public string Email { get; set; } = null!;
        public string Password { get; set; } = null!;
    }

    public class OAuthCallbackDto
    {
        public string Email { get; set; } = null!;
        public string? Name { get; set; }
        public string Provider { get; set; } = null!; // google | facebook
        public string OauthId { get; set; } = null!;
        public string? AvatarUrl { get; set; }
    }

    public class GoogleTokenDto
    {
        public string IdToken { get; set; } = null!;
    }

    public class TokenResponseDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [System.Text.Json.Serialization.JsonPropertyName("user_id")]
        public int UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = null!;

        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string Role { get; set; } = null!;

        [System.Text.Json.Serialization.JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("author_application_status")]
        public string? AuthorApplicationStatus { get; set; }
    }
}
