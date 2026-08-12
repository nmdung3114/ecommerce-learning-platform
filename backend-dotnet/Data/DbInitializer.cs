using System;
using System.Collections.Generic;
using System.Linq;
using ELearnVN.Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace ELearnVN.Backend.Data
{
    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context)
        {
            // Auto migrate/ensure database is created
            context.Database.EnsureCreated();

            // Run some raw SQL to modify avatar_url column (idempotent, like in Python init_data.py)
            try
            {
                context.Database.ExecuteSqlRaw("ALTER TABLE users MODIFY COLUMN avatar_url MEDIUMTEXT");
            }
            catch
            {
                // Ignore if it fails (SQLite or already done)
            }

            // 1. Categories
            if (!context.Categories.Any())
            {
                var categories = new List<Category>
                {
                    new Category { Name = "Lập trình Web", Description = "HTML, CSS, JavaScript, React, Django, FastAPI...", Icon = "💻", SortOrder = 1 },
                    new Category { Name = "Data Science & AI", Description = "Machine Learning, Deep Learning, Python, TensorFlow...", Icon = "🤖", SortOrder = 2 },
                    new Category { Name = "Mobile Development", Description = "React Native, Flutter, iOS, Android...", Icon = "📱", SortOrder = 3 },
                    new Category { Name = "UI/UX Design", Description = "Figma, Adobe XD, Prototyping, Design Systems...", Icon = "🎨", SortOrder = 4 },
                    new Category { Name = "Business & Marketing", Description = "Digital Marketing, SEO, Social Media, Sales...", Icon = "📈", SortOrder = 5 },
                    new Category { Name = "DevOps & Cloud", Description = "Docker, Kubernetes, AWS, CI/CD...", Icon = "☁️", SortOrder = 6 }
                };
                context.Categories.AddRange(categories);
                context.SaveChanges();
            }

            // 2. Users
            if (!context.Users.Any())
            {
                var users = new List<User>
                {
                    new User
                    {
                        Email = "admin@elearning.vn",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                        Name = "Quản trị viên",
                        Role = "admin",
                        Status = "active",
                        AvatarUrl = "https://api.dicebear.com/7.x/initials/svg?seed=Admin"
                    },
                    new User
                    {
                        Email = "author@elearning.vn",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("author123"),
                        Name = "Nguyễn Văn Tác Giả",
                        Role = "author",
                        Status = "active",
                        AvatarUrl = "https://api.dicebear.com/7.x/initials/svg?seed=Author"
                    },
                    new User
                    {
                        Email = "user@elearning.vn",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("user123"),
                        Name = "Trần Thị Học Viên",
                        Role = "learner",
                        Status = "active",
                        AvatarUrl = "https://api.dicebear.com/7.x/initials/svg?seed=User"
                    }
                };
                context.Users.AddRange(users);
                context.SaveChanges();
            }

            // 3. Products (Courses and Ebooks)
            if (!context.Products.Any())
            {
                var author = context.Users.FirstOrDefault(u => u.Role == "author");
                var catWeb = context.Categories.FirstOrDefault(c => c.Name == "Lập trình Web");
                var catAi = context.Categories.FirstOrDefault(c => c.Name == "Data Science & AI");
                var catMobile = context.Categories.FirstOrDefault(c => c.Name == "Mobile Development");
                var catDesign = context.Categories.FirstOrDefault(c => c.Name == "UI/UX Design");
                var catBusiness = context.Categories.FirstOrDefault(c => c.Name == "Business & Marketing");

                int? authorId = author?.UserId;

                // Course 1: Fullstack Web
                if (catWeb != null)
                {
                    var p1 = new Product
                    {
                        CategoryId = catWeb.CategoryId,
                        Name = "Fullstack Web với React & FastAPI",
                        Price = 799000m,
                        OriginalPrice = 1200000m,
                        Description = "Khóa học toàn diện xây dựng ứng dụng web với React (frontend) và FastAPI (backend). Bạn sẽ học cách tạo REST API, quản lý state, authentication, deployment và nhiều hơn nữa.",
                        ShortDescription = "Xây dựng ứng dụng web full-stack chuyên nghiệp từ A-Z",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1633356122544-f134324a6cee?w=640&q=80",
                        Status = "active",
                        ProductType = "course",
                        AuthorId = authorId,
                        TotalEnrolled = 1234,
                        AverageRating = 4.8m,
                        ReviewCount = 256
                    };
                    context.Products.Add(p1);
                    context.SaveChanges();

                    var c1 = new Course
                    {
                        ProductId = p1.ProductId,
                        Duration = 3600,
                        Level = "intermediate",
                        TotalLessons = 48,
                        Requirements = "[\"Biết HTML/CSS cơ bản\", \"Biết Python cơ bản\"]",
                        WhatYouLearn = "[\"React hooks và state management\", \"FastAPI REST API\", \"Docker deployment\", \"Database với PostgreSQL\"]"
                    };
                    context.Courses.Add(c1);
                    context.SaveChanges();

                    // Modules and Lessons
                    var m1 = new Module { CourseId = p1.ProductId, Title = "Giới thiệu & Setup môi trường", SortOrder = 0 };
                    var m2 = new Module { CourseId = p1.ProductId, Title = "React Frontend Cơ Bản", SortOrder = 1 };
                    var m3 = new Module { CourseId = p1.ProductId, Title = "FastAPI Backend", SortOrder = 2 };
                    context.Modules.AddRange(m1, m2, m3);
                    context.SaveChanges();

                    context.Lessons.AddRange(
                        new Lesson { ModuleId = m1.ModuleId, Title = "Giới thiệu khóa học", Duration = 300, SortOrder = 0, IsPreview = true },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Cài đặt Node.js và Python", Duration = 600, SortOrder = 1, IsPreview = true },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Tổng quan kiến trúc", Duration = 450, SortOrder = 2, IsPreview = false },
                        
                        new Lesson { ModuleId = m2.ModuleId, Title = "Components và Props", Duration = 900, SortOrder = 0, IsPreview = false },
                        new Lesson { ModuleId = m2.ModuleId, Title = "State và Event Handling", Duration = 1200, SortOrder = 1, IsPreview = false },
                        new Lesson { ModuleId = m2.ModuleId, Title = "React Hooks (useState, useEffect)", Duration = 1500, SortOrder = 2, IsPreview = false },
                        new Lesson { ModuleId = m2.ModuleId, Title = "React Router", Duration = 900, SortOrder = 3, IsPreview = false },

                        new Lesson { ModuleId = m3.ModuleId, Title = "FastAPI cơ bản", Duration = 1200, SortOrder = 0, IsPreview = false },
                        new Lesson { ModuleId = m3.ModuleId, Title = "SQLAlchemy ORM", Duration = 1500, SortOrder = 1, IsPreview = false },
                        new Lesson { ModuleId = m3.ModuleId, Title = "Authentication với JWT", Duration = 1800, SortOrder = 2, IsPreview = false }
                    );
                    context.SaveChanges();
                }

                // Course 2: Machine Learning
                if (catAi != null)
                {
                    var p2 = new Product
                    {
                        CategoryId = catAi.CategoryId,
                        Name = "Machine Learning với Python từ Zero",
                        Price = 599000m,
                        OriginalPrice = 999000m,
                        Description = "Khóa học Machine Learning toàn diện từ cơ bản đến nâng cao. Học Numpy, Pandas, Scikit-learn, TensorFlow và deploy model lên production.",
                        ShortDescription = "Học ML/AI thực chiến với Python và TensorFlow",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1555949963-ff9fe0c870eb?w=640&q=80",
                        Status = "active",
                        ProductType = "course",
                        AuthorId = authorId,
                        TotalEnrolled = 2156,
                        AverageRating = 4.9m,
                        ReviewCount = 412
                    };
                    context.Products.Add(p2);
                    context.SaveChanges();

                    var c2 = new Course
                    {
                        ProductId = p2.ProductId,
                        Duration = 5400,
                        Level = "beginner",
                        TotalLessons = 72,
                        Requirements = "[\"Biết Python cơ bản\", \"Toán học cơ bản\"]",
                        WhatYouLearn = "[\"Numpy và Pandas\", \"Supervised Learning\", \"Neural Networks\", \"Model deployment\"]"
                    };
                    context.Courses.Add(c2);
                    context.SaveChanges();

                    var m1 = new Module { CourseId = p2.ProductId, Title = "Python cho Data Science", SortOrder = 0 };
                    var m2 = new Module { CourseId = p2.ProductId, Title = "Machine Learning Cơ Bản", SortOrder = 1 };
                    context.Modules.AddRange(m1, m2);
                    context.SaveChanges();

                    context.Lessons.AddRange(
                        new Lesson { ModuleId = m1.ModuleId, Title = "Numpy cơ bản", Duration = 1200, SortOrder = 0, IsPreview = true },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Pandas DataFrame", Duration = 1500, SortOrder = 1, IsPreview = false },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Matplotlib và Seaborn", Duration = 900, SortOrder = 2, IsPreview = false },

                        new Lesson { ModuleId = m2.ModuleId, Title = "Linear Regression", Duration = 1800, SortOrder = 0, IsPreview = false },
                        new Lesson { ModuleId = m2.ModuleId, Title = "Classification với Logistic Regression", Duration = 1500, SortOrder = 1, IsPreview = false },
                        new Lesson { ModuleId = m2.ModuleId, Title = "Decision Trees và Random Forest", Duration = 1800, SortOrder = 2, IsPreview = false }
                    );
                    context.SaveChanges();
                }

                // Course 3: Flutter
                if (catMobile != null)
                {
                    var p3 = new Product
                    {
                        CategoryId = catMobile.CategoryId,
                        Name = "Flutter App Development",
                        Price = 699000m,
                        OriginalPrice = 1100000m,
                        Description = "Build cross-platform mobile apps với Flutter và Dart. Từ UI components đến state management với BLoC, deploy lên App Store và Google Play.",
                        ShortDescription = "Tạo app iOS & Android với một codebase duy nhất",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1512941937669-90a1b58e7e9c?w=640&q=80",
                        Status = "active",
                        ProductType = "course",
                        AuthorId = authorId,
                        TotalEnrolled = 867,
                        AverageRating = 4.7m,
                        ReviewCount = 134
                    };
                    context.Products.Add(p3);
                    context.SaveChanges();

                    var c3 = new Course
                    {
                        ProductId = p3.ProductId,
                        Duration = 4200,
                        Level = "intermediate",
                        TotalLessons = 56,
                        Requirements = "[\"Biết lập trình OOP\", \"Kiến thức mobile app cơ bản\"]",
                        WhatYouLearn = "[\"Dart programming\", \"Flutter widgets\", \"BLoC state management\", \"Firebase integration\"]"
                    };
                    context.Courses.Add(c3);
                    context.SaveChanges();

                    var m1 = new Module { CourseId = p3.ProductId, Title = "Flutter & Dart Basics", SortOrder = 0 };
                    context.Modules.Add(m1);
                    context.SaveChanges();

                    context.Lessons.AddRange(
                        new Lesson { ModuleId = m1.ModuleId, Title = "Giới thiệu Flutter", Duration = 600, SortOrder = 0, IsPreview = true },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Dart language essentials", Duration = 1800, SortOrder = 1, IsPreview = false },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Widget tree và layout", Duration = 1500, SortOrder = 2, IsPreview = false }
                    );
                    context.SaveChanges();
                }

                // Course 4: Figma Design
                if (catDesign != null)
                {
                    var p4 = new Product
                    {
                        CategoryId = catDesign.CategoryId,
                        Name = "UI/UX Design Masterclass với Figma",
                        Price = 499000m,
                        OriginalPrice = 800000m,
                        Description = "Học thiết kế UI/UX chuyên nghiệp với Figma. Từ wireframe, prototype đến design system hoàn chỉnh. Xây dựng portfolio thu hút nhà tuyển dụng.",
                        ShortDescription = "Thiết kế UI/UX pro với Figma từ A-Z",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1561070791-2526d30994b5?w=640&q=80",
                        Status = "active",
                        ProductType = "course",
                        AuthorId = authorId,
                        TotalEnrolled = 543,
                        AverageRating = 4.6m,
                        ReviewCount = 89
                    };
                    context.Products.Add(p4);
                    context.SaveChanges();

                    var c4 = new Course
                    {
                        ProductId = p4.ProductId,
                        Duration = 2700,
                        Level = "beginner",
                        TotalLessons = 36,
                        Requirements = "[\"Không cần kinh nghiệm trước\"]",
                        WhatYouLearn = "[\"UI principles\", \"Figma advanced\", \"Prototyping\", \"Design systems\"]"
                    };
                    context.Courses.Add(c4);
                    context.SaveChanges();

                    var m1 = new Module { CourseId = p4.ProductId, Title = "Design Fundamentals", SortOrder = 0 };
                    context.Modules.Add(m1);
                    context.SaveChanges();

                    context.Lessons.AddRange(
                        new Lesson { ModuleId = m1.ModuleId, Title = "Nguyên tắc thiết kế UI cơ bản", Duration = 900, SortOrder = 0, IsPreview = true },
                        new Lesson { ModuleId = m1.ModuleId, Title = "Color theory và Typography", Duration = 1200, SortOrder = 1, IsPreview = false }
                    );
                    context.SaveChanges();
                }

                // Ebook 1
                if (catWeb != null)
                {
                    var e1 = new Product
                    {
                        CategoryId = catWeb.CategoryId,
                        Name = "Clean Code - Nghệ thuật viết code sạch",
                        Price = 149000m,
                        OriginalPrice = 250000m,
                        Description = "Bản dịch và chú giải cuốn sách nổi tiếng Clean Code của Robert C. Martin. Học cách viết code dễ đọc, dễ maintain và dễ test.",
                        ShortDescription = "Học cách viết code sạch, rõ ràng và dễ bảo trì",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1532012197267-da84d127e765?w=640&q=80",
                        Status = "active",
                        ProductType = "ebook",
                        AuthorId = authorId,
                        TotalEnrolled = 3421,
                        AverageRating = 4.9m,
                        ReviewCount = 678
                    };
                    context.Products.Add(e1);
                    context.SaveChanges();

                    context.Ebooks.Add(new Ebook
                    {
                        ProductId = e1.ProductId,
                        FileSize = 8.5m,
                        Format = "pdf",
                        PageCount = 464,
                        PreviewPages = 20
                    });
                    context.SaveChanges();
                }

                // Ebook 2
                if (catAi != null)
                {
                    var e2 = new Product
                    {
                        CategoryId = catAi.CategoryId,
                        Name = "Deep Learning với Python - Hướng dẫn thực chiến",
                        Price = 199000m,
                        OriginalPrice = 350000m,
                        Description = "Ebook toàn diện về Deep Learning với Python và Keras/TensorFlow. Từ cơ bản đến các kiến trúc CNN, RNN, Transformer.",
                        ShortDescription = "Hướng dẫn thực chiến Deep Learning với Python",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1620712943543-bcc4688e7485?w=640&q=80",
                        Status = "active",
                        ProductType = "ebook",
                        AuthorId = authorId,
                        TotalEnrolled = 1876,
                        AverageRating = 4.7m,
                        ReviewCount = 234
                    };
                    context.Products.Add(e2);
                    context.SaveChanges();

                    context.Ebooks.Add(new Ebook
                    {
                        ProductId = e2.ProductId,
                        FileSize = 12.3m,
                        Format = "pdf",
                        PageCount = 512,
                        PreviewPages = 15
                    });
                    context.SaveChanges();
                }

                // Ebook 3
                if (catBusiness != null)
                {
                    var e3 = new Product
                    {
                        CategoryId = catBusiness.CategoryId,
                        Name = "Digital Marketing 2024 - Chiến lược & Thực thi",
                        Price = 129000m,
                        OriginalPrice = 200000m,
                        Description = "Cẩm nang Digital Marketing đầy đủ nhất 2024. Bao gồm SEO, Google Ads, Facebook Ads, Email Marketing, Content Strategy.",
                        ShortDescription = "Cẩm nang Digital Marketing toàn diện 2024",
                        ThumbnailUrl = "https://images.unsplash.com/photo-1432888498266-38ffec3eaf0a?w=640&q=80",
                        Status = "active",
                        ProductType = "ebook",
                        AuthorId = authorId,
                        TotalEnrolled = 987,
                        AverageRating = 4.5m,
                        ReviewCount = 156
                    };
                    context.Products.Add(e3);
                    context.SaveChanges();

                    context.Ebooks.Add(new Ebook
                    {
                        ProductId = e3.ProductId,
                        FileSize = 5.2m,
                        Format = "pdf",
                        PageCount = 280,
                        PreviewPages = 12
                    });
                    context.SaveChanges();
                }
            }

            // 4. Coupons
            if (!context.Coupons.Any())
            {
                var coupons = new List<Coupon>
                {
                    new Coupon { Code = "WELCOME50", Discount = 50000m, DiscountType = "fixed", MinOrderAmount = 200000m, UsageLimit = 100, IsActive = true },
                    new Coupon { Code = "SALE20", Discount = 20m, DiscountType = "percent", MinOrderAmount = 500000m, UsageLimit = 50, IsActive = true },
                    new Coupon { Code = "NEWUSER", Discount = 100000m, DiscountType = "fixed", MinOrderAmount = 300000m, UsageLimit = 1000, IsActive = true }
                };
                context.Coupons.AddRange(coupons);
                context.SaveChanges();
            }
        }
    }
}
