ALTER TABLE users ADD COLUMN IF NOT EXISTS author_application_status VARCHAR(20) DEFAULT NULL;
ALTER TABLE users ADD COLUMN IF NOT EXISTS author_application_data TEXT DEFAULT NULL;
ALTER TABLE products ADD COLUMN IF NOT EXISTS rejection_reason TEXT DEFAULT NULL;

-- user_access.accessed_at (ebook refund / ORM sync). Chạy một lần; bỏ qua nếu báo duplicate column.
ALTER TABLE user_access ADD COLUMN accessed_at DATETIME DEFAULT NULL AFTER granted_at;

SHOW COLUMNS FROM users;
