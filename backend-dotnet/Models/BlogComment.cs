using System;

namespace ELearnVN.Backend.Models
{
    public class BlogComment
    {
        public int CommentId { get; set; }
        public int PostId { get; set; }
        public int UserId { get; set; }
        public string Content { get; set; } = null!;
        public string Status { get; set; } = "visible"; // visible | hidden
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public BlogPost? Post { get; set; }
        public User? Author { get; set; }
    }
}
