# THIẾT KẾ CƠ SỞ DỮ LIỆU (DATABASE SCHEMA) HỆ THỐNG ELearnVN

Dưới đây là chi tiết các bảng trong cơ sở dữ liệu của hệ thống, liệt kê rõ các cột, kiểu dữ liệu, ràng buộc (Constraints) và mô tả tương ứng. Hệ thống sử dụng khóa chính dạng `UUID` để tăng tính bảo mật và khả năng phân tán dữ liệu.

| Bảng | Cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
| :--- | :--- | :--- | :--- | :--- |
| **users** | id | UUID | PRIMARY KEY, DEFAULT gen_random_uuid() | Mã người dùng |
| | email | VARCHAR(150) | UNIQUE, NOT NULL | Email đăng nhập |
| | full_name | VARCHAR(255) | NOT NULL | Họ và tên người dùng |
| | hashed_password | VARCHAR(255) | NOT NULL | Mật khẩu đã mã hóa (Hash) |
| | role | ENUM | NOT NULL, DEFAULT 'learner' ('admin', 'author', 'learner') | Vai trò hệ thống |
| | is_active | BOOLEAN | NOT NULL, DEFAULT TRUE | Trạng thái hoạt động |
| | created_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Thời gian tạo tài khoản |
| **categories** | id | UUID | PRIMARY KEY | Mã danh mục |
| | name | VARCHAR(255) | UNIQUE, NOT NULL | Tên danh mục |
| | description | TEXT | NULL | Mô tả chi tiết |
| | created_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Thời gian tạo danh mục |
| **products** | id | UUID | PRIMARY KEY | Mã sản phẩm (Khóa học/Ebook) |
| | category_id | UUID | FOREIGN KEY REFERENCES categories(id) | Thuộc danh mục nào |
| | author_id | UUID | FOREIGN KEY REFERENCES users(id) | Mã tác giả tạo sản phẩm |
| | title | VARCHAR(255) | NOT NULL | Tên sản phẩm |
| | description | TEXT | NULL | Mô tả chi tiết sản phẩm |
| | price | DECIMAL(15,2) | NOT NULL, DEFAULT 0 | Giá sản phẩm (VNĐ) |
| | product_type | ENUM | NOT NULL ('course', 'ebook') | Loại hình sản phẩm |
| | is_active | BOOLEAN | NOT NULL, DEFAULT TRUE | Sản phẩm đang được bán hay không |
| | created_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Thời gian đăng bán |
| **courses** | id | UUID | PRIMARY KEY | Mã bảng mở rộng khóa học |
| | product_id | UUID | FOREIGN KEY REFERENCES products(id), UNIQUE | Tham chiếu 1-1 tới products |
| | requirements | TEXT | NULL | Yêu cầu đầu vào khóa học |
| | learning_outcomes | TEXT | NULL | Kết quả đạt được sau khóa học |
| **ebooks** | id | UUID | PRIMARY KEY | Mã bảng mở rộng Ebook |
| | product_id | UUID | FOREIGN KEY REFERENCES products(id), UNIQUE | Tham chiếu 1-1 tới products |
| | file_url | VARCHAR(500) | NOT NULL | Đường dẫn file gốc trên Cloud Storage |
| | format | VARCHAR(50) | NOT NULL, DEFAULT 'PDF' | Định dạng sách (PDF, EPUB) |
| | pages | INT | NOT NULL, DEFAULT 0 | Tổng số trang |
| **modules** | id | UUID | PRIMARY KEY | Mã chương/phần học |
| | course_id | UUID | FOREIGN KEY REFERENCES courses(id) | Thuộc khóa học nào |
| | title | VARCHAR(255) | NOT NULL | Tên chương học |
| | order_num | INT | NOT NULL | Thứ tự hiển thị chương |
| | is_active | BOOLEAN | NOT NULL, DEFAULT TRUE | Trạng thái hiển thị |
| **lessons** | id | UUID | PRIMARY KEY | Mã bài học |
| | module_id | UUID | FOREIGN KEY REFERENCES modules(id) | Thuộc chương học nào |
| | title | VARCHAR(255) | NOT NULL | Tiêu đề bài học |
| | playback_id | VARCHAR(255)| NOT NULL | ID Video trên Mux để stream |
| | duration | INT | NOT NULL, DEFAULT 0 | Thời lượng video (giây) |
| | order_num | INT | NOT NULL | Thứ tự bài học |
| | is_preview | BOOLEAN | NOT NULL, DEFAULT FALSE | Cho phép xem trước miễn phí |
| | is_active | BOOLEAN | NOT NULL, DEFAULT TRUE | Trạng thái hiển thị bài học |
| **carts** | id | UUID | PRIMARY KEY | Mã giỏ hàng |
| | user_id | UUID | FOREIGN KEY REFERENCES users(id), UNIQUE | Mỗi người dùng có 1 giỏ hàng (1-1) |
| | total_amount| DECIMAL(15,2) | NOT NULL, DEFAULT 0 | Tổng tiền tạm tính |
| | updated_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Lần cập nhật cuối |
| **cart_items** | id | UUID | PRIMARY KEY | Mã chi tiết giỏ hàng |
| | cart_id | UUID | FOREIGN KEY REFERENCES carts(id) | Thuộc giỏ hàng nào |
| | product_id | UUID | FOREIGN KEY REFERENCES products(id) | Sản phẩm trong giỏ |
| | price | DECIMAL(15,2) | NOT NULL | Giá tại thời điểm bỏ vào giỏ |
| **coupons** | id | UUID | PRIMARY KEY | Mã giảm giá hệ thống |
| | code | VARCHAR(50) | UNIQUE, NOT NULL | Chuỗi mã Coupon (VD: Giam20K) |
| | discount_pct| DECIMAL(5,2) | NOT NULL | % giảm giá (VD: 10.00%) |
| | max_discount| DECIMAL(15,2) | NOT NULL | Số tiền giảm tối đa (VNĐ) |
| | valid_from | TIMESTAMP | NOT NULL | Thời điểm bắt đầu hiệu lực |
| | valid_until | TIMESTAMP | NOT NULL | Thời điểm hết hạn |
| | is_active | BOOLEAN | NOT NULL, DEFAULT TRUE | Tình trạng áp dụng mã |
| **orders** | id | UUID | PRIMARY KEY | Mã hóa đơn/đơn hàng |
| | user_id | UUID | FOREIGN KEY REFERENCES users(id) | Khách hàng thực hiện |
| | coupon_id | UUID | FOREIGN KEY REFERENCES coupons(id), NULL| Mã giảm giá áp dụng (nếu có) |
| | total_amount| DECIMAL(15,2) | NOT NULL | Tổng tiền ban đầu |
| | discount_amt| DECIMAL(15,2) | NOT NULL, DEFAULT 0 | Số tiền được giảm |
| | final_amount| DECIMAL(15,2) | NOT NULL | Tiền thực tế phải thanh toán |
| | status | ENUM | NOT NULL ('pending', 'paid', 'failed', 'refunded')| Trạng thái giao dịch |
| | pay_method | VARCHAR(50) | NOT NULL, DEFAULT 'VNPAY' | Cổng thanh toán sử dụng |
| | txn_ref | VARCHAR(100) | UNIQUE, NULL | Mã giao dịch tham chiếu của VNPay |
| | created_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Thời gian lập đơn |
| **order_items**| id | UUID | PRIMARY KEY | Mã chi tiết đơn hàng |
| | order_id | UUID | FOREIGN KEY REFERENCES orders(id) | Thuộc đơn hàng nào |
| | product_id | UUID | FOREIGN KEY REFERENCES products(id) | Sản phẩm nào được mua |
| | price | DECIMAL(15,2) | NOT NULL | Giá chốt mua tại thời điểm thanh toán|
| **user_access**| id | UUID | PRIMARY KEY | Mã quyền truy cập nội dung |
| | user_id | UUID | FOREIGN KEY REFERENCES users(id) | Ai được cấp quyền |
| | product_id | UUID | FOREIGN KEY REFERENCES products(id) | Truy cập vào khóa học/ebook nào |
| | is_active | BOOLEAN | NOT NULL, DEFAULT TRUE | Trạng thái quyền (Active/Revoked) |
| | expiry_date | TIMESTAMP | NULL | Ngày hết hạn (NULL = Vĩnh viễn) |
| | granted_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Thời gian được cấp quyền (sau khi Paid)|
| **progresses** | id | UUID | PRIMARY KEY | Mã tiến trình học tập |
| | user_id | UUID | FOREIGN KEY REFERENCES users(id) | Tiến độ của ai |
| | course_id | UUID | FOREIGN KEY REFERENCES courses(id) | Tiến độ của khóa học nào |
| | lesson_id | UUID | FOREIGN KEY REFERENCES lessons(id) | Tiến độ của bài học cụ thể |
| | is_completed| BOOLEAN | NOT NULL, DEFAULT FALSE | Đã học xong bài này chưa |
| | watch_time | INT | NOT NULL, DEFAULT 0 | Thời gian đã xem video (giây) |
| | updated_at | TIMESTAMP | NOT NULL, DEFAULT CURRENT_TIMESTAMP | Lần cuối học là khi nào |
