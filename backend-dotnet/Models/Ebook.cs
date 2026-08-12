namespace ELearnVN.Backend.Models
{
    public class Ebook
    {
        public int ProductId { get; set; } // Primary key & Foreign key to Product
        public decimal? FileSize { get; set; } // MB
        public string? Format { get; set; } // pdf | epub
        public int? PageCount { get; set; }
        public string? MuxAssetId { get; set; }
        public string? FileKey { get; set; } // Storage key / path
        public int PreviewPages { get; set; } = 10;

        // Navigation properties
        public Product? Product { get; set; }
    }
}
