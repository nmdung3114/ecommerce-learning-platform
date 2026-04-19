# Nộp bài — GitHub nhóm Backend (BTL 2 chức năng)

**Repository nộp thầy:** [https://github.com/nmdung3114/Nhom6-3-TMDT-Backend](https://github.com/nmdung3114/Nhom6-3-TMDT-Backend)

## Mô hình nhánh

| Nhánh | Mục đích |
|--------|----------|
| `main` | Mã ổn định, đủ chức năng nộp bài |
| `develop` | Nhánh tích hợp (merge từ feature) |
| `feature/chức-năng-1` | **Chức năng 1:** Quản trị sản phẩm số phân cấp (admin/author): `products` + `courses`/`ebooks`, `modules`, `lessons`, Mux |
| `feature/chức-năng-2` | **Chức năng 2:** Giỏ hàng, đơn hàng, coupon, VNPay, `user_access`, hoàn tiền |

Hai nhánh `feature/*` trỏ cùng codebase đầy đủ; phân biệt theo **danh sách file** dưới đây (đúng quy trình Git Flow: feature → develop → main).

---

## Chức năng 1 — file / thư mục chính

- `database/init.sql` — bảng `products`, `courses`, `ebooks`, `modules`, `lessons`
- `database/erd_btl_hai_chuc_nang.puml` — ERD (phần CN1)
- `backend/app/models/product.py`
- `backend/app/routers/admin.py` (quản trị / duyệt sản phẩm)
- `backend/app/routers/instructor.py` (tác giả: khóa học, chương, bài, draft)
- `backend/app/schemas/product.py`

---

## Chức năng 2 — file / thư mục chính

- `database/init.sql` — `carts`, `cart_items`, `coupons`, `orders`, `order_items`, `payments`, `user_access`, `learning_progress`
- `database/erd_btl_hai_chuc_nang.puml` — ERD (phần CN2)
- `backend/app/models/order.py`, `backend/app/models/cart.py`
- `backend/app/routers/cart.py`, `backend/app/routers/orders.py`, `backend/app/routers/payment.py`
- `backend/app/services/vnpay_service.py`, `backend/app/services/payment_service.py`
- `backend/app/config.py` — `VNPAY_*`, `VNPAY_IPN_URL`
- `backend/app/schemas/order.py`

---

## Clone & remote (máy mới)

```bash
git clone https://github.com/nmdung3114/Nhom6-3-TMDT-Backend.git
cd Nhom6-3-TMDT-Backend
git checkout develop
# hoặc: git checkout feature/chức-năng-1
```

Thêm song song remote gốc monorepo (nếu cần):

```bash
git remote add upstream https://github.com/nmdung3114/ecommerce-learning-platform.git
```

---

## Push từ máy đang code (đã có `origin` = ecommerce-learning-platform)

```bash
git remote add nhom6 https://github.com/nmdung3114/Nhom6-3-TMDT-Backend.git
git push -u nhom6 main --force
git push nhom6 develop --force
git push nhom6 "feature/chức-năng-1" --force
git push nhom6 "feature/chức-năng-2" --force
```

Lần đầu nếu remote đã có README commit khác, dùng `--force` để ghi đè bằng toàn bộ dự án (đã thống nhất nộp bài).
