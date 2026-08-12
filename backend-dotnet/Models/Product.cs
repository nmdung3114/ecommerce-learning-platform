using System;
using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Product
    {
        public int ProductId { get; set; }
        public int? CategoryId { get; set; }
        public string Name { get; set; } = null!;
        public decimal Price { get; set; }
        public decimal? OriginalPrice { get; set; }
        public string? Description { get; set; }
        public string? ShortDescription { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string Status { get; set; } = "active"; // active | draft | archived
        public string ProductType { get; set; } = null!; // course | ebook
        public int? AuthorId { get; set; }
        public int TotalEnrolled { get; set; } = 0;
        public decimal AverageRating { get; set; } = 0;
        public int ReviewCount { get; set; } = 0;
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Category? Category { get; set; }
        public User? Author { get; set; }
        public Ebook? Ebook { get; set; }
        public Course? Course { get; set; }
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
        public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
        public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();
    }
}
