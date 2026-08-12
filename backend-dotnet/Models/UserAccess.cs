using System;

namespace ELearnVN.Backend.Models
{
    public class UserAccess
    {
        public int AccessId { get; set; }
        public int UserId { get; set; }
        public int ProductId { get; set; }
        public int OrderId { get; set; }
        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AccessedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public bool IsActive { get; set; } = true;

        // Navigation properties
        public User? User { get; set; }
        public Product? Product { get; set; }
        public Order? Order { get; set; }
    }
}
