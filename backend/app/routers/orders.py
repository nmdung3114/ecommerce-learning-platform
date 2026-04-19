from fastapi import APIRouter, Depends, Query
from sqlalchemy.orm import Session, joinedload
from datetime import datetime, timedelta
from typing import Optional
from app.core.timezone import now_vn
from app.database import get_db
from app.schemas.order import (
    OrderResponse, OrderItemResponse, PaymentResponse,
    CreateOrderRequest, OrdersListResponse, CouponValidateRequest, CouponResponse
)
from app.models.order import Order, OrderItem, Payment, Coupon, UserAccess
from app.models.cart import Cart, CartItem
from app.models.product import Product, Module, Lesson
from app.models.course import LearningProgress
from app.core.exceptions import NotFoundException, BadRequestException
from app.dependencies import get_current_user
from app.models.user import User
from decimal import Decimal

router = APIRouter(prefix="/api/orders", tags=["orders"])


def _compute_discount(coupon: Coupon, subtotal: Decimal) -> Decimal:
    if not coupon or not coupon.is_active:
        return Decimal("0")
    if coupon.expired_date and coupon.expired_date < now_vn().replace(tzinfo=None):
        return Decimal("0")
    if coupon.min_order_amount and subtotal < coupon.min_order_amount:
        return Decimal("0")
    if coupon.discount_type == "percent":
        return round(subtotal * coupon.discount / 100, 0)
    return min(coupon.discount, subtotal)


def _assert_coupon_eligible(coupon: Coupon, subtotal: Decimal) -> Decimal:
    """
    Kiểm tra mã có thể áp dụng cho subtotal (hạn, lượt, đơn tối thiểu).
    Trả về số tiền giảm; ném BadRequestException nếu không đủ điều kiện.
    """
    if not coupon.is_active:
        raise BadRequestException("Mã giảm giá không hợp lệ")
    if coupon.expired_date and coupon.expired_date < now_vn().replace(tzinfo=None):
        raise BadRequestException("Mã giảm giá đã hết hạn")
    if coupon.usage_limit and (coupon.used_count or 0) >= coupon.usage_limit:
        raise BadRequestException("Mã giảm giá đã hết lượt sử dụng")
    if coupon.min_order_amount and subtotal < coupon.min_order_amount:
        raise BadRequestException(f"Đơn hàng tối thiểu {coupon.min_order_amount:,.0f}đ")
    discount = _compute_discount(coupon, subtotal)
    return discount


@router.post("/validate-coupon", response_model=dict)
def validate_coupon(
    data: CouponValidateRequest,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    coupon = db.query(Coupon).filter(Coupon.code == data.code).first()
    if not coupon or not coupon.is_active:
        raise NotFoundException("Mã giảm giá không hợp lệ")
    discount = _assert_coupon_eligible(coupon, data.order_amount)
    return {"valid": True, "discount": discount, "discount_type": coupon.discount_type}


@router.post("", response_model=OrderResponse)
def create_order(
    data: CreateOrderRequest,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    cart = db.query(Cart).options(
        joinedload(Cart.items).joinedload(CartItem.product)
    ).filter(Cart.user_id == current_user.user_id).first()

    if not cart or not cart.items:
        raise BadRequestException("Giỏ hàng trống")

    subtotal = sum(item.price * item.quantity for item in cart.items)
    discount = Decimal("0")
    coupon = None

    if data.coupon_code:
        coupon = db.query(Coupon).filter(Coupon.code == data.coupon_code).first()
        if not coupon:
            raise BadRequestException("Mã giảm giá không tồn tại")
        discount = _assert_coupon_eligible(coupon, subtotal)
        coupon.used_count = (coupon.used_count or 0) + 1

    total = max(subtotal - discount, Decimal("0"))

    order = Order(
        user_id=current_user.user_id,
        coupon_code=data.coupon_code if coupon is not None else None,
        subtotal=subtotal,
        discount_amount=discount,
        total_amount=total,
        status="pending",
    )
    db.add(order)
    db.flush()  # get order_id

    for item in cart.items:
        oi = OrderItem(
            order_id=order.order_id,
            product_id=item.product_id,
            quantity=item.quantity,
            price=item.price,
        )
        db.add(oi)

    payment = Payment(order_id=order.order_id, status="pending", amount=total)
    db.add(payment)
    db.commit()
    db.refresh(order)

    items_resp = [
        OrderItemResponse(
            order_item_id=i.order_item_id, product_id=i.product_id,
            product_name=i.product.name if i.product else None,
            product_thumbnail=i.product.thumbnail_url if i.product else None,
            product_type=i.product.product_type if i.product else None,
            quantity=i.quantity, price=i.price,
        ) for i in order.items
    ]
    return OrderResponse(
        order_id=order.order_id, user_id=order.user_id,
        coupon_code=order.coupon_code, subtotal=order.subtotal,
        discount_amount=order.discount_amount, total_amount=order.total_amount,
        status=order.status, created_at=order.created_at,
        items=items_resp, payment=PaymentResponse(
            payment_id=order.payment.payment_id, method=order.payment.method,
            status=order.payment.status, amount=order.payment.amount,
        ) if order.payment else None,
    )


@router.get("", response_model=OrdersListResponse)
def list_orders(
    page: int = Query(1, ge=1),
    page_size: int = Query(10, ge=1, le=50),
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    query = db.query(Order).options(
        joinedload(Order.items).joinedload(OrderItem.product),
        joinedload(Order.payment),
    ).filter(Order.user_id == current_user.user_id).order_by(Order.created_at.desc())

    total = query.count()
    orders = query.offset((page - 1) * page_size).limit(page_size).all()

    orders_resp = []
    for o in orders:
        items_resp = [
            OrderItemResponse(
                order_item_id=i.order_item_id, product_id=i.product_id,
                product_name=i.product.name if i.product else None,
                product_thumbnail=i.product.thumbnail_url if i.product else None,
                product_type=i.product.product_type if i.product else None,
                quantity=i.quantity, price=i.price,
            ) for i in o.items
        ]
        payment_resp = None
        if o.payment:
            payment_resp = PaymentResponse(
                payment_id=o.payment.payment_id, method=o.payment.method,
                status=o.payment.status, transaction_id=o.payment.transaction_id,
                paid_at=o.payment.paid_at, amount=o.payment.amount,
            )
        orders_resp.append(OrderResponse(
            order_id=o.order_id, user_id=o.user_id,
            coupon_code=o.coupon_code, subtotal=o.subtotal,
            discount_amount=o.discount_amount, total_amount=o.total_amount,
            status=o.status, created_at=o.created_at,
            items=items_resp, payment=payment_resp,
        ))
    return OrdersListResponse(orders=orders_resp, total=total, page=page, page_size=page_size)


@router.get("/{order_id}", response_model=OrderResponse)
def get_order(
    order_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    order = db.query(Order).options(
        joinedload(Order.items).joinedload(OrderItem.product),
        joinedload(Order.payment),
    ).filter(Order.order_id == order_id, Order.user_id == current_user.user_id).first()
    if not order:
        raise NotFoundException("Đơn hàng không tồn tại")

    items_resp = [
        OrderItemResponse(
            order_item_id=i.order_item_id, product_id=i.product_id,
            product_name=i.product.name if i.product else None,
            product_thumbnail=i.product.thumbnail_url if i.product else None,
            product_type=i.product.product_type if i.product else None,
            quantity=i.quantity, price=i.price,
        ) for i in order.items
    ]
    payment_resp = None
    if order.payment:
        payment_resp = PaymentResponse(
            payment_id=order.payment.payment_id, method=order.payment.method,
            status=order.payment.status, transaction_id=order.payment.transaction_id,
            paid_at=order.payment.paid_at, amount=order.payment.amount,
        )
    return OrderResponse(
        order_id=order.order_id, user_id=order.user_id,
        coupon_code=order.coupon_code, subtotal=order.subtotal,
        discount_amount=order.discount_amount, total_amount=order.total_amount,
        status=order.status, created_at=order.created_at,
        items=items_resp, payment=payment_resp,
    )


@router.delete("/{order_id}")
def cancel_order(
    order_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    """Hủy đơn hàng đang chờ thanh toán (pending)."""
    order = db.query(Order).filter(
        Order.order_id == order_id,
        Order.user_id == current_user.user_id,
    ).first()
    if not order:
        raise NotFoundException("Đơn hàng không tồn tại")
    if order.status != "pending":
        raise BadRequestException("Chỉ có thể hủy đơn hàng đang chờ thanh toán")
    order.status = "cancelled"
    if order.payment:
        order.payment.status = "cancelled"

    # Thông báo user + admin
    from app.services.notification_service import notify_order_cancelled
    notify_order_cancelled(db, order_id=order_id, user_id=current_user.user_id)

    db.commit()
    return {"message": "Đã hủy đơn hàng thành công"}


@router.post("/{order_id}/refund-request")
def request_refund(
    order_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    """
    User tự yêu cầu hoàn tiền. Điều kiện:
    - Đơn hàng đã thanh toán (paid)
    - Trong vòng 3 ngày kể từ thời điểm thanh toán
    - Với khóa học: chưa hoàn thành quá 10% tổng bài học
    - Với ebook: chưa từng mở (accessed_at = None)
    """
    order = db.query(Order).options(
        joinedload(Order.items).joinedload(OrderItem.product),
        joinedload(Order.payment),
    ).filter(
        Order.order_id == order_id,
        Order.user_id == current_user.user_id,
    ).first()

    if not order:
        raise NotFoundException("Đơn hàng không tồn tại")
    if order.status != "paid":
        raise BadRequestException("Chỉ có thể hoàn tiền đơn hàng đã thanh toán")

    # ── Kiểm tra 3 ngày ───────────────────────────────────────
    paid_at = order.payment.paid_at if order.payment else None
    if not paid_at:
        raise BadRequestException("Không tìm thấy thông tin thanh toán")
    # paid_at từ MySQL là naive datetime (stored as VN time), so_sánh với now_vn() stripped
    now_naive = now_vn().replace(tzinfo=None)
    if now_naive - paid_at > timedelta(days=3):
        raise BadRequestException(
            "Đã quá 3 ngày kể từ khi thanh toán, không thể yêu cầu hoàn tiền"
        )

    # ── Kiểm tra điều kiện từng sản phẩm ─────────────────────
    total_lessons = 0
    completed_lessons = 0

    for item in order.items:
        product = item.product
        if not product:
            continue

        if product.product_type == "course":
            # Tổng bài học trong khóa học
            course_total = db.query(Lesson).join(Module).filter(
                Module.course_id == product.product_id
            ).count()
            total_lessons += course_total

            # Số bài đã hoàn thành
            course_completed = (
                db.query(LearningProgress)
                .join(Lesson)
                .join(Module)
                .filter(
                    Module.course_id == product.product_id,
                    LearningProgress.user_id == current_user.user_id,
                    LearningProgress.completed == True,
                )
                .count()
            )
            completed_lessons += course_completed

        elif product.product_type == "ebook":
            # Ebook đã mở → từ chối hoàn tiền
            access = db.query(UserAccess).filter(
                UserAccess.user_id == current_user.user_id,
                UserAccess.product_id == product.product_id,
                UserAccess.is_active == True,
            ).first()
            if access and access.accessed_at is not None:
                raise BadRequestException(
                    f"Bạn đã mở ebook \"{product.name}\", không thể hoàn tiền"
                )

    # ── Kiểm tra ngưỡng 10% khóa học ─────────────────────────
    if total_lessons > 0:
        progress_pct = completed_lessons / total_lessons
        if progress_pct >= 0.1:
            raise BadRequestException(
                f"Bạn đã hoàn thành {completed_lessons}/{total_lessons} bài học "
                f"({progress_pct * 100:.0f}%), vượt quá 10% không thể hoàn tiền"
            )

    # ── Xử lý hoàn tiền ──────────────────────────────────────
    order.status = "refunded"
    if order.payment:
        order.payment.status = "refunded"

    # Thu hồi quyền truy cập tất cả sản phẩm trong đơn
    for item in order.items:
        access = db.query(UserAccess).filter(
            UserAccess.user_id == current_user.user_id,
            UserAccess.product_id == item.product_id,
        ).first()
        if access:
            access.is_active = False
            access.revoked_at = now_vn()

    # Thông báo user + admin
    from app.services.notification_service import notify_refund_requested
    notify_refund_requested(
        db,
        order_id=order_id,
        user_id=current_user.user_id,
        amount=float(order.total_amount),
    )

    db.commit()
    return {"message": "Yêu cầu hoàn tiền thành công. Quyền truy cập đã được thu hồi."}

