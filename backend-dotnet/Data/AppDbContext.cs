using Microsoft.EntityFrameworkCore;
using ELearnVN.Backend.Models;
using System.Text.RegularExpressions;

namespace ELearnVN.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Ebook> Ebooks { get; set; } = null!;
        public DbSet<Course> Courses { get; set; } = null!;
        public DbSet<Module> Modules { get; set; } = null!;
        public DbSet<Lesson> Lessons { get; set; } = null!;
        public DbSet<Review> Reviews { get; set; } = null!;
        public DbSet<Cart> Carts { get; set; } = null!;
        public DbSet<CartItem> CartItems { get; set; } = null!;
        public DbSet<Coupon> Coupons { get; set; } = null!;
        public DbSet<Order> Orders { get; set; } = null!;
        public DbSet<OrderItem> OrderItems { get; set; } = null!;
        public DbSet<Payment> Payments { get; set; } = null!;
        public DbSet<UserAccess> UserAccesses { get; set; } = null!;
        public DbSet<LearningProgress> LearningProgresses { get; set; } = null!;
        public DbSet<BlogPost> BlogPosts { get; set; } = null!;
        public DbSet<BlogComment> BlogComments { get; set; } = null!;
        public DbSet<Wishlist> Wishlists { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure snake_case naming convention
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                // Set Table Name
                var clrType = entity.ClrType;
                var tableName = clrType.Name;

                // Explicit table renaming to match database/init.sql plurals/snake_case
                if (tableName == "User") entity.SetTableName("users");
                else if (tableName == "Category") entity.SetTableName("categories");
                else if (tableName == "Product") entity.SetTableName("products");
                else if (tableName == "Ebook") entity.SetTableName("ebooks");
                else if (tableName == "Course") entity.SetTableName("courses");
                else if (tableName == "Module") entity.SetTableName("modules");
                else if (tableName == "Lesson") entity.SetTableName("lessons");
                else if (tableName == "Review") entity.SetTableName("reviews");
                else if (tableName == "Cart") entity.SetTableName("carts");
                else if (tableName == "CartItem") entity.SetTableName("cart_items");
                else if (tableName == "Coupon") entity.SetTableName("coupons");
                else if (tableName == "Order") entity.SetTableName("orders");
                else if (tableName == "OrderItem") entity.SetTableName("order_items");
                else if (tableName == "Payment") entity.SetTableName("payments");
                else if (tableName == "UserAccess") entity.SetTableName("user_access");
                else if (tableName == "LearningProgress") entity.SetTableName("learning_progress");
                else if (tableName == "BlogPost") entity.SetTableName("blog_posts");
                else if (tableName == "BlogComment") entity.SetTableName("blog_comments");
                else if (tableName == "Wishlist") entity.SetTableName("wishlists");
                else if (tableName == "Notification") entity.SetTableName("notifications");

                // Convert Column Names
                foreach (var property in entity.GetProperties())
                {
                    property.SetColumnName(ToSnakeCase(property.Name));
                }
            }

            // --- Custom Entity Mappings & Relationships ---

            // User
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserId);
                entity.HasIndex(e => e.Email).IsUnique();
            });

            // Category
            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(e => e.CategoryId);
                entity.HasIndex(e => e.Name).IsUnique();
            });

            // Product
            modelBuilder.Entity<Product>(entity =>
            {
                entity.HasKey(e => e.ProductId);
                
                entity.HasOne(d => d.Category)
                    .WithMany(p => p.Products)
                    .HasForeignKey(d => d.CategoryId)
                    .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(d => d.Author)
                    .WithMany(p => p.Products)
                    .HasForeignKey(d => d.AuthorId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Ebook: One-to-one with Product sharing ProductId as PK/FK
            modelBuilder.Entity<Ebook>(entity =>
            {
                entity.HasKey(e => e.ProductId);
                entity.Property(e => e.ProductId).ValueGeneratedNever();

                entity.HasOne(d => d.Product)
                    .WithOne(p => p.Ebook)
                    .HasForeignKey<Ebook>(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Course: One-to-one with Product sharing ProductId as PK/FK
            modelBuilder.Entity<Course>(entity =>
            {
                entity.HasKey(e => e.ProductId);
                entity.Property(e => e.ProductId).ValueGeneratedNever();

                entity.HasOne(d => d.Product)
                    .WithOne(p => p.Course)
                    .HasForeignKey<Course>(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Module
            modelBuilder.Entity<Module>(entity =>
            {
                entity.HasKey(e => e.ModuleId);
                entity.HasOne(d => d.Course)
                    .WithMany(p => p.Modules)
                    .HasForeignKey(d => d.CourseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Lesson
            modelBuilder.Entity<Lesson>(entity =>
            {
                entity.HasKey(e => e.LessonId);
                entity.HasOne(d => d.Module)
                    .WithMany(p => p.Lessons)
                    .HasForeignKey(d => d.ModuleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Review
            modelBuilder.Entity<Review>(entity =>
            {
                entity.HasKey(e => e.ReviewId);
                entity.HasIndex(e => new { e.ProductId, e.UserId }).IsUnique();

                entity.HasOne(d => d.Product)
                    .WithMany(p => p.Reviews)
                    .HasForeignKey(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.User)
                    .WithMany(p => p.Reviews)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Cart (One-to-one with User)
            modelBuilder.Entity<Cart>(entity =>
            {
                entity.HasKey(e => e.CartId);
                entity.HasIndex(e => e.UserId).IsUnique();

                entity.HasOne(d => d.User)
                    .WithOne(p => p.Cart)
                    .HasForeignKey<Cart>(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // CartItem
            modelBuilder.Entity<CartItem>(entity =>
            {
                entity.HasKey(e => e.CartItemId);
                entity.HasIndex(e => new { e.CartId, e.ProductId }).IsUnique();

                entity.HasOne(d => d.Cart)
                    .WithMany(p => p.Items)
                    .HasForeignKey(d => d.CartId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Product)
                    .WithMany(p => p.CartItems)
                    .HasForeignKey(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Coupon
            modelBuilder.Entity<Coupon>(entity =>
            {
                entity.HasKey(e => e.Code);
            });

            // Order
            modelBuilder.Entity<Order>(entity =>
            {
                entity.HasKey(e => e.OrderId);
                
                entity.HasOne(d => d.User)
                    .WithMany(p => p.Orders)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(d => d.Coupon)
                    .WithMany(p => p.Orders)
                    .HasForeignKey(d => d.CouponCode)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // OrderItem
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.HasKey(e => e.OrderItemId);
                
                entity.HasOne(d => d.Order)
                    .WithMany(p => p.Items)
                    .HasForeignKey(d => d.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Product)
                    .WithMany(p => p.OrderItems)
                    .HasForeignKey(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // Payment
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.HasKey(e => e.PaymentId);
                entity.HasIndex(e => e.OrderId).IsUnique();
                entity.HasIndex(e => e.TransactionId).IsUnique();

                entity.HasOne(d => d.Order)
                    .WithOne(p => p.Payment)
                    .HasForeignKey<Payment>(d => d.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Map JSON response to longtext/JSON column
                entity.Property(e => e.VnpayResponse).HasColumnType("json");
            });

            // UserAccess
            modelBuilder.Entity<UserAccess>(entity =>
            {
                entity.HasKey(e => e.AccessId);
                entity.HasIndex(e => new { e.UserId, e.ProductId }).IsUnique();

                entity.HasOne(d => d.User)
                    .WithMany(p => p.UserAccesses)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Product)
                    .WithMany(p => p.UserAccesses)
                    .HasForeignKey(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Order)
                    .WithMany(p => p.UserAccesses)
                    .HasForeignKey(d => d.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // LearningProgress
            modelBuilder.Entity<LearningProgress>(entity =>
            {
                entity.HasKey(e => e.ProgressId);
                entity.HasIndex(e => new { e.UserId, e.LessonId }).IsUnique();

                entity.HasOne(d => d.User)
                    .WithMany(p => p.LearningProgresses)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Lesson)
                    .WithMany(p => p.LearningProgresses)
                    .HasForeignKey(d => d.LessonId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // BlogPost
            modelBuilder.Entity<BlogPost>(entity =>
            {
                entity.HasKey(e => e.PostId);
                
                entity.HasOne(d => d.Author)
                    .WithMany(p => p.BlogPosts)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // BlogComment
            modelBuilder.Entity<BlogComment>(entity =>
            {
                entity.HasKey(e => e.CommentId);

                entity.HasOne(d => d.Post)
                    .WithMany(p => p.Comments)
                    .HasForeignKey(d => d.PostId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Author)
                    .WithMany(p => p.BlogComments)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Wishlist
            modelBuilder.Entity<Wishlist>(entity =>
            {
                entity.HasKey(e => e.WishlistId);
                entity.HasIndex(e => new { e.UserId, e.ProductId }).IsUnique();

                entity.HasOne(d => d.User)
                    .WithMany(p => p.Wishlists)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Product)
                    .WithMany(p => p.Wishlists)
                    .HasForeignKey(d => d.ProductId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Notification
            modelBuilder.Entity<Notification>(entity =>
            {
                entity.HasKey(e => e.NotificationId);

                entity.HasOne(d => d.User)
                    .WithMany(p => p.Notifications)
                    .HasForeignKey(d => d.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }

        private static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            // Special handling for acronyms and ID
            if (input == "Id") return "id";
            
            var snake = Regex.Replace(input, @"(?<!^)(?=[A-Z][a-z])", "_");
            snake = Regex.Replace(snake, @"(?<!^)(?=[A-Z][A-Z][A-Z])", "_");
            snake = Regex.Replace(snake, @"(?<!^)(?=[A-Z][A-Z])", "_");
            
            return snake.Replace("__", "_").ToLower();
        }
    }
}
