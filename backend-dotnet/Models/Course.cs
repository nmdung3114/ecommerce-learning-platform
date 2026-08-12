using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Course
    {
        public int ProductId { get; set; } // Primary key & Foreign key to Product
        public int Duration { get; set; } = 0; // total minutes
        public string? Level { get; set; } // beginner | intermediate | advanced
        public int TotalLessons { get; set; } = 0;
        public string? Requirements { get; set; } // JSON array string
        public string? WhatYouLearn { get; set; } // JSON array string

        // Navigation properties
        public Product? Product { get; set; }
        public ICollection<Module> Modules { get; set; } = new List<Module>();
    }
}
