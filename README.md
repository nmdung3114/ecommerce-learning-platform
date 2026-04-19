# ELearnVN — Hệ thống thương mại điện tử phân phối nội dung số

Nền tảng phân phối khóa học trực tuyến và ebook với đầy đủ tính năng: xác thực JWT/OAuth, thanh toán VNPay, stream video qua Mux, và admin dashboard.

---

## 🚀 Chạy nhanh với Docker

```bash
# 1. Clone/mở project
cd ecommerce-learning-platform

# 2. Cấu hình VNPay & Mux (tùy chọn cho test đầy đủ)
# Mở backend/.env và điền API keys

# 3. Khởi động toàn bộ hệ thống
cd docker
docker-compose up --build -d

# 4. Chờ khoảng 30 giây rồi truy cập
open http://localhost
```

---

## 📁 Cấu trúc dự án

```
ecommerce-learning-platform/
├── backend/              # FastAPI backend
│   ├── app/
│   │   ├── main.py       # FastAPI app entry point
│   │   ├── config.py     # Pydantic settings
│   │   ├── database.py   # SQLAlchemy setup
│   │   ├── models/       # ORM models
│   │   ├── schemas/      # Pydantic schemas
│   │   ├── routers/      # API endpoints
│   │   ├── services/     # Business logic
│   │   └── core/         # Security, exceptions, middleware
│   ├── init_data.py      # Seed data script (chạy auto khi startup)
│   ├── requirements.txt
│   ├── Dockerfile
│   └── .env              # ⚠️ Cấu hình API keys tại đây
├── frontend/             # Vanilla JS SPA
│   ├── public/           # HTML pages
│   │   ├── index.html    # Homepage
│   │   ├── auth/         # Login, Register
│   │   ├── products/     # List, Detail
│   │   ├── cart/         # Giỏ hàng
│   │   ├── checkout/     # Thanh toán
│   │   ├── orders/       # Đơn hàng
│   │   ├── learning/     # Video player & Ebook
│   │   ├── profile/      # Hồ sơ người dùng
│   │   └── admin/        # Dashboard admin
│   ├── css/              # Design system CSS
│   └── js/               # JavaScript modules
│       ├── app.js        # Global state & utilities
│       ├── api/          # API client modules
│       ├── components/   # header.js, footer.js
│       └── pages/        # Page-specific scripts
├── database/
│   ├── init.sql          # Schema SQL
│   └── seed.sql          # (seed handled by init_data.py)
├── docker/
│   └── docker-compose.yml
└── nginx.conf            # Reverse proxy config
```

---

## 🔑 Tài khoản test mặc định

| Role   | Email                   | Mật khẩu   |
|--------|-------------------------|------------|
| Admin  | admin@elearning.vn      | admin123   |
| Author | author@elearning.vn     | author123  |
| User   | user@elearning.vn       | user123    |

**Mã giảm giá test:** `WELCOME50` · `SALE20` · `NEWUSER`

---

## 💳 VNPay Sandbox — Thẻ test

| Trường     | Giá trị             |
|------------|---------------------|
| Bank       | NCB                 |
| Số thẻ     | 9704198526191432198 |
| Tên chủ thẻ| NGUYEN VAN A        |
| Ngày hết   | 07/15               |
| OTP        | 123456              |

---

## 🔧 Cấu hình thực tế (VNPay + Mux)

Chỉnh sửa `backend/.env`:

```env
# VNPay Sandbox — đăng ký tại sandbox.vnpay.vn
VNPAY_TMN_CODE=your-tmn-code
VNPAY_HASH_SECRET=your-hash-secret
# URL công khai (vd. https://xxx.ngrok-free.app/api/payment/vnpay-ipn) — đăng ký IPN trên cổng VNPay
VNPAY_IPN_URL=

# Mux — đăng ký tại dashboard.mux.com
MUX_TOKEN_ID=your-token-id
MUX_TOKEN_SECRET=your-token-secret
MUX_SIGNING_KEY_ID=your-signing-key-id
MUX_SIGNING_PRIVATE_KEY=your-private-key
```

---

## 📡 API Endpoints chính

| Method | Endpoint                    | Mô tả               |
|--------|-----------------------------|---------------------|
| POST   | /api/auth/register          | Đăng ký             |
| POST   | /api/auth/login             | Đăng nhập           |
| POST   | /api/auth/oauth/callback    | OAuth (mock)        |
| GET    | /api/products               | Danh sách sản phẩm  |
| GET    | /api/products/{id}          | Chi tiết sản phẩm   |
| GET    | /api/cart                   | Xem giỏ hàng        |
| POST   | /api/orders                 | Tạo đơn hàng        |
| POST   | /api/payment/create/{id}   | Tạo VNPay URL       |
| GET    | /api/payment/vnpay-return   | VNPay return URL    |
| GET/POST | /api/payment/vnpay-ipn    | VNPay IPN (server)  |
| GET    | /api/learning/my-courses    | Khóa học của tôi    |
| GET    | /api/learning/course/{id}   | Nội dung video      |
| GET    | /api/admin/stats            | Thống kê admin      |
| GET    | /api/docs                   | Swagger UI          |

---

## 🛠 Phát triển local (không Docker)

```bash
# Backend
cd backend
python -m venv venv
venv\Scripts\activate      # Windows
pip install -r requirements.txt

# Chỉnh DATABASE_URL trong .env trỏ đến MySQL local
python init_data.py        # Tạo bảng + seed data
uvicorn app.main:app --reload --port 8000

# Frontend — dùng Live Server hoặc bất kỳ HTTP server nào
# Phải phục vụ từ root của frontend/ với /api proxy → localhost:8000
```

---

## GitHub & nhánh `develop`

Remote mặc định: [github.com/nmdung3114/ecommerce-learning-platform](https://github.com/nmdung3114/ecommerce-learning-platform). Quy trình nhánh và PR: xem [CONTRIBUTING.md](CONTRIBUTING.md).

---

## 📝 Lưu ý

- **Video Mux**: Upload video lên Mux Dashboard → lấy `playback_id` → cập nhật vào bảng `lessons` qua Admin API hoặc trực tiếp database
- **Signed URLs**: Mux signed URLs yêu cầu `MUX_SIGNING_KEY_ID` và `MUX_SIGNING_PRIVATE_KEY` hợp lệ
- **Production**: Thay `SECRET_KEY` trong `.env` bằng key ngẫu nhiên dài ít nhất 32 ký tự
