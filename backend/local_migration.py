import pymysql

with open('/app/migration_output.txt', 'w') as log:
    try:
        conn = pymysql.connect(
            host='mysql',
            user='root',
            password='root123',
            database='elearning',
            port=3306
        )
        with conn.cursor() as cur:
            cur.execute("SHOW COLUMNS FROM users;")
            cols = [r[0] for r in cur.fetchall()]
            log.write("Existing columns in users:\n" + str(cols) + "\n")

            if 'author_application_status' not in cols:
                log.write("Adding author_application_status...\n")
                cur.execute("ALTER TABLE users ADD COLUMN author_application_status VARCHAR(20) DEFAULT NULL;")
            
            if 'author_application_data' not in cols:
                log.write("Adding author_application_data...\n")
                cur.execute("ALTER TABLE users ADD COLUMN author_application_data TEXT DEFAULT NULL;")

            conn.commit()

            cur.execute("SHOW COLUMNS FROM products;")
            cols_p = [r[0] for r in cur.fetchall()]
            if 'rejection_reason' not in cols_p:
                log.write("Adding rejection_reason...\n")
                cur.execute("ALTER TABLE products ADD COLUMN rejection_reason TEXT DEFAULT NULL;")
                conn.commit()

            cur.execute("SHOW COLUMNS FROM user_access;")
            cols_a = [r[0] for r in cur.fetchall()]
            if 'accessed_at' not in cols_a:
                log.write("Adding user_access.accessed_at...\n")
                cur.execute(
                    "ALTER TABLE user_access ADD COLUMN accessed_at DATETIME DEFAULT NULL AFTER granted_at;"
                )
                conn.commit()

            log.write("Database schema migration complete.\n")

    except Exception as e:
        log.write(f"Error: {e}\n")
    finally:
        if 'conn' in locals() and conn:
            conn.close()

