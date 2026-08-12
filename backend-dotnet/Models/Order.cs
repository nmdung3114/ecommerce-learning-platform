using System;
using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Order
    {
        public int OrderId { get; set; }
        public int UserId { get; set; }
        public string? CouponCode { get; set; }
        public decimal Subtotal { get; set; }
        public decimal DiscountAmount { get; set; } = 0;
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "pending"; // pending | paid | refunded | cancelled
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User? User { get; set; }
        public Coupon? Coupon { get; set; }
        public Payment? Payment { get; set; }
        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
        public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
    }
}
