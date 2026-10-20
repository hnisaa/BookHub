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

        // Eski veritabanlarında olmayan sütunları ekle (veri kaybı olmadan)
        EnsureColumn("Categories", "GroupName", "TEXT NOT NULL DEFAULT 'Diğer'");

        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Users")) == 0)
        {
            AddUser("admin", "123456", "Yönetici", "admin");
            AddUser("misafir", "123456", "Misafir Kullanıcı", "musteri");
        }

        // Örnek veriler sürüm sürüm eklenir; her sürüm bir kez çalışır.
        // Böylece yöneticinin sildiği örnek kitaplar yeniden açılışta geri gelmez.
        var version = Convert.ToInt32(Scalar("SELECT Value FROM Meta WHERE Key = 'seed_version'") ?? 0);
        if (version < 1)
        {
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Categories")) == 0)
                foreach (var name in new[] { "Roman", "Klasik", "Bilim Kurgu", "Tarih", "Kişisel Gelişim", "Çocuk & Genç" })
                    Execute("INSERT INTO Categories (Name) VALUES ($n)", ("$n", name));
            if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Books")) == 0)
                SeedBooks(SeedV1);
        }
        if (version < 2)
        {
            foreach (var (group, names) in CategoryGroupsV2)
                foreach (var name in names)
                {
                    // Var olan kategoriye grup ata (yönetici başka grup verdiyse dokunma), yoksa ekle
                    if (Execute("UPDATE Categories SET GroupName = $g WHERE Name = $n AND GroupName = 'Diğer'", ("$g", group), ("$n", name)) == 0
                        && Scalar("SELECT 1 FROM Categories WHERE Name = $n", ("$n", name)) is null)
                        Execute("INSERT INTO Categories (Name, GroupName) VALUES ($n, $g)", ("$n", name), ("$g", group));
                }
            SeedBooks(SeedV2);
        }
        Execute("INSERT OR REPLACE INTO Meta (Key, Value) VALUES ('seed_version', '2')");
    }

    void EnsureColumn(string table, string column, string definition)
    {
        var exists = Query($"PRAGMA table_info({table})", r => (string)r["name"])
            .Any(c => c.Equals(column, StringComparison.OrdinalIgnoreCase));
        if (!exists) Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }

    public void AddUser(string username, string password, string fullName, string role)
    {
        var (hash, salt) = HashPassword(password);
        Execute("INSERT INTO Users (Username, PasswordHash, Salt, FullName, Role, CreatedAt) VALUES ($u,$h,$s,$f,$r,$c)",
            ("$u", username), ("$h", hash), ("$s", salt), ("$f", fullName), ("$r", role), ("$c", Now()));
    }

    long? CategoryId(string name) => Scalar("SELECT Id FROM Categories WHERE Name = $n", ("$n", name)) is { } id ? Convert.ToInt64(id) : null;

    static readonly (string Group, string[] Names)[] CategoryGroupsV2 =
    [
        ("Edebiyat", ["Roman", "Klasik", "Öykü", "Şiir", "Polisiye & Gerilim", "Fantastik", "Bilim Kurgu"]),
        ("Düşünce & Bilim", ["Tarih", "Felsefe", "Popüler Bilim", "Psikoloji", "Kişisel Gelişim"]),
        ("Çocuk & Gençlik", ["Çocuk & Genç", "Çizgi Roman"]),
        ("Yaşam & Sanat", ["Sanat & Tasarım", "Biyografi", "Gezi"]),
    ];

    // (başlık, yazar, kategori, durum, durum notu, fiyat, stok, sayfa, yıl, kapak, yazı rengi, 3B, vitrin, açıklama)
    static readonly (string, string, string, string, string?, decimal, int, int, int?, string, string, bool, bool, string)[] SeedV1 =
    [
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
    ];

    static readonly (string, string, string, string, string?, decimal, int, int, int?, string, string, bool, bool, string)[] SeedV2 =
    [
        // Edebiyat
        ("Yüzyıllık Yalnızlık", "Gabriel García Márquez", "Roman", "sifir", null, 195, 10, 464, 1967, "#d9a441", "#3b1f0e", true, false,
         "Macondo kasabasında Buendía ailesinin kuşaklar boyu süren hikâyesi; büyülü gerçekçiliğin başyapıtı."),
        ("Aylak Adam", "Yusuf Atılgan", "Roman", "ikinci_el", "İyi – kapakta hafif solma", 50, 3, 184, 1959, "#3d4f5c", "#efe3cc", false, false,
         "İstanbul sokaklarında aradığı kadını bulmaya çalışan C.'nin yalnızlığı ve yabancılaşması."),
        ("Anna Karenina", "Lev Tolstoy", "Klasik", "sifir", null, 230, 6, 960, 1878, "#6a1e33", "#f5dfc2", true, false,
         "Yasak bir aşkın ve 19. yüzyıl Rus aristokrasisinin ikiyüzlülüğünün geniş bir panoraması."),
        ("Dönüşüm", "Franz Kafka", "Klasik", "ikinci_el", "Çok iyi", 25, 8, 80, 1915, "#2b2b2b", "#d8cfb8", false, false,
         "Bir sabah dev bir böceğe dönüşmüş olarak uyanan Gregor Samsa ve ailesinin değişen tutumu."),
        ("Semaver", "Sait Faik Abasıyanık", "Öykü", "sifir", null, 70, 15, 120, 1936, "#7a3f2a", "#f6e7cf", false, false,
         "Sait Faik'in sıradan insanların hayatını sıcak bir dille anlattığı ilk öykü kitabı."),
        ("Değirmen", "Sabahattin Ali", "Öykü", "ikinci_el", "İyi – sayfa kenarlarında sararma", 35, 2, 150, 1935, "#4b5d43", "#f0e6cf", false, false,
         "Anadolu insanının yoksulluğunu ve haksızlıklarla mücadelesini anlatan öyküler."),
        ("Memleketimden İnsan Manzaraları", "Nazım Hikmet", "Şiir", "sifir", null, 160, 9, 480, 1966, "#9c2f2f", "#fbe9d0", true, false,
         "Bir trenin yolculuğu boyunca yüzlerce insanın hayatından kesitler sunan destansı şiir."),
        ("Bütün Şiirleri", "Orhan Veli Kanık", "Şiir", "ikinci_el", "Yeni gibi", 60, 4, 230, null, "#e3c770", "#2a2416", false, false,
         "Garip akımının kurucusu Orhan Veli'nin gündelik dili şiire taşıyan bütün şiirleri."),
        ("Doğu Ekspresinde Cinayet", "Agatha Christie", "Polisiye & Gerilim", "sifir", null, 110, 20, 256, 1934, "#14324a", "#e7c46a", true, false,
         "Karda mahsur kalan bir trende işlenen cinayeti çözmeye çalışan Hercule Poirot."),
        ("Baskervillerin Köpeği", "Arthur Conan Doyle", "Polisiye & Gerilim", "ikinci_el", "İyi – sırtta hafif kırışıklık", 40, 3, 224, 1902, "#1f3a2f", "#d9e3c8", false, false,
         "Sherlock Holmes ve Dr. Watson'ın bataklıkta dolaşan efsanevi bir köpeğin izini sürdüğü macera."),
        ("Hobbit", "J.R.R. Tolkien", "Fantastik", "sifir", null, 175, 14, 352, 1937, "#2f5a3a", "#f1d98c", true, true,
         "Bilbo Baggins'in cüceler ve büyücü Gandalf ile ejderha Smaug'un hazinesine uzanan yolculuğu."),
        ("Harry Potter ve Felsefe Taşı", "J.K. Rowling", "Fantastik", "ikinci_el", "Çok iyi – ilk sayfada isim yazılı", 85, 5, 276, 1997, "#7b2232", "#f4cf63", false, false,
         "On bir yaşındaki Harry'nin bir büyücü olduğunu öğrenip Hogwarts'a başladığı ilk yıl."),
        ("Fahrenheit 451", "Ray Bradbury", "Bilim Kurgu", "sifir", null, 105, 12, 208, 1953, "#c4421d", "#1d0f08", true, false,
         "Kitapların yasak olduğu ve itfaiyecilerin kitap yaktığı bir gelecekte uyanmaya başlayan Montag."),
        ("Vakıf", "Isaac Asimov", "Bilim Kurgu", "ikinci_el", "İyi – kapak köşesinde katlanma", 65, 2, 280, 1951, "#1b2a4a", "#a9c7e8", false, false,
         "Çöken bir galaktik imparatorluğun ardından karanlık çağı kısaltmaya çalışan bilim insanları."),
        // Düşünce & Bilim
        ("Nutuk", "Mustafa Kemal Atatürk", "Tarih", "sifir", null, 140, 18, 640, 1927, "#7a1c1c", "#f2dcae", true, false,
         "Kurtuluş Savaşı'nın ve Cumhuriyet'in kuruluşunun Atatürk'ün kendi anlatımıyla tarihi."),
        ("Osmanlı İmparatorluğu Klasik Çağ", "Halil İnalcık", "Tarih", "ikinci_el", "Çok iyi – altı çizili birkaç satır", 95, 2, 336, 1973, "#5c3b1e", "#efd9b0", false, false,
         "Osmanlı Devleti'nin 1300-1600 arası siyasi, ekonomik ve kurumsal yapısının temel kaynağı."),
        ("Devlet", "Platon", "Felsefe", "sifir", null, 125, 8, 384, null, "#e6dcc6", "#3a2f22", false, false,
         "Adaletin ne olduğu sorusundan yola çıkarak ideal devleti tartışan felsefe klasiği."),
        ("Sofie'nin Dünyası", "Jostein Gaarder", "Felsefe", "ikinci_el", "İyi", 70, 4, 560, 1991, "#3b6ea5", "#fdf3d6", false, false,
         "Gizemli mektuplar alan genç Sofie'nin gözünden felsefe tarihine roman tadında bir giriş."),
        ("Zamanın Kısa Tarihi", "Stephen Hawking", "Popüler Bilim", "sifir", null, 150, 11, 248, 1988, "#0f1a2e", "#e9c46a", true, false,
         "Büyük Patlama'dan kara deliklere, evrenin işleyişini herkesin anlayacağı dille anlatan klasik."),
        ("Kozmos", "Carl Sagan", "Popüler Bilim", "ikinci_el", "Çok iyi", 80, 3, 432, 1980, "#24123d", "#d7b8f3", false, false,
         "Evrenin ve bilimin tarihini insanlığın merakıyla birleştiren ünlü popüler bilim kitabı."),
        ("İnsanın Anlam Arayışı", "Viktor E. Frankl", "Psikoloji", "sifir", null, 95, 16, 168, 1946, "#e8e2d6", "#1f2b38", false, false,
         "Toplama kampı deneyiminden yola çıkarak yaşamın anlamını sorgulayan logoterapinin temel eseri."),
        ("Atomik Alışkanlıklar", "James Clear", "Kişisel Gelişim", "sifir", null, 180, 22, 312, 2018, "#f2efe6", "#c0392b", false, false,
         "Küçük değişikliklerin zamanla büyük sonuçlar doğurduğunu anlatan, alışkanlık edinme rehberi."),
        // Çocuk & Gençlik
        ("Pal Sokağı Çocukları", "Ferenc Molnár", "Çocuk & Genç", "sifir", null, 55, 25, 216, 1906, "#3a7d5c", "#fff4cf", true, false,
         "Budapeşte'de oyun alanlarını korumak için birleşen çocukların dostluk ve fedakârlık hikâyesi."),
        ("Şeker Portakalı", "José Mauro de Vasconcelos", "Çocuk & Genç", "ikinci_el", "İyi – kapakta çizik", 30, 6, 184, 1968, "#e07a2e", "#2b1a0c", false, false,
         "Yoksul ama hayal gücü zengin Zezé'nin portakal fidanıyla kurduğu dostluk."),
        ("Maus", "Art Spiegelman", "Çizgi Roman", "sifir", null, 320, 5, 296, 1991, "#d8d2c4", "#111111", false, false,
         "Yazarın babasının Holokost'tan kurtuluş hikâyesini fareler ve kediler üzerinden anlatan çizgi roman."),
        ("Persepolis", "Marjane Satrapi", "Çizgi Roman", "ikinci_el", "Yeni gibi", 150, 2, 352, 2000, "#121212", "#f2f2f2", false, false,
         "İran Devrimi yıllarında büyüyen bir kız çocuğunun siyah beyaz çizgilerle anlatılan otobiyografisi."),
        // Yaşam & Sanat
        ("Sanatın Öyküsü", "E. H. Gombrich", "Sanat & Tasarım", "sifir", null, 650, 4, 688, 1950, "#b23a2a", "#fbf2e2", true, false,
         "Mağara resimlerinden modern sanata, sanat tarihini akıcı bir dille anlatan temel başvuru kitabı."),
        ("Steve Jobs", "Walter Isaacson", "Biyografi", "ikinci_el", "Çok iyi", 120, 3, 704, 2011, "#f5f5f5", "#1a1a1a", false, false,
         "Apple'ın kurucusu Steve Jobs'un hayatını yüzlerce röportaja dayanarak anlatan biyografi."),
        ("Seyahatname", "Evliya Çelebi", "Gezi", "sifir", null, 210, 7, 600, null, "#0f4c5c", "#f2d49b", false, false,
         "17. yüzyılda Osmanlı topraklarını ve çevresini dolaşan Evliya Çelebi'nin ünlü gezi notları."),
        ("İstanbul: Hatıralar ve Şehir", "Orhan Pamuk", "Gezi", "ikinci_el", "İyi – birkaç sayfada not", 75, 2, 376, 2003, "#5d6d7e", "#f4ecdf", false, false,
         "Yazarın çocukluk ve gençlik yıllarını İstanbul'un hüzünlü atmosferiyle birlikte anlattığı anı kitabı."),
    ];

    void SeedBooks((string, string, string, string, string?, decimal, int, int, int?, string, string, bool, bool, string)[] books)
    {
        foreach (var b in books)
        {
            // Aynı kitap (ad + yazar) zaten varsa tekrar ekleme
            if (Scalar("SELECT 1 FROM Books WHERE Title = $t AND Author = $a", ("$t", b.Item1), ("$a", b.Item2)) is not null) continue;
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
