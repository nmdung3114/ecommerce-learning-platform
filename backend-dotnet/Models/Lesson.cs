using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Lesson
    {
        public int LessonId { get; set; }
        public int ModuleId { get; set; }
        public string Title { get; set; } = null!;
        public string? MuxAssetId { get; set; }
        public string? MuxPlaybackId { get; set; }
        public int Duration { get; set; } = 0; // seconds
        public int SortOrder { get; set; } = 0;
        public bool IsPreview { get; set; } = false;

        // Navigation properties
        public Module? Module { get; set; }
        public ICollection<LearningProgress> LearningProgresses { get; set; } = new List<LearningProgress>();
    }
}
