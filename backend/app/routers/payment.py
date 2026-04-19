from fastapi import APIRouter, Depends, Request, Query
from fastapi.responses import RedirectResponse, JSONResponse
from sqlalchemy.orm import Session
from typing import Optional
import logging
import unicodedata
from app.database import get_db
from app.models.order import Order, Payment
from app.models.cart import Cart, CartItem
from app.services.vnpay_service import create_payment_url
from app.services.payment_service import process_vnpay_return
from app.services.paypal_service import create_paypal_order, capture_paypal_order
from app.core.exceptions import NotFoundException, BadRequestException
from app.dependencies import get_current_user
from app.models.user import User
from app.core.timezone import now_vn
from decimal import Decimal


logger = logging.getLogger(__name__)


def _ascii_safe(text: str) -> str:
    """Chuyển text tiếng Việt thành ASCII để dùng trong VNPay OrderInfo."""
    nfkd = unicodedata.normalize('NFKD', text)
    return ''.join(c for c in nfkd if unicodedata.category(c) != 'Mn')

router = APIRouter(prefix="/api/payment", tags=["payment"])


@router.post("/create/{order_id}")
def create_payment(
    order_id: int,
    request: Request,
    bank_code: Optional[str] = None,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    """Tạo VNPay payment URL và redirect sang cổng thanh toán."""
    order = db.query(Order).filter(
        Order.order_id == order_id,
        Order.user_id == current_user.user_id,
    ).first()
    if not order:
        raise NotFoundException("Đơn hàng không tồn tại")
    if order.status == "paid":
        raise BadRequestException("Đơn hàng đã được thanh toán")
    # Cho phép retry nếu đơn bị cancelled do thanh toán lỗi trước
    if order.status == "cancelled":
        order.status = "pending"
        db.commit()

    client_ip = request.client.host if request.client else "127.0.0.1"
    # Dùng ASCII để tránh lỗi encoding khi tính HMAC với VNPay
    safe_name = _ascii_safe(current_user.name)
    order_desc = f"Thanh toan don hang #{order_id} - {safe_name}"

    logger.info(f"Creating VNPay payment for order #{order_id}, amount={order.total_amount}")
    payment_url, txn_ref = create_payment_url(
        order_id=order_id,
        amount=float(order.total_amount),
        order_desc=order_desc,
        client_ip=client_ip,
        bank_code=bank_code or "",
    )
    logger.info(f"VNPay URL created: {payment_url[:80]}...")

    # Update payment with txn_ref
    payment = db.query(Payment).filter(Payment.order_id == order_id).first()
    if payment:
        payment.vnpay_txn_ref = txn_ref
        db.commit()

    # Clear cart after creating payment
    cart = db.query(Cart).filter(Cart.user_id == current_user.user_id).first()
    if cart:
        db.query(CartItem).filter(CartItem.cart_id == cart.cart_id).delete()
        db.commit()

    return {"payment_url": payment_url, "order_id": order_id}


@router.get("/vnpay-return")
def vnpay_return(request: Request, db: Session = Depends(get_db)):
    """VNPay callback — verify and update order status."""
    params = dict(request.query_params)
    logger.info(f"VNPay callback received. Params: {params}")
    result = process_vnpay_return(db, params)
    logger.info(f"VNPay result: {result}")

    order_id = result.get("order_id", 0)
    if result["success"]:
        # Gửi thông báo cho user + admin
        try:
            from app.models.order import Order as OrderModel
            from app.services.notification_service import notify_payment_success
            order_obj = db.query(OrderModel).filter(OrderModel.order_id == order_id).first()
            if order_obj:
                notify_payment_success(
                    db,
                    order_id=order_id,
                    user_id=order_obj.user_id,
                    amount=float(order_obj.total_amount),
                )
                db.commit()
        except Exception as e:
            logger.warning(f"Could not create notification: {e}")

        return RedirectResponse(
            url=f"/orders/index.html?order_id={order_id}&status=success",
            status_code=302,
        )
    else:
        code = result.get("code", "99")
        return RedirectResponse(
            url=f"/checkout/index.html?order_id={order_id}&status=failed&code={code}",
            status_code=302,
        )


def _vnpay_ipn_json(rsp_code: str, message: str) -> JSONResponse:
    """VNPay IPN yêu cầu HTTP 200 + JSON RspCode/Message (tài liệu cổng 2.x)."""
    return JSONResponse(
        status_code=200,
        content={"RspCode": rsp_code, "Message": message},
    )


@router.api_route("/vnpay-ipn", methods=["GET", "POST"])
async def vnpay_ipn(request: Request, db: Session = Depends(get_db)):
    """
    IPN (Instant Payment Notification) — VNPay gọi server-to-server.
    Xử lý idempotent qua process_vnpay_return (đơn đã paid → success, không cấp quyền trùng).
    """
    merged: dict[str, str] = {}
    for key, value in request.query_params.multi_items():
        merged[key] = value
    if request.method == "POST":
        try:
            form = await request.form()
            for key in form:
                merged[str(key)] = str(form.get(key))
        except Exception as e:
            logger.warning("VNPay IPN: could not read form body: %s", e)

    if not merged.get("vnp_SecureHash"):
        return _vnpay_ipn_json("97", "Missing checksum")

    result = process_vnpay_return(db, dict(merged))

    if not result.get("success"):
        msg = (result.get("message") or "")[:250]
        if "Invalid signature" in msg:
            return _vnpay_ipn_json("97", "Invalid checksum")
        if msg in ("Order not found", "Payment record not found"):
            return _vnpay_ipn_json("01", msg)
        # Giao dịch không thành công (vnp_ResponseCode != 00) nhưng đã cập nhật DB — vẫn báo 00
        return _vnpay_ipn_json("00", "Confirm Success")

    order_id = int(result.get("order_id") or 0)
    if result.get("message") == "Payment successful":
        try:
            from app.models.order import Order as OrderModel
            from app.services.notification_service import notify_payment_success

            order_obj = db.query(OrderModel).filter(OrderModel.order_id == order_id).first()
            if order_obj:
                notify_payment_success(
                    db,
                    order_id=order_id,
                    user_id=order_obj.user_id,
                    amount=float(order_obj.total_amount),
                )
                db.commit()
        except Exception as e:
            logger.warning("VNPay IPN: notification skipped: %s", e)

    return _vnpay_ipn_json("00", "Confirm Success")


@router.get("/status/{order_id}")
def get_payment_status(
    order_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    payment = db.query(Payment).join(Order).filter(
        Payment.order_id == order_id,
        Order.user_id == current_user.user_id,
    ).first()
    if not payment:
        raise NotFoundException("Thông tin thanh toán không tìm thấy")
    return {
        "order_id": order_id,
        "status": payment.status,
        "method": payment.method,
        "transaction_id": payment.transaction_id,
        "paid_at": payment.paid_at,
        "amount": payment.amount,
    }


# ── PayPal Sandbox ────────────────────────────────────────────────────────────

@router.post("/paypal/create/{order_id}")
def create_paypal_payment(
    order_id: int,
    db: Session = Depends(get_db),
    current_user: User = Depends(get_current_user),
):
    """Tạo PayPal order và trả về approve_url để redirect user sang PayPal sandbox."""
    order = db.query(Order).filter(
        Order.order_id == order_id,
        Order.user_id == current_user.user_id,
    ).first()
    if not order:
        raise NotFoundException("Đơn hàng không tồn tại")
    if order.status == "paid":
        raise BadRequestException("Đơn hàng đã được thanh toán")
    if order.status == "cancelled":
        order.status = "pending"
        db.commit()

    try:
        result = create_paypal_order(order_id, float(order.total_amount))
    except Exception as e:
        logger.error(f"PayPal create order error: {e}")
        raise BadRequestException(f"Không thể tạo PayPal order: {str(e)}")

    # Lưu paypal_order_id vào payment record (tái dùng cột vnpay_txn_ref)
    payment = db.query(Payment).filter(Payment.order_id == order_id).first()
    if payment:
        payment.method = "paypal"
        payment.vnpay_txn_ref = result["paypal_order_id"]
        db.commit()

    # Clear cart
    cart = db.query(Cart).filter(Cart.user_id == current_user.user_id).first()
    if cart:
        db.query(CartItem).filter(CartItem.cart_id == cart.cart_id).delete()
        db.commit()

    logger.info(f"PayPal order created for order #{order_id}: {result['paypal_order_id']}")
    return {
        "approve_url": result["approve_url"],
        "paypal_order_id": result["paypal_order_id"],
        "usd_amount": result["usd_amount"],
        "order_id": order_id,
    }


@router.get("/paypal-return")
def paypal_return(
    order_id: int,
    token: str,          # PayPal đặt tên param là "token" (= PayPal order ID)
    PayerID: Optional[str] = None,
    db: Session = Depends(get_db),
):
    """
    PayPal redirect callback sau khi user approve trên sandbox.
    PayPal tự append: ?token=<paypal_order_id>&PayerID=<payer_id>
    """
    logger.info(f"PayPal return: order_id={order_id}, paypal_token={token}, PayerID={PayerID}")

    order = db.query(Order).filter(Order.order_id == order_id).first()
    if not order:
        return RedirectResponse(
            url=f"/checkout/index.html?order_id={order_id}&status=failed&code=not_found",
            status_code=302,
        )

    if order.status == "paid":
        return RedirectResponse(
            url=f"/orders/index.html?order_id={order_id}&status=success",
            status_code=302,
        )

    try:
        result = capture_paypal_order(token)
    except Exception as e:
        logger.error(f"PayPal capture error: {e}")
        if order.payment:
            order.payment.status = "failed"
            order.payment.method = "paypal"
        order.status = "cancelled"
        db.commit()
        return RedirectResponse(
            url=f"/checkout/index.html?order_id={order_id}&status=failed&code=capture_failed",
            status_code=302,
        )

    payment = db.query(Payment).filter(Payment.order_id == order_id).first()

    if result["success"]:
        order.status = "paid"
        order.updated_at = now_vn()
        if payment:
            payment.status = "success"
            payment.method = "paypal"
            payment.transaction_id = result["capture_id"]
            payment.paid_at = now_vn()
            payment.amount = order.total_amount  # giữ VND gốc

        # Cấp quyền truy cập
        from app.services.payment_service import _grant_access
        _grant_access(db, order)
        db.commit()

        # Thông báo
        try:
            from app.services.notification_service import notify_payment_success
            notify_payment_success(
                db,
                order_id=order_id,
                user_id=order.user_id,
                amount=float(order.total_amount),
            )
            db.commit()
        except Exception as e:
            logger.warning(f"Notification error: {e}")

        logger.info(f"PayPal payment SUCCESS: order #{order_id}, capture={result['capture_id']}")
        return RedirectResponse(
            url=f"/orders/index.html?order_id={order_id}&status=success",
            status_code=302,
        )
    else:
        order.status = "cancelled"
        if payment:
            payment.status = "failed"
            payment.method = "paypal"
        db.commit()
        logger.warning(f"PayPal capture NOT COMPLETED: {result}")
        return RedirectResponse(
            url=f"/checkout/index.html?order_id={order_id}&status=failed&code=paypal_failed",
            status_code=302,
        )

