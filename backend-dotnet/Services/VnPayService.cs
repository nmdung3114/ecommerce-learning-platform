using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ELearnVN.Backend.Models;
using Microsoft.Extensions.Configuration;

namespace ELearnVN.Backend.Services
{
    public interface IVnPayService
    {
        (string PaymentUrl, string TxnRef) CreatePaymentUrl(int orderId, decimal amount, string orderDesc, string clientIp, string bankCode = "", string locale = "vn");
        VnPayCallbackResult VerifyCallback(Dictionary<string, string> queryParams);
    }

    public class VnPayCallbackResult
    {
        public bool IsValid { get; set; }
        public bool IsSuccess { get; set; }
        public string ResponseCode { get; set; } = null!;
        public string ResponseMessage { get; set; } = null!;
        public int OrderId { get; set; }
        public string TxnRef { get; set; } = null!;
        public string TransactionId { get; set; } = null!;
        public decimal Amount { get; set; }
        public Dictionary<string, string> RawParams { get; set; } = null!;
    }

    public class VnPayService : IVnPayService
    {
        private readonly IConfiguration _config;
        private static readonly Dictionary<string, string> ResponseCodes = new()
        {
            { "00", "Giao dịch thành công" },
            { "07", "Trừ tiền thành công. Giao dịch bị nghi ngờ gian lận" },
            { "09", "Chưa đăng ký Internet Banking" },
            { "10", "Xác thực thông tin không đúng quá 3 lần" },
            { "11", "Hết thời gian thanh toán" },
            { "12", "Thẻ/Tài khoản bị khóa" },
            { "13", "OTP sai" },
            { "24", "Giao dịch hủy" },
            { "51", "Tài khoản không đủ số dư" },
            { "65", "Vượt hạn mức giao dịch trong ngày" },
            { "75", "Ngân hàng bảo trì" },
            { "79", "Nhập sai mật khẩu quá số lần quy định" },
            { "99", "Lỗi khác" }
        };

        public VnPayService(IConfiguration config)
        {
            _config = config;
        }

        public (string PaymentUrl, string TxnRef) CreatePaymentUrl(
            int orderId,
            decimal amount,
            string orderDesc,
            string clientIp,
            string bankCode = "",
            string locale = "vn")
        {
            var tmnCode = _config["VnPay:TmnCode"] ?? "";
            var hashSecret = _config["VnPay:HashSecret"] ?? "";
            var vnpayUrl = _config["VnPay:Url"] ?? "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
            var returnUrl = _config["VnPay:ReturnUrl"] ?? "";
            var ipnUrl = _config["VnPay:IpnUrl"] ?? "";

            // Time in Vietnam timezone (UTC+7)
            var utcTime = DateTime.UtcNow;
            var vnTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"));
            var createDate = vnTime.ToString("yyyyMMddHHmmss");
            var expireDate = vnTime.AddMinutes(15).ToString("yyyyMMddHHmmss");

            var vnpAmount = (long)(amount * 100); // VNPay requires amount * 100
            var txnRef = $"{orderId}_{vnTime:yyyyMMddHHmmss}";

            // Sanitize order description: keep alphanumeric, space, hyphens, underscores
            var safeDesc = RemoveDiacritics(orderDesc);
            var sb = new StringBuilder();
            foreach (char c in safeDesc)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
                {
                    sb.Append(c);
                }
            }
            var cleanDesc = sb.ToString();
            if (cleanDesc.Length > 255)
            {
                cleanDesc = cleanDesc.Substring(0, 255);
            }

            var paramsMap = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "vnp_Version", "2.1.0" },
                { "vnp_Command", "pay" },
                { "vnp_TmnCode", tmnCode },
                { "vnp_Amount", vnpAmount.ToString() },
                { "vnp_CurrCode", "VND" },
                { "vnp_TxnRef", txnRef },
                { "vnp_OrderInfo", cleanDesc },
                { "vnp_OrderType", "other" },
                { "vnp_Locale", locale },
                { "vnp_ReturnUrl", returnUrl },
                { "vnp_IpAddr", string.IsNullOrEmpty(clientIp) ? "127.0.0.1" : clientIp },
                { "vnp_CreateDate", createDate },
                { "vnp_ExpireDate", expireDate }
            };

            if (!string.IsNullOrEmpty(bankCode))
            {
                paramsMap["vnp_BankCode"] = bankCode;
            }

            if (!string.IsNullOrEmpty(ipnUrl))
            {
                paramsMap["vnp_IpnUrl"] = ipnUrl;
            }

            var queryString = BuildQueryString(paramsMap);
            var secureHash = HmacSha512(hashSecret, queryString);
            var paymentUrl = $"{vnpayUrl}?{queryString}&vnp_SecureHash={secureHash}";

            return (paymentUrl, txnRef);
        }

        public VnPayCallbackResult VerifyCallback(Dictionary<string, string> queryParams)
        {
            var hashSecret = _config["VnPay:HashSecret"] ?? "";

            // Clone map and remove hash parameters
            var paramsMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string vnpSecureHash = "";

            foreach (var kv in queryParams)
            {
                if (kv.Key == "vnp_SecureHash")
                {
                    vnpSecureHash = kv.Value;
                }
                else if (kv.Key != "vnp_SecureHashType")
                {
                    paramsMap[kv.Key] = kv.Value;
                }
            }

            var queryString = BuildQueryString(paramsMap);
            var computedHash = HmacSha512(hashSecret, queryString);

            bool isValid = string.Equals(computedHash, vnpSecureHash, StringComparison.OrdinalIgnoreCase);

            paramsMap.TryGetValue("vnp_ResponseCode", out var responseCode);
            responseCode ??= "99";

            paramsMap.TryGetValue("vnp_TxnRef", out var txnRef);
            txnRef ??= "";

            paramsMap.TryGetValue("vnp_TransactionNo", out var transactionNo);
            transactionNo ??= "";

            paramsMap.TryGetValue("vnp_Amount", out var amountStr);
            decimal amount = 0;
            if (decimal.TryParse(amountStr, out var amountVal))
            {
                amount = amountVal / 100m;
            }

            int orderId = 0;
            if (!string.IsNullOrEmpty(txnRef) && txnRef.Contains('_'))
            {
                int.TryParse(txnRef.Split('_')[0], out orderId);
            }

            ResponseCodes.TryGetValue(responseCode, out var responseMsg);
            responseMsg ??= "Lỗi không xác định";

            return new VnPayCallbackResult
            {
                IsValid = isValid,
                IsSuccess = isValid && responseCode == "00",
                ResponseCode = responseCode,
                ResponseMessage = responseMsg,
                OrderId = orderId,
                TxnRef = txnRef,
                TransactionId = transactionNo,
                Amount = amount,
                RawParams = queryParams
            };
        }

        private string BuildQueryString(SortedDictionary<string, string> paramsMap)
        {
            var sb = new StringBuilder();
            foreach (var kv in paramsMap)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    if (sb.Length > 0)
                    {
                        sb.Append("&");
                    }
                    sb.Append(kv.Key);
                    sb.Append("=");
                    sb.Append(VnPayUrlEncode(kv.Value));
                }
            }
            return sb.ToString();
        }

        private string VnPayUrlEncode(string str)
        {
            var sb = new StringBuilder();
            foreach (char c in str)
            {
                if (c == ' ')
                {
                    sb.Append('+');
                }
                else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.' || c == '*')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('%');
                    sb.Append(((int)c).ToString("X2"));
                }
            }
            return sb.ToString();
        }

        private string HmacSha512(string key, string data)
        {
            var keyBytes = Encoding.UTF8.GetBytes(key);
            var dataBytes = Encoding.UTF8.GetBytes(data);
            using var hmac = new HMACSHA512(keyBytes);
            var hashBytes = hmac.ComputeHash(dataBytes);
            return Convert.ToHexString(hashBytes).ToLower();
        }

        private string RemoveDiacritics(string text)
        {
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
