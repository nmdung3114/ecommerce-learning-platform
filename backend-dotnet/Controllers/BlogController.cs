using System;
using System.Collections.Generic;
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

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    public class BlogController : ControllerBase
    {
        private readonly AppDbContext _context;

        public BlogController(AppDbContext context)
        {
            _context = context;
        }

        private int GetCurrentUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(userIdStr, out var id) ? id : 0;
        }

        private bool IsAdmin()
        {
            return User.IsInRole("admin");
        }

        private async Task<int?> GetOptionalUserIdAsync()
        {
            var authenticateResult = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
            if (authenticateResult.Succeeded && authenticateResult.Principal != null)
            {
                var userIdStr = authenticateResult.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (int.TryParse(userIdStr, out var id))
                {
                    return id;
                }
            }
            return null;
        }

        private object BuildAuthorInfo(User u)
        {
            return new
            {
                user_id = u.UserId,
                name = u.Name,
                avatar_url = u.AvatarUrl
            };
        }

        private object BuildPostListItem(BlogPost post, int commentCount)
        {
            var preview = post.Content.Length > 200
                ? post.Content.Substring(0, 200) + "..."
                : post.Content;

            return new
            {
                post_id = post.PostId,
                title = post.Title,
                content_preview = preview,
                status = post.Status,
                created_at = post.CreatedAt,
                author = post.Author != null ? BuildAuthorInfo(post.Author) : null,
                comment_count = commentCount
            };
        }

        // --- PUBLIC / USER ENDPOINTS ---

        [HttpGet("api/blog/posts")]
        [AllowAnonymous]
        public async Task<IActionResult> ListPosts(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 10,
            [FromQuery] string? search = null)
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 50) limit = 10;

            var query = _context.BlogPosts
                .Include(p => p.Author)
                .Where(p => p.Status == "published");

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Title.Contains(search));
            }

            var total = await query.CountAsync();
            var posts = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            // Comment counts
            var postIds = posts.Select(p => p.PostId).ToList();
            var commentCounts = new Dictionary<int, int>();

            if (postIds.Any())
            {
                commentCounts = await _context.BlogComments
                    .Where(c => postIds.Contains(c.PostId) && c.Status == "visible")
                    .GroupBy(c => c.PostId)
                    .Select(g => new { PostId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.PostId, x => x.Count);
            }

            var items = posts.Select(p =>
            {
                commentCounts.TryGetValue(p.PostId, out var count);
                return BuildPostListItem(p, count);
            }).ToList();

            var totalPages = (int)Math.Ceiling((double)total / limit);
            if (totalPages < 1) totalPages = 1;

            return Ok(new
            {
                items = items,
                total = total,
                page = page,
                total_pages = totalPages
            });
        }

        [HttpPost("api/blog/posts")]
        [Authorize]
        public async Task<IActionResult> CreatePost([FromBody] BlogPostCreateDto dto)
        {
            if (string.IsNullOrEmpty(dto.Title) || string.IsNullOrEmpty(dto.Title.Trim()))
            {
                return BadRequest(new { detail = "Tiêu đề không được bỏ trống" });
            }
            if (string.IsNullOrEmpty(dto.Content) || string.IsNullOrEmpty(dto.Content.Trim()))
            {
                return BadRequest(new { detail = "Nội dung không được bỏ trống" });
            }

            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstAsync(u => u.UserId == userId);

            var post = new BlogPost
            {
                UserId = userId,
                Title = dto.Title.Trim(),
                Content = dto.Content.Trim(),
                Status = "published",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.BlogPosts.Add(post);
            await _context.SaveChangesAsync();

            return StatusCode(201, new
            {
                post_id = post.PostId,
                title = post.Title,
                content = post.Content,
                status = post.Status,
                created_at = post.CreatedAt,
                updated_at = post.UpdatedAt,
                author = BuildAuthorInfo(user),
                comment_count = 0
            });
        }

        [HttpGet("api/blog/posts/{post_id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPost(int post_id)
        {
            var post = await _context.BlogPosts
                .Include(p => p.Author)
                .FirstOrDefaultAsync(p => p.PostId == post_id);

            if (post == null)
            {
                return NotFound(new { detail = "Bài viết không tồn tại" });
            }

            if (post.Status == "hidden")
            {
                var currentUserId = await GetOptionalUserIdAsync();
                var isAdmin = User.IsInRole("admin") || (await IsUserAdminAsync(currentUserId));

                if (!currentUserId.HasValue || (post.UserId != currentUserId.Value && !isAdmin))
                {
                    return NotFound(new { detail = "Bài viết không tồn tại" });
                }
            }

            var commentCount = await _context.BlogComments
                .CountAsync(c => c.PostId == post_id && c.Status == "visible");

            return Ok(new
            {
                post_id = post.PostId,
                title = post.Title,
                content = post.Content,
                status = post.Status,
                created_at = post.CreatedAt,
                updated_at = post.UpdatedAt,
                author = post.Author != null ? BuildAuthorInfo(post.Author) : null,
                comment_count = commentCount
            });
        }

        private async Task<bool> IsUserAdminAsync(int? userId)
        {
            if (!userId.HasValue) return false;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value);
            return user?.Role == "admin";
        }

        [HttpDelete("api/blog/posts/{post_id:int}")]
        [Authorize]
        public async Task<IActionResult> DeletePost(int post_id)
        {
            var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.PostId == post_id);
            if (post == null)
            {
                return NotFound(new { detail = "Bài viết không tồn tại" });
            }

            var userId = GetCurrentUserId();
            if (post.UserId != userId && !IsAdmin())
            {
                return Forbid("Bạn không có quyền xóa bài viết này");
            }

            _context.BlogPosts.Remove(post);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // --- COMMENTS ---

        [HttpGet("api/blog/posts/{post_id:int}/comments")]
        [AllowAnonymous]
        public async Task<IActionResult> ListComments(int post_id)
        {
            var post = await _context.BlogPosts.AnyAsync(p => p.PostId == post_id);
            if (!post)
            {
                return NotFound(new { detail = "Bài viết không tồn tại" });
            }

            var comments = await _context.BlogComments
                .Include(c => c.Author)
                .Where(c => c.PostId == post_id && c.Status == "visible")
                .OrderBy(c => c.CreatedAt)
                .ToListAsync();

            var items = comments.Select(c => new
            {
                comment_id = c.CommentId,
                post_id = c.PostId,
                content = c.Content,
                status = c.Status,
                created_at = c.CreatedAt,
                author = c.Author != null ? BuildAuthorInfo(c.Author) : null
            }).ToList();

            return Ok(new { items = items, total = items.Count });
        }

        [HttpPost("api/blog/posts/{post_id:int}/comments")]
        [Authorize]
        public async Task<IActionResult> CreateComment(int post_id, [FromBody] BlogCommentCreateDto dto)
        {
            var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.PostId == post_id && p.Status == "published");
            if (post == null)
            {
                return NotFound(new { detail = "Bài viết không tồn tại" });
            }

            if (string.IsNullOrEmpty(dto.Content) || string.IsNullOrEmpty(dto.Content.Trim()))
            {
                return BadRequest(new { detail = "Nội dung bình luận không được bỏ trống" });
            }

            var userId = GetCurrentUserId();
            var user = await _context.Users.FirstAsync(u => u.UserId == userId);

            var comment = new BlogComment
            {
                PostId = post_id,
                UserId = userId,
                Content = dto.Content.Trim(),
                Status = "visible",
                CreatedAt = DateTime.UtcNow
            };
            _context.BlogComments.Add(comment);
            await _context.SaveChangesAsync();

            return StatusCode(201, new
            {
                comment_id = comment.CommentId,
                post_id = comment.PostId,
                content = comment.Content,
                status = comment.Status,
                created_at = comment.CreatedAt,
                author = BuildAuthorInfo(user)
            });
        }

        [HttpDelete("api/blog/comments/{comment_id:int}")]
        [Authorize]
        public async Task<IActionResult> DeleteComment(int comment_id)
        {
            var comment = await _context.BlogComments.FirstOrDefaultAsync(c => c.CommentId == comment_id);
            if (comment == null)
            {
                return NotFound(new { detail = "Bình luận không tồn tại" });
            }

            var userId = GetCurrentUserId();
            if (comment.UserId != userId && !IsAdmin())
            {
                return Forbid("Bạn không có quyền xóa bình luận này");
            }

            _context.BlogComments.Remove(comment);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // --- ADMIN ENDPOINTS ---

        [HttpGet("api/admin/blog/posts")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminListPosts(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? search = null)
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var query = _context.BlogPosts
                .Include(p => p.Author)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(p => p.Status == status);
            }
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Title.Contains(search));
            }

            var total = await query.CountAsync();
            var posts = await query
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            var postIds = posts.Select(p => p.PostId).ToList();
            var commentCounts = new Dictionary<int, int>();

            if (postIds.Any())
            {
                commentCounts = await _context.BlogComments
                    .Where(c => postIds.Contains(c.PostId))
                    .GroupBy(c => c.PostId)
                    .Select(g => new { PostId = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.PostId, x => x.Count);
            }

            var items = posts.Select(p =>
            {
                commentCounts.TryGetValue(p.PostId, out var count);
                return BuildPostListItem(p, count);
            }).ToList();

            var totalPages = (int)Math.Ceiling((double)total / limit);
            if (totalPages < 1) totalPages = 1;

            return Ok(new
            {
                items = items,
                total = total,
                page = page,
                total_pages = totalPages
            });
        }

        [HttpPatch("api/admin/blog/posts/{post_id:int}/status")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminUpdatePostStatus(int post_id, [FromBody] BlogPostStatusUpdateDto dto)
        {
            if (dto.Status != "published" && dto.Status != "hidden")
            {
                return BadRequest(new { detail = "Trạng thái không hợp lệ" });
            }

            var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.PostId == post_id);
            if (post == null)
            {
                return NotFound(new { detail = "Bài viết không tồn tại" });
            }

            post.Status = dto.Status;
            post.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { detail = "Cập nhật trạng thái thành công", status = post.Status });
        }

        [HttpDelete("api/admin/blog/posts/{post_id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminDeletePost(int post_id)
        {
            var post = await _context.BlogPosts.FirstOrDefaultAsync(p => p.PostId == post_id);
            if (post == null)
            {
                return NotFound(new { detail = "Bài viết không tồn tại" });
            }

            _context.BlogPosts.Remove(post);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("api/admin/blog/comments")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminListComments(
            [FromQuery] int page = 1,
            [FromQuery] int limit = 20,
            [FromQuery] string? status = null)
        {
            if (page < 1) page = 1;
            if (limit < 1 || limit > 100) limit = 20;

            var query = _context.BlogComments
                .Include(c => c.Author)
                .Include(c => c.Post)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(c => c.Status == status);
            }

            var total = await query.CountAsync();
            var comments = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * limit)
                .Take(limit)
                .ToListAsync();

            var items = comments.Select(c => new
            {
                comment_id = c.CommentId,
                post_id = c.PostId,
                post_title = c.Post?.Title ?? "—",
                content = c.Content,
                status = c.Status,
                created_at = c.CreatedAt,
                author = c.Author != null ? BuildAuthorInfo(c.Author) : null
            }).ToList();

            var totalPages = (int)Math.Ceiling((double)total / limit);
            if (totalPages < 1) totalPages = 1;

            return Ok(new
            {
                items = items,
                total = total,
                page = page,
                total_pages = totalPages
            });
        }

        [HttpPatch("api/admin/blog/comments/{comment_id:int}/status")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminUpdateCommentStatus(int comment_id, [FromBody] BlogCommentStatusUpdateDto dto)
        {
            if (dto.Status != "visible" && dto.Status != "hidden")
            {
                return BadRequest(new { detail = "Trạng thái không hợp lệ" });
            }

            var comment = await _context.BlogComments.FirstOrDefaultAsync(c => c.CommentId == comment_id);
            if (comment == null)
            {
                return NotFound(new { detail = "Bình luận không tồn tại" });
            }

            comment.Status = dto.Status;
            await _context.SaveChangesAsync();

            return Ok(new { detail = "Cập nhật thành công", status = comment.Status });
        }

        [HttpDelete("api/admin/blog/comments/{comment_id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AdminDeleteComment(int comment_id)
        {
            var comment = await _context.BlogComments.FirstOrDefaultAsync(c => c.CommentId == comment_id);
            if (comment == null)
            {
                return NotFound(new { detail = "Bình luận không tồn tại" });
            }

            _context.BlogComments.Remove(comment);
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    // DTOs
    public class BlogPostCreateDto
    {
        public string Title { get; set; } = null!;
        public string Content { get; set; } = null!;
    }

    public class BlogCommentCreateDto
    {
        public string Content { get; set; } = null!;
    }

    public class BlogPostStatusUpdateDto
    {
        public string Status { get; set; } = null!; // published | hidden
    }

    public class BlogCommentStatusUpdateDto
    {
        public string Status { get; set; } = null!; // visible | hidden
    }
}
