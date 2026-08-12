using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ELearnVN.Backend.Services
{
    public interface IPayPalService
    {
        Task<string> GetAccessTokenAsync();
        Task<PayPalCreateOrderResult> CreatePayPalOrderAsync(int orderId, decimal amountVnd);
        Task<PayPalCaptureOrderResult> CapturePayPalOrderAsync(string paypalOrderId);
    }

    public class PayPalCreateOrderResult
    {
        public string PaypalOrderId { get; set; } = null!;
        public string ApproveUrl { get; set; } = null!;
        public string UsdAmount { get; set; } = null!;
    }

    public class PayPalCaptureOrderResult
    {
        public bool Success { get; set; }
        public string PaypalOrderId { get; set; } = null!;
        public string CaptureId { get; set; } = null!;
        public string Status { get; set; } = null!;
        public decimal UsdAmount { get; set; }
    }

    public class PayPalService : IPayPalService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;
        private readonly ILogger<PayPalService> _logger;

        public PayPalService(IConfiguration config, HttpClient httpClient, ILogger<PayPalService> logger)
        {
            _config = config;
            _httpClient = httpClient;
            _logger = logger;
        }

        private string GetUsdAmountString(decimal vndAmount)
        {
            var vndRateStr = _config["PayPal:VndRate"] ?? "26000";
            if (!decimal.TryParse(vndRateStr, out var vndRate) || vndRate <= 0)
            {
                vndRate = 26000m;
            }
            var usd = Math.Ceiling(vndAmount / vndRate * 100m) / 100m;
            return usd.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<string> GetAccessTokenAsync()
        {
            var clientId = _config["PayPal:ClientId"];
            var clientSecret = _config["PayPal:ClientSecret"];
            var baseUrl = _config["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com";

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/oauth2/token");
            var authBytes = Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}");
            var basicAuth = Convert.ToBase64String(authBytes);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var token = json?.RootElement.GetProperty("access_token").GetString();

            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("PayPal: Không lấy được access_token");
            }
            return token;
        }

        public async Task<PayPalCreateOrderResult> CreatePayPalOrderAsync(int orderId, decimal amountVnd)
        {
            var token = await GetAccessTokenAsync();
            var usd = GetUsdAmountString(amountVnd);
            var baseUrl = _config["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com";
            var returnUrl = $"{_config["PayPal:ReturnUrl"]}?order_id={orderId}";
            var cancelUrl = $"{_config["PayPal:CancelUrl"]}?order_id={orderId}&status=cancelled";

            var payload = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        reference_id = orderId.ToString(),
                        description = $"ELearnVN - Don hang #{orderId}",
                        amount = new
                        {
                            currency_code = "USD",
                            value = usd
                        }
                    }
                },
                application_context = new
                {
                    brand_name = "ELearnVN",
                    locale = "vi-VN",
                    landing_page = "NO_PREFERENCE",
                    user_action = "PAY_NOW",
                    return_url = returnUrl,
                    cancel_url = cancelUrl
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/checkout/orders");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var root = json?.RootElement ?? throw new InvalidOperationException("PayPal: Create order returned empty response");

            var paypalOrderId = root.GetProperty("id").GetString() ?? "";
            string approveUrl = "";

            if (root.TryGetProperty("links", out var linksElement) && linksElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in linksElement.EnumerateArray())
                {
                    if (link.GetProperty("rel").GetString() == "approve")
                    {
                        approveUrl = link.GetProperty("href").GetString() ?? "";
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(paypalOrderId) || string.IsNullOrEmpty(approveUrl))
            {
                throw new InvalidOperationException($"PayPal order tạo thất bại. ID: {paypalOrderId}, URL: {approveUrl}");
            }

            _logger.LogInformation("PayPal order created: {PaypalOrderId} | USD={Usd} | order_id={OrderId}", paypalOrderId, usd, orderId);

            return new PayPalCreateOrderResult
            {
                PaypalOrderId = paypalOrderId,
                ApproveUrl = approveUrl,
                UsdAmount = usd
            };
        }

        public async Task<PayPalCaptureOrderResult> CapturePayPalOrderAsync(string paypalOrderId)
        {
            var token = await GetAccessTokenAsync();
            var baseUrl = _config["PayPal:BaseUrl"] ?? "https://api-m.sandbox.paypal.com";

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/checkout/orders/{paypalOrderId}/capture");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent("", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogWarning("PayPal order {PaypalOrderId} already captured", paypalOrderId);
                return new PayPalCaptureOrderResult
                {
                    Success = true,
                    PaypalOrderId = paypalOrderId,
                    CaptureId = paypalOrderId,
                    Status = "COMPLETED",
                    UsdAmount = 0.0m
                };
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var root = json?.RootElement ?? throw new InvalidOperationException("PayPal: Capture order returned empty response");

            var status = root.GetProperty("status").GetString() ?? "";
            string captureId = "";
            decimal usdAmount = 0;

            if (root.TryGetProperty("purchase_units", out var puElement) && puElement.ValueKind == JsonValueKind.Array && puElement.GetArrayLength() > 0)
            {
                var firstPu = puElement[0];
                if (firstPu.TryGetProperty("payments", out var paymentsElement) && paymentsElement.TryGetProperty("captures", out var capturesElement) && capturesElement.ValueKind == JsonValueKind.Array && capturesElement.GetArrayLength() > 0)
                {
                    var capture = capturesElement[0];
                    captureId = capture.GetProperty("id").GetString() ?? "";
                    if (capture.TryGetProperty("amount", out var amountElement) && amountElement.TryGetProperty("value", out var valElement))
                    {
                        decimal.TryParse(valElement.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out usdAmount);
                    }
                }
            }

            _logger.LogInformation("PayPal capture: {CaptureId} | status={Status} | usd={UsdAmount}", captureId, status, usdAmount);

            return new PayPalCaptureOrderResult
            {
                Success = status == "COMPLETED",
                PaypalOrderId = paypalOrderId,
                CaptureId = captureId,
                Status = status,
                UsdAmount = usdAmount
            };
        }
    }
}
