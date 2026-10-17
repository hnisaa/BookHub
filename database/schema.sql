-- =====================================================================
--  BookHub veritabanı şeması (SQLite)
--  Sunucu her açılışta bu dosyayı çalıştırır; tablolar yoksa oluşturulur.
--  İleride MySQL / PostgreSQL / SQL Server'a geçerken:
--    INTEGER PRIMARY KEY AUTOINCREMENT  ->  INT AUTO_INCREMENT / SERIAL / IDENTITY
--    NUMERIC fiyat alanları             ->  DECIMAL(10,2)
--    TEXT tarih alanları (ISO 8601)     ->  DATETIME / TIMESTAMP
-- =====================================================================

CREATE TABLE IF NOT EXISTS Users (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Username     TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash TEXT    NOT NULL,          -- PBKDF2-SHA256 (Base64)
    Salt         TEXT    NOT NULL,          -- rastgele 16 bayt (Base64)
    FullName     TEXT    NOT NULL,
    Role         TEXT    NOT NULL CHECK (Role IN ('admin', 'musteri')),
    CreatedAt    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Sessions (
    Token     TEXT    PRIMARY KEY,
    UserId    INTEGER NOT NULL REFERENCES Users(Id) ON DELETE CASCADE,
    ExpiresAt TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS Categories (
    Id   INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT    NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS Books (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    Title         TEXT    NOT NULL,
    Author        TEXT    NOT NULL,
    Publisher     TEXT,
    Isbn          TEXT,
    CategoryId    INTEGER REFERENCES Categories(Id) ON DELETE SET NULL,
    Condition     TEXT    NOT NULL CHECK (Condition IN ('sifir', 'ikinci_el')),
    ConditionNote TEXT,                     -- 2. el kitabın durumu ("Çok iyi", "Sayfalarda not var" ...)
    Price         NUMERIC NOT NULL CHECK (Price >= 0),
    Stock         INTEGER NOT NULL DEFAULT 0 CHECK (Stock >= 0),
    Pages         INTEGER,
    Year          INTEGER,
    Description   TEXT,
    CoverColor    TEXT    NOT NULL DEFAULT '#7a2e3a',
    TextColor     TEXT    NOT NULL DEFAULT '#f5e9d3',
    CoverImage    TEXT,                     -- /uploads/... veya tam URL (boşsa renkli kapak çizilir)
    Has3D         INTEGER NOT NULL DEFAULT 0, -- 1 ise detay sayfasında 3B görüntüleyici açılır
    Featured      INTEGER NOT NULL DEFAULT 0, -- 1 ise anasayfadaki 3B rafta görünür
    CreatedAt     TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_Books_Condition ON Books(Condition);
CREATE INDEX IF NOT EXISTS IX_Books_Category  ON Books(CategoryId);

CREATE TABLE IF NOT EXISTS Orders (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId    INTEGER NOT NULL REFERENCES Users(Id),
    Total     NUMERIC NOT NULL,
    Status    TEXT    NOT NULL DEFAULT 'hazirlaniyor',
    Address   TEXT,
    CreatedAt TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS OrderItems (
    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
    OrderId   INTEGER NOT NULL REFERENCES Orders(Id) ON DELETE CASCADE,
    BookId    INTEGER REFERENCES Books(Id) ON DELETE SET NULL,
    Title     TEXT    NOT NULL,             -- kitap silinse bile siparişte adı kalsın
    Quantity  INTEGER NOT NULL CHECK (Quantity > 0),
    UnitPrice NUMERIC NOT NULL
);
