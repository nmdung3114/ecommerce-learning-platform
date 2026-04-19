-- ============================================================
-- E-Commerce Learning Platform - Database Schema
-- ============================================================
CREATE DATABASE IF NOT EXISTS elearning CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE elearning;

-- Users
CREATE TABLE IF NOT EXISTS users (
    user_id INT PRIMARY KEY AUTO_INCREMENT,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255),
    name VARCHAR(100) NOT NULL,
    phone VARCHAR(20),
    role VARCHAR(20) NOT NULL DEFAULT 'learner',   -- learner | admin | author
    status VARCHAR(20) NOT NULL DEFAULT 'active',  -- active | suspended
    author_application_status VARCHAR(20),         -- null | pending | rejected
    avatar_url MEDIUMTEXT,
    oauth_provider VARCHAR(50),                    -- google | facebook | null
    oauth_id VARCHAR(255),
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    INDEX idx_email (email),
    INDEX idx_role (role)
) ENGINE=InnoDB;

-- Categories
CREATE TABLE IF NOT EXISTS categories (
    category_id INT PRIMARY KEY AUTO_INCREMENT,
    name VARCHAR(100) NOT NULL UNIQUE,
    description TEXT,
    icon VARCHAR(100),
    sort_order INT DEFAULT 0
) ENGINE=InnoDB;

-- Products (base table for both course and ebook)
CREATE TABLE IF NOT EXISTS products (
    product_id INT PRIMARY KEY AUTO_INCREMENT,
    category_id INT,
    name VARCHAR(255) NOT NULL,
    price DECIMAL(12,2) NOT NULL,
    original_price DECIMAL(12,2),
    description TEXT,
    short_description VARCHAR(500),
    thumbnail_url VARCHAR(500),
    status VARCHAR(20) NOT NULL DEFAULT 'active',  -- active | draft | archived
    product_type VARCHAR(20) NOT NULL,              -- course | ebook
    author_id INT,
    total_enrolled INT DEFAULT 0,
    average_rating DECIMAL(3,2) DEFAULT 0,
    review_count INT DEFAULT 0,
    rejection_reason TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (category_id) REFERENCES categories(category_id) ON DELETE SET NULL,
    FOREIGN KEY (author_id) REFERENCES users(user_id) ON DELETE SET NULL,
    INDEX idx_type (product_type),
    INDEX idx_status (status),
    INDEX idx_category (category_id)
) ENGINE=InnoDB;

-- Ebooks (extends products)
CREATE TABLE IF NOT EXISTS ebooks (
    product_id INT PRIMARY KEY,
    file_size DECIMAL(10,2),         -- MB
    format VARCHAR(20),              -- pdf | epub
    page_count INT,
    mux_asset_id VARCHAR(255),       -- Mux asset ID (for PDF preview via Mux)
    file_key VARCHAR(500),           -- Storage key / path
    preview_pages INT DEFAULT 10,   -- số trang preview miễn phí
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- Courses (extends products)
CREATE TABLE IF NOT EXISTS courses (
    product_id INT PRIMARY KEY,
    duration INT DEFAULT 0,          -- total minutes
    level VARCHAR(50),               -- beginner | intermediate | advanced
    total_lessons INT DEFAULT 0,
    requirements TEXT,               -- JSON array
    what_you_learn TEXT,             -- JSON array
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- Course Modules
CREATE TABLE IF NOT EXISTS modules (
    module_id INT PRIMARY KEY AUTO_INCREMENT,
    course_id INT NOT NULL,
    title VARCHAR(255) NOT NULL,
    sort_order INT DEFAULT 0,
    FOREIGN KEY (course_id) REFERENCES courses(product_id) ON DELETE CASCADE,
    INDEX idx_course (course_id)
) ENGINE=InnoDB;

-- Lessons
CREATE TABLE IF NOT EXISTS lessons (
    lesson_id INT PRIMARY KEY AUTO_INCREMENT,
    module_id INT NOT NULL,
    title VARCHAR(255) NOT NULL,
    mux_asset_id VARCHAR(255),       -- Mux asset ID
    mux_playback_id VARCHAR(255),    -- Mux playback ID (public or signed)
    duration INT DEFAULT 0,          -- seconds
    sort_order INT DEFAULT 0,
    is_preview BOOLEAN DEFAULT FALSE,
    FOREIGN KEY (module_id) REFERENCES modules(module_id) ON DELETE CASCADE,
    INDEX idx_module (module_id)
) ENGINE=InnoDB;

-- Reviews
CREATE TABLE IF NOT EXISTS reviews (
    review_id INT PRIMARY KEY AUTO_INCREMENT,
    product_id INT NOT NULL,
    user_id INT NOT NULL,
    rating INT NOT NULL,
    comment TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE KEY unique_review (product_id, user_id),
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    CONSTRAINT chk_rating CHECK (rating BETWEEN 1 AND 5)
) ENGINE=InnoDB;

-- Carts
CREATE TABLE IF NOT EXISTS carts (
    cart_id INT PRIMARY KEY AUTO_INCREMENT,
    user_id INT UNIQUE NOT NULL,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- Cart Items
CREATE TABLE IF NOT EXISTS cart_items (
    cart_item_id INT PRIMARY KEY AUTO_INCREMENT,
    cart_id INT NOT NULL,
    product_id INT NOT NULL,
    quantity INT NOT NULL DEFAULT 1,
    price DECIMAL(12,2) NOT NULL,
    UNIQUE KEY unique_cart_product (cart_id, product_id),
    FOREIGN KEY (cart_id) REFERENCES carts(cart_id) ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- Coupons
CREATE TABLE IF NOT EXISTS coupons (
    code VARCHAR(50) PRIMARY KEY,
    discount DECIMAL(12,2) NOT NULL,
    discount_type VARCHAR(20) DEFAULT 'fixed',  -- fixed | percent
    min_order_amount DECIMAL(12,2) DEFAULT 0,
    expired_date DATETIME,
    usage_limit INT,
    used_count INT DEFAULT 0,
    is_active BOOLEAN DEFAULT TRUE
) ENGINE=InnoDB;

-- Orders
CREATE TABLE IF NOT EXISTS orders (
    order_id INT PRIMARY KEY AUTO_INCREMENT,
    user_id INT NOT NULL,
    coupon_code VARCHAR(50),
    subtotal DECIMAL(12,2) NOT NULL,
    discount_amount DECIMAL(12,2) DEFAULT 0,
    total_amount DECIMAL(12,2) NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | paid | refunded | cancelled
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(user_id),
    FOREIGN KEY (coupon_code) REFERENCES coupons(code) ON DELETE SET NULL,
    INDEX idx_user (user_id),
    INDEX idx_status (status)
) ENGINE=InnoDB;

-- Order Items
CREATE TABLE IF NOT EXISTS order_items (
    order_item_id INT PRIMARY KEY AUTO_INCREMENT,
    order_id INT NOT NULL,
    product_id INT NOT NULL,
    quantity INT NOT NULL DEFAULT 1,
    price DECIMAL(12,2) NOT NULL,
    FOREIGN KEY (order_id) REFERENCES orders(order_id) ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(product_id)
) ENGINE=InnoDB;

-- Payments
CREATE TABLE IF NOT EXISTS payments (
    payment_id INT PRIMARY KEY AUTO_INCREMENT,
    order_id INT UNIQUE NOT NULL,
    method VARCHAR(50) NOT NULL DEFAULT 'vnpay',
    status VARCHAR(20) NOT NULL DEFAULT 'pending',  -- pending | success | failed
    transaction_id VARCHAR(255) UNIQUE,
    vnpay_txn_ref VARCHAR(100),
    paid_at DATETIME,
    amount DECIMAL(12,2),
    vnpay_response JSON,
    FOREIGN KEY (order_id) REFERENCES orders(order_id)
) ENGINE=InnoDB;

-- User Access (content rights after purchase)
CREATE TABLE IF NOT EXISTS user_access (
    access_id INT PRIMARY KEY AUTO_INCREMENT,
    user_id INT NOT NULL,
    product_id INT NOT NULL,
    order_id INT NOT NULL,
    granted_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    accessed_at DATETIME DEFAULT NULL,
    revoked_at DATETIME,
    is_active BOOLEAN DEFAULT TRUE,
    UNIQUE KEY unique_access (user_id, product_id),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    FOREIGN KEY (product_id) REFERENCES products(product_id) ON DELETE CASCADE,
    FOREIGN KEY (order_id) REFERENCES orders(order_id),
    INDEX idx_user_product (user_id, product_id)
) ENGINE=InnoDB;

-- Learning Progress
CREATE TABLE IF NOT EXISTS learning_progress (
    progress_id INT PRIMARY KEY AUTO_INCREMENT,
    user_id INT NOT NULL,
    lesson_id INT NOT NULL,
    completed BOOLEAN DEFAULT FALSE,
    watched_seconds INT DEFAULT 0,
    completed_at DATETIME,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    UNIQUE KEY unique_progress (user_id, lesson_id),
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    FOREIGN KEY (lesson_id) REFERENCES lessons(lesson_id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- Blog Posts
CREATE TABLE IF NOT EXISTS blog_posts (
    post_id INT PRIMARY KEY AUTO_INCREMENT,
    user_id INT NOT NULL,
    title VARCHAR(300) NOT NULL,
    content TEXT NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'published',  -- published | hidden
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    INDEX idx_blog_status (status),
    INDEX idx_blog_user (user_id),
    INDEX idx_blog_created (created_at)
) ENGINE=InnoDB;

-- Blog Comments
CREATE TABLE IF NOT EXISTS blog_comments (
    comment_id INT PRIMARY KEY AUTO_INCREMENT,
    post_id INT NOT NULL,
    user_id INT NOT NULL,
    content TEXT NOT NULL,
    status VARCHAR(20) NOT NULL DEFAULT 'visible',    -- visible | hidden
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (post_id) REFERENCES blog_posts(post_id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES users(user_id) ON DELETE CASCADE,
    INDEX idx_comment_post (post_id),
    INDEX idx_comment_status (status)
) ENGINE=InnoDB;

