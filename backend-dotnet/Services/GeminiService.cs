using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ELearnVN.Backend.Services
{
    public interface IGeminiService
    {
        Task<string> ChatAsync(string message, string userName, string? context);
    }

    public class GeminiService : IGeminiService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeminiService> _logger;

        public GeminiService(IConfiguration config, HttpClient httpClient, ILogger<GeminiService> logger)
        {
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<string> ChatAsync(string message, string userName, string? context)
        {
            var apiKey = _config["Gemini:ApiKey"];
            var systemPrompt = BuildSystemPrompt(userName, context);

            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    return await CallGeminiApiAsync(message, systemPrompt, apiKey);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Gemini API error: {Error} — falling back to rule-based chatbot", ex.Message);
                }
            }

            return RuleBasedReply(message, userName);
        }

        private string BuildSystemPrompt(string userName, string? context)
        {
            var ctxInfo = !string.IsNullOrEmpty(context) ? $"\nNgữ cảnh hiện tại: Học viên đang xem bài '{context}'." : "";
            return $"Bạn là AI Tutor của nền tảng ELearnVN — một nền tảng học trực tuyến chuyên về lập trình và công nghệ.\n" +
                   $"Tên học viên: {userName}.{ctxInfo}\n\n" +
                   $"Nhiệm vụ của bạn:\n" +
                   $"- Giải thích các khái niệm kỹ thuật một cách dễ hiểu, ngắn gọn (3-5 câu là tốt nhất)\n" +
                   $"- Hỗ trợ debug code nếu học viên paste code vào\n" +
                   $"- Gợi ý tài nguyên học thêm khi phù hợp\n" +
                   $"- Luôn trả lời bằng tiếng Việt, thân thiện và khích lệ\n" +
                   $"- Nếu câu hỏi không liên quan đến học lập trình, nhẹ nhàng hướng về chủ đề học tập\n\n" +
                   $"Phong cách: Ngắn gọn, rõ ràng, dùng emoji khi phù hợp để tạo cảm giác thân thiện.";
        }

        private async Task<string> CallGeminiApiAsync(string message, string systemPrompt, string apiKey)
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.0-flash:generateContent?key={apiKey}";
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = $"{systemPrompt}\n\nUser: {message}" }
                        }
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var root = json?.RootElement ?? throw new InvalidOperationException("Gemini response is empty");

            // Extract text from candidates[0].content.parts[0].text
            try
            {
                var candidates = root.GetProperty("candidates");
                if (candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
                {
                    var content = candidates[0].GetProperty("content");
                    var parts = content.GetProperty("parts");
                    if (parts.ValueKind == JsonValueKind.Array && parts.GetArrayLength() > 0)
                    {
                        return parts[0].GetProperty("text").GetString() ?? "";
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Không thể parse cấu trúc response của Gemini: {ex.Message}");
            }

            return "Xin lỗi, tôi không nhận diện được câu trả lời từ AI. Vui lòng thử lại.";
        }

        private string RuleBasedReply(string userMessage, string userName)
        {
            var msg = userMessage.ToLowerInvariant();

            if (msg.Contains("api") || msg.Contains("rest") || msg.Contains("endpoint"))
            {
                return "🔌 API (Application Programming Interface) là cầu nối giữa các ứng dụng. RESTful API dùng HTTP methods (GET, POST, PUT, DELETE) để giao tiếp. Bạn muốn tìm hiểu thêm về phần nào?";
            }
            if (msg.Contains("lỗi") || msg.Contains("error") || msg.Contains("bug") || msg.Contains("không chạy"))
            {
                return "🐛 Để debug hiệu quả: 1) Đọc kỹ thông báo lỗi, 2) Kiểm tra console (F12), 3) Thêm `console.log` hoặc `print` để trace. Bạn có thể paste lỗi cụ thể cho mình xem không?";
            }
            if (msg.Contains("python") || msg.Contains("fastapi"))
            {
                return "🐍 Python + FastAPI là combo cực mạnh! FastAPI tự động generate Swagger docs, hỗ trợ async/await, và type hints. Bạn đang gặp vấn đề gì với FastAPI?";
            }
            if (msg.Contains("javascript") || msg.Contains("js") || msg.Contains("react") || msg.Contains("vue"))
            {
                return "⚡ JavaScript hiện đại (ES6+) rất mạnh! Hãy học `async/await`, `destructuring`, và `arrow functions` trước. Bạn đang làm việc với framework nào?";
            }
            if (msg.Contains("docker") || msg.Contains("container") || msg.Contains("deploy"))
            {
                return "🐳 Docker giúp bạn đóng gói ứng dụng vào container — chạy nhất quán mọi nơi. `docker-compose up` để chạy nhiều service cùng lúc. Bạn cần giúp về Docker Compose không?";
            }
            if (msg.Contains("database") || msg.Contains("sql") || msg.Contains("mysql") || msg.Contains("query"))
            {
                return "🗄️ Database là trái tim của ứng dụng! SQLAlchemy ORM giúp bạn làm việc với MySQL mà không cần viết SQL thô. Bạn cần giải thích về JOIN, query hay relationships?";
            }
            if (msg.Contains("giá") || msg.Contains("tiền") || msg.Contains("mua") || msg.Contains("học phí"))
            {
                return "💰 Về học phí và thanh toán, bạn có thể xem tại trang chi tiết khóa học. ELearnVN hỗ trợ thanh toán qua VNPay an toàn nhé!";
            }

            return $"🤔 Câu hỏi của bạn rất thú vị! Mình đang ở chế độ offline, hãy hỏi cụ thể về một khái niệm lập trình và mình sẽ cố giải thích nhé, {userName}! 💪";
        }
    }
}
