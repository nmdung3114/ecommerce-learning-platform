using System;

namespace ELearnVN.Backend.Models
{
    public class LearningProgress
    {
        public int ProgressId { get; set; }
        public int UserId { get; set; }
        public int LessonId { get; set; }
        public bool Completed { get; set; } = false;
        public int WatchedSeconds { get; set; } = 0;
        public DateTime? CompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public User? User { get; set; }
        public Lesson? Lesson { get; set; }
    }
}
