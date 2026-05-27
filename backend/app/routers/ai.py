# AI Router — Gemini gemini-2.0-flash
from fastapi import APIRouter, Depends
from pydantic import BaseModel
from sqlalchemy.orm import Session
from app.database import get_db
from app.dependencies import get_current_user
from app.models.user import User
from app.config import settings
import logging

logger = logging.getLogger(__name__)

router = APIRouter(prefix="/api/ai", tags=["ai"])


class ChatRequest(BaseModel):
    message: str
    product_id: int = None
    context: str = None  # e.g., "video_lesson: Bài 3 - FastAPI"


class ChatResponse(BaseModel):
    reply: str
    role: str = "assistant"
    model: str = "gemini"


def _build_system_prompt(user_name: str, context: str = None) -> str:
    ctx_info = f"\nNgữ cảnh hiện tại: Học viên đang xem bài '{context}'." if context else ""
    return f"""Bạn là AI Tutor của nền tảng ELearnVN — một nền tảng học trực tuyến chuyên về lập trình và công nghệ.
Tên học viên: {user_name}.{ctx_info}

Nhiệm vụ của bạn:
- Giải thích các khái niệm kỹ thuật một cách dễ hiểu, ngắn gọn (3-5 câu là tốt nhất)
- Hỗ trợ debug code nếu học viên paste code vào
- Gợi ý tài nguyên học thêm khi phù hợp
- Luôn trả lời bằng tiếng Việt, thân thiện và khích lệ
- Nếu câu hỏi không liên quan đến học lập trình, nhẹ nhàng hướng về chủ đề học tập

Phong cách: Ngắn gọn, rõ ràng, dùng emoji khi phù hợp để tạo cảm giác thân thiện."""


def _gemini_chat(user_message: str, system_prompt: str) -> str:
    from google import genai

    client = genai.Client(api_key=settings.GEMINI_API_KEY)

    full_prompt = f"""
    {system_prompt}

    User: {user_message}
    """

    response = client.models.generate_content(
        model="gemini-2.0-flash",
        contents=full_prompt,
    )

    return response.text


def _rule_based_reply(user_message: str, user_name: str) -> str:
    """Fallback rule-based khi không có API key."""
    msg = user_message.lower()
    if any(w in msg for w in ["api", "rest", "endpoint"]):
        return "🔌 API (Application Programming Interface) là cầu nối giữa các ứng dụng. RESTful API dùng HTTP methods (GET, POST, PUT, DELETE) để giao tiếp. Bạn muốn tìm hiểu thêm về phần nào?"
    elif any(w in msg for w in ["lỗi", "error", "bug", "không chạy"]):
        return "🐛 Để debug hiệu quả: 1) Đọc kỹ thông báo lỗi, 2) Kiểm tra console (F12), 3) Thêm `console.log` hoặc `print` để trace. Bạn có thể paste lỗi cụ thể cho mình xem không?"
    elif any(w in msg for w in ["python", "fastapi"]):
        return "🐍 Python + FastAPI là combo cực mạnh! FastAPI tự động generate Swagger docs, hỗ trợ async/await, và type hints. Bạn đang gặp vấn đề gì với FastAPI?"
    elif any(w in msg for w in ["javascript", "js", "react", "vue"]):
        return "⚡ JavaScript hiện đại (ES6+) rất mạnh! Hãy học `async/await`, `destructuring`, và `arrow functions` trước. Bạn đang làm việc với framework nào?"
    elif any(w in msg for w in ["docker", "container", "deploy"]):
        return "🐳 Docker giúp bạn đóng gói ứng dụng vào container — chạy nhất quán mọi nơi. `docker-compose up` để chạy nhiều service cùng lúc. Bạn cần giúp về Docker Compose không?"
    elif any(w in msg for w in ["database", "sql", "mysql", "query"]):
        return "🗄️ Database là trái tim của ứng dụng! SQLAlchemy ORM giúp bạn làm việc với MySQL mà không cần viết SQL thô. Bạn cần giải thích về JOIN, query hay relationships?"
    elif any(w in msg for w in ["giá", "tiền", "mua", "học phí"]):
        return "💰 Về học phí và thanh toán, bạn có thể xem tại trang chi tiết khóa học. ELearnVN hỗ trợ thanh toán qua VNPay an toàn nhé!"
    else:
        return f"🤔 Câu hỏi của bạn rất thú vị! Mình đang ở chế độ offline, hãy hỏi cụ thể về một khái niệm lập trình và mình sẽ cố giải thích nhé, {user_name}! 💪"


@router.post("/chat", response_model=ChatResponse)
def ai_tutor_chat(
    req: ChatRequest,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    """
    AI Tutor Chat — tích hợp Gemini 1.5 Flash.
    Fallback sang rule-based nếu không có GEMINI_API_KEY.
    """
    system_prompt = _build_system_prompt(current_user.name, req.context)
    model_used = "gemini-2.0-flash"

    if settings.GEMINI_API_KEY:
        try:
            reply = _gemini_chat(req.message, system_prompt)
            return ChatResponse(reply=reply, model=model_used)
        except Exception as e:
            logger.warning(f"Gemini API error: {e} — falling back to rule-based")

    # Fallback
    reply = _rule_based_reply(req.message, current_user.name)
    return ChatResponse(reply=reply, model="rule-based")
