using System.Collections.Generic;

namespace ELearnVN.Backend.Models
{
    public class Category
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? Icon { get; set; }
        public int SortOrder { get; set; } = 0;

        // Navigation properties
        public ICollection<Product> Products { get; set; } = new List<Product>();
    }
}
