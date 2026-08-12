using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ELearnVN.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ELearnVN.Backend.Controllers
{
    [ApiController]
    [Route("api/ai")]
    [Authorize]
    public class AiController : ControllerBase
    {
        private readonly IGeminiService _geminiService;

        public AiController(IGeminiService geminiService)
        {
            _geminiService = geminiService;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequestDto req)
        {
            var userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Học viên";
            var reply = await _geminiService.ChatAsync(req.Message, userName, req.Context);

            // Return matching ChatResponse structure
            return Ok(new
            {
                reply = reply,
                role = "assistant",
                model = string.IsNullOrEmpty(reply) ? "rule-based" : "gemini"
            });
        }
    }

    public class ChatRequestDto
    {
        public string Message { get; set; } = null!;
        public int? ProductId { get; set; }
        public string? Context { get; set; }
    }
}
