using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Module
    {
        public int ModuleId { get; set; }
        public int CourseId { get; set; }
        public string Title { get; set; } = null!;
        public int SortOrder { get; set; } = 0;

        // Navigation properties
        public Course? Course { get; set; }
        public ICollection<Lesson> Lessons { get; set; } = new List<Lesson>();
    }
}
