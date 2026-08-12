using System;
using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class BlogPost
    {
        public int PostId { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; } = null!;
        public string Content { get; set; } = null!;
        public string Status { get; set; } = "published"; // published | hidden
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User? Author { get; set; }
        public ICollection<BlogComment> Comments { get; set; } = new List<BlogComment>();
    }
}
