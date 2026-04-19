"""
VNPay Sandbox Integration Service — theo tài liệu chính thức VNPay 2.1.0
https://sandbox.vnpayment.vn/apis/docs/thanh-toan-pay/pay.html

Nguyên tắc tính chữ ký (theo Python sample chính thức của VNPay):
  - Sort params theo alphabetical
  - Build hash string: key=quote_plus(value) nối bằng &
  - Dùng HMAC-SHA512 với HASH_SECRET
  - Hash string và URL query string XÂY DỰNG GIỐNG NHAU (đều quote_plus)
"""
import hashlib
import hmac
import urllib.parse
from datetime import datetime, timedelta, timezone
from typing import Dict, Any
from app.config import settings


VNPAY_RESPONSE_CODES = {
    "00": "Giao dịch thành công",
    "07": "Trừ tiền thành công. Giao dịch bị nghi ngờ gian lận",
    "09": "Chưa đăng ký Internet Banking",
    "10": "Xác thực thông tin không đúng quá 3 lần",
    "11": "Hết thời gian thanh toán",
    "12": "Thẻ/Tài khoản bị khóa",
    "13": "OTP sai",
    "24": "Giao dịch hủy",
    "51": "Tài khoản không đủ số dư",
    "65": "Vượt hạn mức giao dịch trong ngày",
    "75": "Ngân hàng bảo trì",
    "79": "Nhập sai mật khẩu quá số lần quy định",
    "99": "Lỗi khác",
}

# Múi giờ Việt Nam (UTC+7) — VNPay yêu cầu thời gian theo giờ VN
VN_TZ = timezone(timedelta(hours=7))


def _hmac_sha512(key: str, data: str) -> str:
    byte_key = key.encode("utf-8")
    byte_data = data.encode("utf-8")
    return hmac.new(byte_key, byte_data, hashlib.sha512).hexdigest()


def _build_vnpay_query(params: Dict[str, str]) -> str:
    """
    Build query string CHÍNH XÁC theo Python sample chính thức của VNPay:
    - Sort alphabetically
    - Mỗi value được quote_plus (giống urlencode của PHP)
    - Dùng cho cả tính HASH lẫn tạo URL
    """
    hash_data = ""
    i = 0
    for key, val in sorted(params.items()):
        if val and val != "":
            if i == 1:
                hash_data += "&" + key + "=" + urllib.parse.quote_plus(str(val), safe="")
            else:
                hash_data += key + "=" + urllib.parse.quote_plus(str(val), safe="")
            i = 1
    return hash_data


def create_payment_url(
    order_id: int,
    amount: float,
    order_desc: str,
    client_ip: str,
    bank_code: str = "",
    locale: str = "vn",
) -> tuple:
    """
    Build VNPay payment URL với HMAC-SHA512 signature.
    Theo đúng tài liệu VNPay v2.1.0.
    """
    # Thời gian theo giờ Việt Nam (VNPay server ở VN)
    now = datetime.now(VN_TZ)
    vnp_create_date = now.strftime("%Y%m%d%H%M%S")
    vnp_expire_date = (now + timedelta(minutes=15)).strftime("%Y%m%d%H%M%S")

    # VNPay amount = số tiền * 100 (không có dấu thập phân)
    vnp_amount = int(amount * 100)
    vnp_txn_ref = f"{order_id}_{now.strftime('%Y%m%d%H%M%S')}"

    # Chỉ dùng ASCII, không dùng ký tự đặc biệt trong OrderInfo
    safe_desc = order_desc.encode("ascii", errors="ignore").decode("ascii")
    safe_desc = "".join(c for c in safe_desc if c.isalnum() or c in " -_")[:255]

    params: Dict[str, str] = {
        "vnp_Version":   "2.1.0",
        "vnp_Command":   "pay",
        "vnp_TmnCode":   settings.VNPAY_TMN_CODE,
        "vnp_Amount":    str(vnp_amount),
        "vnp_CurrCode":  "VND",
        "vnp_TxnRef":    vnp_txn_ref,
        "vnp_OrderInfo": safe_desc,
        "vnp_OrderType": "other",
        "vnp_Locale":    locale,
        "vnp_ReturnUrl": settings.VNPAY_RETURN_URL,
        "vnp_IpAddr":    client_ip or "127.0.0.1",
        "vnp_CreateDate": vnp_create_date,
        "vnp_ExpireDate": vnp_expire_date,
    }
    if bank_code:
        params["vnp_BankCode"] = bank_code

    ipn = (settings.VNPAY_IPN_URL or "").strip()
    if ipn:
        params["vnp_IpnUrl"] = ipn

    # Tính hash TRÊN CÙNG query string với URL (quote_plus values)
    query_string = _build_vnpay_query(params)
    secure_hash = _hmac_sha512(settings.VNPAY_HASH_SECRET, query_string)

    payment_url = f"{settings.VNPAY_URL}?{query_string}&vnp_SecureHash={secure_hash}"
    return payment_url, vnp_txn_ref


def verify_vnpay_callback(params: Dict[str, str]) -> Dict[str, Any]:
    """
    Verify VNPay callback signature.
    FastAPI tự decode URL params → dùng quote_plus lại để rebuild hash giống VNPay.
    """
    vnp_secure_hash = params.pop("vnp_SecureHash", "")
    params.pop("vnp_SecureHashType", None)

    # Rebuild hash giống cách VNPay tính (quote_plus values, sorted)
    query_string = _build_vnpay_query(params)
    computed_hash = _hmac_sha512(settings.VNPAY_HASH_SECRET, query_string)

    is_valid = hmac.compare_digest(computed_hash.lower(), vnp_secure_hash.lower())
    response_code = params.get("vnp_ResponseCode", "99")
    txn_ref = params.get("vnp_TxnRef", "")
    transaction_id = params.get("vnp_TransactionNo", "")
    amount = int(params.get("vnp_Amount", "0")) / 100  # VNPay gửi x100

    # Lấy order_id từ txn_ref (format: {order_id}_{datetime})
    try:
        order_id = int(txn_ref.split("_")[0]) if "_" in txn_ref else 0
    except (ValueError, IndexError):
        order_id = 0

    return {
        "is_valid": is_valid,
        "is_success": is_valid and response_code == "00",
        "response_code": response_code,
        "response_message": VNPAY_RESPONSE_CODES.get(response_code, "Lỗi không xác định"),
        "order_id": order_id,
        "txn_ref": txn_ref,
        "transaction_id": transaction_id,
        "amount": amount,
        "raw_params": params,
    }
