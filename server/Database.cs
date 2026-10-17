using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace BookHub;

/// <summary>
/// SQLite erişim katmanı. Tüm SQL burada; başka bir veritabanına geçerken
/// yalnızca bu dosyanın değişmesi yeterli olacak şekilde tasarlandı.
/// </summary>
public sealed class Database(string connectionString)
{
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    // ------------------------------------------------------------------ yardımcılar

    public static SqliteCommand Cmd(SqliteConnection conn, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, sql, args);
        using var r = cmd.ExecuteReader();
        var list = new List<T>();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    public T? One<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args) where T : class =>
        Query(sql, map, args).FirstOrDefault();

    public int Execute(string sql, params (string, object?)[] args)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, sql, args);
        return cmd.ExecuteNonQuery();
    }

    public long Insert(string sql, params (string, object?)[] args)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, sql + "; SELECT last_insert_rowid();", args);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public object? Scalar(string sql, params (string, object?)[] args)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, sql, args);
        return cmd.ExecuteScalar();
    }

    static string? Str(SqliteDataReader r, string col) => r[col] is DBNull ? null : Convert.ToString(r[col]);
    static int? Int(SqliteDataReader r, string col) => r[col] is DBNull ? null : Convert.ToInt32(r[col]);

    public static string Now() => DateTime.UtcNow.ToString("o");

    // ------------------------------------------------------------------ şifreleme

    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool VerifyPassword(string password, string hash, string salt)
    {
        var computed = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(computed, Convert.FromBase64String(hash));
    }

    // ------------------------------------------------------------------ eşleyiciler

    public static User MapUser(SqliteDataReader r) =>
        new((long)r["Id"], (string)r["Username"], (string)r["FullName"], (string)r["Role"]);

    public static Book MapBook(SqliteDataReader r) => new(
        Id: (long)r["Id"],
        Title: (string)r["Title"],
        Author: (string)r["Author"],
        Publisher: Str(r, "Publisher"),
        Isbn: Str(r, "Isbn"),
        CategoryId: r["CategoryId"] is DBNull ? null : (long)r["CategoryId"],
        CategoryName: Str(r, "CategoryName"),
        Condition: (string)r["Condition"],
        ConditionNote: Str(r, "ConditionNote"),
        Price: Convert.ToDecimal(r["Price"]),
        Stock: Convert.ToInt32(r["Stock"]),
        Pages: Int(r, "Pages"),
        Year: Int(r, "Year"),
        Description: Str(r, "Description"),
        CoverColor: (string)r["CoverColor"],
        TextColor: (string)r["TextColor"],
        CoverImage: Str(r, "CoverImage"),
        Has3D: Convert.ToInt32(r["Has3D"]) == 1,
        Featured: Convert.ToInt32(r["Featured"]) == 1,
        CreatedAt: (string)r["CreatedAt"]);

    public const string BookSelect = """
        SELECT b.*, c.Name AS CategoryName
        FROM Books b LEFT JOIN Categories c ON c.Id = b.CategoryId
        """;

    // ------------------------------------------------------------------ kurulum + örnek veri

    public void Initialize(string schemaFile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(new SqliteConnectionStringBuilder(connectionString).DataSource)!);
        using (var conn = Open())
        using (var cmd = Cmd(conn, File.ReadAllText(schemaFile)))
            cmd.ExecuteNonQuery();

        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Users")) == 0)
        {
            AddUser("admin", "123456", "Yönetici", "admin");
            AddUser("misafir", "123456", "Misafir Kullanıcı", "musteri");
        }

        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Categories")) == 0)
            foreach (var name in new[] { "Roman", "Klasik", "Bilim Kurgu", "Tarih", "Kişisel Gelişim", "Çocuk & Genç" })
                Execute("INSERT INTO Categories (Name) VALUES ($n)", ("$n", name));

        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Books")) == 0)
            SeedBooks();
    }

    public void AddUser(string username, string password, string fullName, string role)
    {
        var (hash, salt) = HashPassword(password);
        Execute("INSERT INTO Users (Username, PasswordHash, Salt, FullName, Role, CreatedAt) VALUES ($u,$h,$s,$f,$r,$c)",
            ("$u", username), ("$h", hash), ("$s", salt), ("$f", fullName), ("$r", role), ("$c", Now()));
    }

    long CategoryId(string name) => Convert.ToInt64(Scalar("SELECT Id FROM Categories WHERE Name = $n", ("$n", name)));

    void SeedBooks()
    {
        // (başlık, yazar, kategori, durum, durum notu, fiyat, stok, sayfa, yıl, kapak, yazı rengi, 3B, vitrin, açıklama)
        var books = new (string, string, string, string, string?, decimal, int, int, int, string, string, bool, bool, string)[]
        {
            ("Suç ve Ceza", "Fyodor Dostoyevski", "Klasik", "sifir", null, 185, 12, 687, 1866, "#5b1f2a", "#f3e2c4", true, true,
             "Yoksul bir öğrenci olan Raskolnikov'un işlediği cinayetin ardından yaşadığı vicdan muhasebesini anlatan psikolojik roman."),
            ("Kürk Mantolu Madonna", "Sabahattin Ali", "Roman", "sifir", null, 95, 25, 160, 1943, "#2f3d5c", "#f0d9a8", true, true,
             "Raif Efendi'nin defterinden okunan, Berlin'de başlayan sessiz ve derin bir aşk hikâyesi."),
            ("1984", "George Orwell", "Bilim Kurgu", "sifir", null, 120, 18, 352, 1949, "#1d1d1d", "#e04a3a", true, true,
             "Büyük Birader'in her şeyi gözetlediği distopik bir dünyada bireyin özgürlük arayışı."),
            ("Sefiller", "Victor Hugo", "Klasik", "sifir", null, 240, 7, 1488, 1862, "#3c2a1e", "#e8cf9a", true, true,
             "Jean Valjean'ın kefaret yolculuğu üzerinden adalet, merhamet ve toplum üzerine destansı bir anlatı."),
            ("Küçük Prens", "Antoine de Saint-Exupéry", "Çocuk & Genç", "sifir", null, 60, 40, 112, 1943, "#1f5c7a", "#ffe9a8", true, true,
             "Bir pilotun çölde tanıştığı küçük prensle yaşadığı, her yaşa dokunan felsefi masal."),
            ("Dune", "Frank Herbert", "Bilim Kurgu", "sifir", null, 265, 9, 712, 1965, "#b8641e", "#2a1608", true, true,
             "Çöl gezegeni Arrakis'te siyaset, din ve ekolojinin iç içe geçtiği bilim kurgu destanı."),
            ("Saatleri Ayarlama Enstitüsü", "Ahmet Hamdi Tanpınar", "Roman", "sifir", null, 150, 11, 400, 1961, "#4a5a3a", "#f1e6c8", false, true,
             "Hayri İrdal'ın gözünden modernleşme sancılarını ironik bir dille anlatan roman."),
            ("Sapiens", "Yuval Noah Harari", "Tarih", "sifir", null, 210, 14, 412, 2011, "#e8e1d3", "#2b2b2b", false, false,
             "İnsan türünün bilişsel devrimden günümüze uzanan hikâyesine geniş bir bakış."),
            ("Hayvan Çiftliği", "George Orwell", "Klasik", "ikinci_el", "Çok iyi – kapakta hafif yıpranma", 45, 3, 152, 1945, "#6b7f3a", "#f6f0d8", true, true,
             "Sahibine isyan eden çiftlik hayvanlarının hikâyesi üzerinden iktidarın yozlaşmasını anlatan alegori."),
            ("Tutunamayanlar", "Oğuz Atay", "Roman", "ikinci_el", "İyi – birkaç sayfada kurşun kalem notu", 110, 2, 724, 1972, "#7d3c2b", "#f4e4c9", false, true,
             "Selim Işık'ın intiharının ardından arkadaşı Turgut'un onun izini sürdüğü, Türk edebiyatının dönüm noktası."),
            ("İnce Memed", "Yaşar Kemal", "Roman", "ikinci_el", "Yeni gibi", 90, 4, 436, 1955, "#8a5a2b", "#fbecd0", false, false,
             "Çukurova'da ağalara başkaldıran Memed'in efsaneleşen hikâyesi."),
            ("Martin Eden", "Jack London", "Klasik", "ikinci_el", "İyi – sayfalarda sararma", 55, 5, 480, 1909, "#2c4a52", "#e9dcc0", true, false,
             "Yoksul bir denizcinin yazar olma tutkusu ve başarının bedeli üzerine yarı otobiyografik roman."),
            ("Simyacı", "Paulo Coelho", "Kişisel Gelişim", "ikinci_el", "Çok iyi", 40, 6, 188, 1988, "#c49a3a", "#3a2410", false, false,
             "Endülüslü çoban Santiago'nun hazinesini bulmak için çıktığı ve kendini keşfettiği yolculuk."),
            ("Beyaz Diş", "Jack London", "Çocuk & Genç", "ikinci_el", "Orta – sırtta kırışıklık", 30, 1, 312, 1906, "#9aa5ad", "#1e2a33", false, false,
             "Yarı kurt yarı köpek Beyaz Diş'in vahşi doğadan insanlar arasına uzanan hayat mücadelesi."),
        };

        foreach (var b in books)
        {
            Execute("""
                INSERT INTO Books (Title, Author, CategoryId, Condition, ConditionNote, Price, Stock, Pages, Year,
                                   CoverColor, TextColor, Has3D, Featured, Description, CreatedAt)
                VALUES ($t,$a,$c,$cond,$note,$p,$s,$pg,$y,$cc,$tc,$3d,$f,$d,$now)
                """,
                ("$t", b.Item1), ("$a", b.Item2), ("$c", CategoryId(b.Item3)), ("$cond", b.Item4), ("$note", b.Item5),
                ("$p", b.Item6), ("$s", b.Item7), ("$pg", b.Item8), ("$y", b.Item9), ("$cc", b.Item10), ("$tc", b.Item11),
                ("$3d", b.Item12 ? 1 : 0), ("$f", b.Item13 ? 1 : 0), ("$d", b.Item14), ("$now", Now()));
        }
    }
}

public record User(long Id, string Username, string FullName, string Role)
{
    public bool IsAdmin => Role == "admin";
}

public record Book(long Id, string Title, string Author, string? Publisher, string? Isbn, long? CategoryId, string? CategoryName,
    string Condition, string? ConditionNote, decimal Price, int Stock, int? Pages, int? Year, string? Description,
    string CoverColor, string TextColor, string? CoverImage, bool Has3D, bool Featured, string CreatedAt);

public record BookInput(string? Title, string? Author, string? Publisher, string? Isbn, long? CategoryId,
    string? Condition, string? ConditionNote, decimal? Price, int? Stock, int? Pages, int? Year, string? Description,
    string? CoverColor, string? TextColor, string? CoverImage, bool? Has3D, bool? Featured);

public record LoginInput(string? Username, string? Password);
public record OrderItemInput(long BookId, int Quantity);
public record OrderInput(List<OrderItemInput>? Items, string? Address);
public record StatusInput(string? Status);
