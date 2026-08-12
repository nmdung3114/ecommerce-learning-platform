using System;
using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Coupon
    {
        public string Code { get; set; } = null!;
        public decimal Discount { get; set; }
        public string DiscountType { get; set; } = "fixed"; // fixed | percent
        public decimal MinOrderAmount { get; set; } = 0;
        public DateTime? ExpiredDate { get; set; }
        public int? UsageLimit { get; set; }
        public int UsedCount { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        // Navigation properties
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }
}
