namespace ELearnVN.Backend.Models
{
    public class CartItem
    {
        public int CartItemId { get; set; }
        public int CartId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal Price { get; set; }

        // Navigation properties
        public Cart? Cart { get; set; }
        public Product? Product { get; set; }
    }
}
