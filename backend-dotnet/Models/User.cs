using System;
using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Email { get; set; } = null!;
        public string? PasswordHash { get; set; }
        public string Name { get; set; } = null!;
        public string? Phone { get; set; }
        public string Role { get; set; } = "learner"; // learner | admin | author
        public string Status { get; set; } = "active"; // active | suspended
        public string? AuthorApplicationStatus { get; set; } // null | pending | rejected | approved
        public string? AuthorApplicationData { get; set; } // JSON or text
        public string? AvatarUrl { get; set; }
        public string? OauthProvider { get; set; } // google | facebook | null
        public string? OauthId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public Cart? Cart { get; set; }
        public ICollection<Product> Products { get; set; } = new List<Product>();
        public ICollection<Review> Reviews { get; set; } = new List<Review>();
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<UserAccess> UserAccesses { get; set; } = new List<UserAccess>();
        public ICollection<LearningProgress> LearningProgresses { get; set; } = new List<LearningProgress>();
        public ICollection<BlogPost> BlogPosts { get; set; } = new List<BlogPost>();
        public ICollection<BlogComment> BlogComments { get; set; } = new List<BlogComment>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
        public ICollection<Wishlist> Wishlists { get; set; } = new List<Wishlist>();
    }
}
