using System;

namespace ELearnVN.Backend.Models
{
    public class Payment
    {
        public int PaymentId { get; set; }
        public int OrderId { get; set; }
        public string Method { get; set; } = "vnpay"; // vnpay | paypal
        public string Status { get; set; } = "pending"; // pending | success | failed
        public string? TransactionId { get; set; }
        public string? VnpayTxnRef { get; set; }
        public DateTime? PaidAt { get; set; }
        public decimal? Amount { get; set; }
        public string? VnpayResponse { get; set; } // Map as a JSON string

        // Navigation properties
        public Order? Order { get; set; }
    }
}
