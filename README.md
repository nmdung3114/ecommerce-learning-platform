# ELearnVN — Nền tảng phân phối khóa học trực tuyến & Ebook (.NET 8)

Hệ thống thương mại điện tử phân phối nội dung số (khoá học trực tuyến, ebook) đầy đủ tính năng. Hệ thống đã được chuyển đổi hoàn toàn từ Python (FastAPI) sang **.NET 8 (ASP.NET Core Web API)** chất lượng cao, tích hợp Entity Framework Core, JWT, thanh toán VNPay, PayPal Sandbox, Mux Video Stream, AI Chatbot (Gemini) và trang quản trị Admin.

---

## 🚀 Chạy nhanh với Docker

```bash
# 1. Di chuyển vào thư mục dự án
cd ecommerce-learning-platform

# 2. Khởi động toàn bộ hệ thống (MySQL, .NET 8 Backend, Nginx Frontend)
cd docker
docker compose up --build -d

# 3. Chờ khoảng 30 giây để Database khởi tạo và seed dữ liệu, sau đó truy cập:
# Frontend: http://localhost
# Swagger API Docs: http://localhost/swagger (hoặc http://localhost:8000/swagger)

# 4. Khi muốn dừng hệ thống:
docker compose down
```

---

## 📁 Cấu trúc dự án

```
ecommerce-learning-platform/
├── backend-dotnet/           # ASP.NET Core 8 Web API
│   ├── Controllers/          # API Controllers (Auth, Products, Cart, Orders, Admin, etc.)
│   ├── Data/                 # AppDbContext & DbInitializer (tự động seed dữ liệu mẫu)
│   ├── Models/               # Entity Models ánh xạ trực tiếp MySQL
│   ├── Services/             # Logic nghiệp vụ (VNPay, PayPal, Mux, Gemini, Notification, etc.)
│   ├── Uploads/              # Lưu trữ static files (avatar, ebook, thumbnail...)
│   ├── appsettings.json      # Cấu hình Database, JWT, VNPay, PayPal, Mux, Gemini
│   ├── ELearnVN.Backend.csproj
│   ├── Program.cs            # Đăng ký DI Services, Middleware, CORS, Authentication
│   └── Dockerfile            # Multi-stage Docker build cho .NET 8
├── frontend/                 # Vanilla JS Single Page Application (SPA)
│   ├── public/               # Giao diện HTML
│   │   ├── index.html        # Trang chủ
│   │   ├── auth/             # Đăng ký, đăng nhập
│   │   ├── products/         # Danh sách, chi tiết khoá học & ebook
│   │   ├── cart/             # Giỏ hàng
│   │   ├── checkout/         # Thanh toán đơn hàng
│   │   ├── orders/           # Danh sách đơn hàng & hoàn tiền
│   │   ├── learning/         # Trình phát video & Đọc Ebook bảo mật
│   │   ├── profile/          # Thông tin cá nhân & ứng tuyển giảng viên
│   │   └── admin/            # Dashboard quản trị & duyệt khoá học
│   ├── css/                  # Vanilla CSS Design System
│   └── js/                   # Modules JS xử lý logic client
├── database/
│   └── init.sql              # Schema cơ sở dữ liệu MySQL ban đầu
├── docker/
│   └── docker-compose.yml    # Orchestration file chạy cả hệ thống
└── nginx.conf                # Nginx proxy định tuyến frontend và backend (/api)
```

---

## 🔑 Tài khoản thử nghiệm mặc định

Sau khi khởi động, cơ sở dữ liệu sẽ tự động được tạo và seed sẵn 3 tài khoản demo sau:

| Vai trò | Email | Mật khẩu |
| :--- | :--- | :--- |
| **Admin** | `admin@elearning.vn` | `admin123` |
| **Author (Giảng viên)** | `author@elearning.vn` | `author123` |
| **Learner (Học viên)** | `user@elearning.vn` | `user123` |

* **Mã giảm giá thử nghiệm:** `WELCOME50` (Giảm 50K cho đơn từ 200K) · `SALE20` (Giảm 20% cho đơn từ 500K) · `NEWUSER` (Giảm 100K cho đơn từ 300K).

---

## 💳 VNPay Sandbox — Thông tin thẻ test

Khi chọn thanh toán qua cổng VNPay, bạn sử dụng thông tin thẻ bên dưới của ngân hàng NCB Sandbox:

| Trường | Giá trị |
| :--- | :--- |
| **Ngân hàng** | NCB |
| **Số thẻ** | `9704198526191432198` |
| **Tên chủ thẻ** | NGUYEN VAN A |
| **Ngày phát hành** | `07/15` |
| **Mật khẩu OTP** | `123456` |

---

## ⚙️ Cấu hình API thực tế (VNPay + PayPal + Mux + Gemini)

Nếu chạy bằng Docker, bạn cấu hình trực tiếp qua các biến môi trường trong [docker-compose.yml](file:///d:/ecommerce-learning-platform/docker/docker-compose.yml). Nếu chạy local (không Docker), bạn cấu hình các khoá bí mật trong [appsettings.json](file:///d:/ecommerce-learning-platform/backend-dotnet/appsettings.json):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "server=localhost;port=3306;database=elearning;user=root;password=root123"
  },
  "Jwt": {
    "Secret": "jwt-secret-change-me-thirty-two-characters-long",
    "ExpiryMinutes": 1440
  },
  "VnPay": {
    "TmnCode": "YOUR_VNPAY_TMN_CODE",
    "HashSecret": "YOUR_VNPAY_HASH_SECRET",
    "Url": "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html",
    "ReturnUrl": "http://localhost/api/payment/vnpay-return"
  },
  "PayPal": {
    "ClientId": "YOUR_PAYPAL_CLIENT_ID",
    "ClientSecret": "YOUR_PAYPAL_CLIENT_SECRET",
    "BaseUrl": "https://api-m.sandbox.paypal.com",
    "ReturnUrl": "http://localhost/api/payment/paypal-return"
  },
  "Mux": {
    "TokenId": "YOUR_MUX_TOKEN_ID",
    "TokenSecret": "YOUR_MUX_TOKEN_SECRET",
    "SigningKeyId": "YOUR_MUX_SIGNING_KEY_ID",
    "SigningPrivateKey": "YOUR_MUX_SIGNING_PRIVATE_KEY_BASE64"
  },
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY"
  }
}
```

---

## 🌐 API Endpoints chính

| Phương thức | Endpoint | Mô tả |
| :--- | :--- | :--- |
| **POST** | `/api/auth/register` | Đăng ký tài khoản mới |
| **POST** | `/api/auth/login` | Đăng nhập hệ thống (trả về JWT Token) |
| **POST** | `/api/auth/google` | Đăng nhập thông qua Google OAuth ID Token |
| **GET** | `/api/products` | Lấy danh sách sản phẩm (lọc theo loại, danh mục, giá, level) |
| **GET** | `/api/products/{id}` | Chi tiết khoá học/ebook kèm danh sách bài học và đánh giá |
| **POST** | `/api/products/{id}/reviews` | Học viên viết đánh giá sản phẩm sau khi mua thành công |
| **GET** | `/api/cart` | Lấy các sản phẩm trong giỏ hàng hiện tại |
| **POST** | `/api/orders` | Đặt hàng và áp dụng coupon giảm giá |
| **POST** | `/api/payment/create` | Tạo phiên thanh toán (trả về URL VNPay hoặc PayPal Sandbox) |
| **GET** | `/api/payment/vnpay-return` | Callback tiếp nhận kết quả thanh toán từ VNPay |
| **POST** | `/api/payment/paypal-return` | Xác thực và capture giao dịch thành công từ PayPal |
| **GET** | `/api/learning/my-courses` | Khoá học đã sở hữu của học viên |
| **GET** | `/api/learning/lessons/{id}` | Lấy bài học (gồm ký JWT token xem video private trên Mux) |
| **GET** | `/api/learning/ebook/{id}` | Tải file PDF ebook thông qua token ký tạm thời 1 giờ |
| **POST** | `/api/learning/progress` | Lưu tiến trình bài học (hoàn thành 100% để đủ điều kiện cấp chứng nhận) |
| **GET** | `/api/certificates/{id}` | Xuất chứng chỉ hoàn thành khoá học dạng file vector SVG sắc nét |
| **GET** | `/api/admin/stats` | Biểu đồ doanh thu admin theo ngày/tuần/tháng/năm |
| **GET** | `/swagger` | Tài liệu tương tác API Swagger UI |

---

## 🛠️ Phát triển Local (Không dùng Docker)

### 1. Chuẩn bị Cơ sở dữ liệu
* Cài đặt **MySQL Server** trên máy local của bạn.
* Chạy các câu lệnh trong file [init.sql](file:///d:/ecommerce-learning-platform/database/init.sql) để tạo cấu trúc cơ sở dữ liệu `elearning`.

### 2. Cài đặt và chạy Backend .NET 8
* Yêu cầu cài đặt **SDK .NET 8.0** trên hệ điều hành.
* Di chuyển vào thư mục backend:
  ```bash
  cd backend-dotnet
  ```
* Chỉnh sửa cấu hình chuỗi kết nối MySQL tại `"DefaultConnection"` trong [appsettings.json](file:///d:/ecommerce-learning-platform/backend-dotnet/appsettings.json) trỏ đến MySQL local của bạn.
* Build và khởi chạy dự án (hệ thống sẽ tự động tạo Schema và seed dữ liệu mẫu trong lần chạy đầu tiên):
  ```bash
  dotnet run
  ```
  Ứng dụng backend sẽ chạy tại: `http://localhost:8000`

### 3. Khởi chạy Frontend
* Bạn có thể sử dụng bất kỳ HTTP Server đơn giản nào (ví dụ: Live Server trong VS Code, `http-server` của Node.js, hoặc Nginx local).
* Đảm bảo frontend được host tại root của thư mục `frontend/`.
* Các request tới `/api/*` phải được định tuyến (proxy) hoặc gọi trực tiếp về cổng backend `http://localhost:8000/api/*`.
