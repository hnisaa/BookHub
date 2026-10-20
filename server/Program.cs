using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BookHub;

// ---------------------------------------------------------------------- kurulum

Console.OutputEncoding = System.Text.Encoding.UTF8;
var root = FindRoot();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = root,
    WebRootPath = Path.Combine(root, "wwwroot"),
});

var db = new Database($"Data Source={Path.Combine(root, "database", "bookhub.db")}");
db.Initialize(Path.Combine(root, "database", "schema.sql"));
builder.Services.AddSingleton(db);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();
app.UseDefaultFiles();
// no-cache: dosyalar her istekte doğrulanır, böylece HTML/CSS/JS değişiklikleri anında görünür
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",
});

const string SessionCookie = "bh_session";
string[] orderStatuses = ["hazirlaniyor", "kargoda", "teslim_edildi", "iptal"];

User? CurrentUser(HttpContext ctx)
{
    if (!ctx.Request.Cookies.TryGetValue(SessionCookie, out var token) || string.IsNullOrEmpty(token)) return null;
    return db.One("""
        SELECT u.* FROM Sessions s JOIN Users u ON u.Id = s.UserId
        WHERE s.Token = $t AND s.ExpiresAt > $now
        """, Database.MapUser, ("$t", token), ("$now", Database.Now()));
}

static IResult Error(string message, int status = 400) => Results.Json(new { error = message }, statusCode: status);
static IResult NeedLogin() => Error("Bu işlem için giriş yapmalısınız.", 401);
static IResult NeedAdmin() => Error("Bu işlem yalnızca yönetici içindir.", 403);

// ---------------------------------------------------------------------- oturum

app.MapPost("/api/auth/login", (LoginInput input, HttpContext ctx) =>
{
    var row = db.One("SELECT * FROM Users WHERE Username = $u",
        r => new { User = Database.MapUser(r), Hash = (string)r["PasswordHash"], Salt = (string)r["Salt"] },
        ("$u", input.Username?.Trim() ?? ""));

    if (row is null || !Database.VerifyPassword(input.Password ?? "", row.Hash, row.Salt))
        return Error("Kullanıcı adı veya şifre hatalı.", 401);

    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var expires = DateTime.UtcNow.AddDays(7);
    db.Execute("DELETE FROM Sessions WHERE ExpiresAt < $now", ("$now", Database.Now()));
    db.Execute("INSERT INTO Sessions (Token, UserId, ExpiresAt) VALUES ($t,$u,$e)",
        ("$t", token), ("$u", row.User.Id), ("$e", expires.ToString("o")));

    ctx.Response.Cookies.Append(SessionCookie, token, new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Lax, Expires = expires, Secure = ctx.Request.IsHttps,
    });
    return Results.Ok(row.User);
});

app.MapPost("/api/auth/logout", (HttpContext ctx) =>
{
    if (ctx.Request.Cookies.TryGetValue(SessionCookie, out var token))
        db.Execute("DELETE FROM Sessions WHERE Token = $t", ("$t", token));
    ctx.Response.Cookies.Delete(SessionCookie);
    return Results.Ok();
});

app.MapGet("/api/auth/me", (HttpContext ctx) => Results.Json(CurrentUser(ctx)));

// ---------------------------------------------------------------------- kategoriler

app.MapGet("/api/categories", () => db.Query("""
    SELECT c.Id, c.Name, c.GroupName, COUNT(b.Id) AS Cnt FROM Categories c
    LEFT JOIN Books b ON b.CategoryId = c.Id GROUP BY c.Id ORDER BY c.GroupName, c.Name
    """, r => new { id = (long)r["Id"], name = (string)r["Name"], group = (string)r["GroupName"], count = (long)r["Cnt"] }));

app.MapPost("/api/categories", (HttpContext ctx, CategoryInput input) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    var name = input.Name?.Trim();
    var group = string.IsNullOrWhiteSpace(input.Group) ? "Diğer" : input.Group.Trim();
    if (string.IsNullOrEmpty(name)) return Error("Kategori adı boş olamaz.");
    if (db.Scalar("SELECT 1 FROM Categories WHERE Name = $n", ("$n", name)) is not null) return Error("Bu kategori zaten var.");
    var id = db.Insert("INSERT INTO Categories (Name, GroupName) VALUES ($n, $g)", ("$n", name), ("$g", group));
    return Results.Ok(new { id, name, group, count = 0 });
});

// ---------------------------------------------------------------------- kitaplar

app.MapGet("/api/books", (string? condition, long? category, string? q, string? sort, bool? featured) =>
{
    var where = new List<string>();
    var args = new List<(string, object?)>();
    if (condition is "sifir" or "ikinci_el") { where.Add("b.Condition = $cond"); args.Add(("$cond", condition)); }
    if (category is not null) { where.Add("b.CategoryId = $cat"); args.Add(("$cat", category)); }
    if (featured == true) where.Add("b.Featured = 1");
    if (!string.IsNullOrWhiteSpace(q))
    {
        where.Add("(b.Title LIKE $q OR b.Author LIKE $q OR b.Isbn LIKE $q)");
        args.Add(("$q", $"%{q.Trim()}%"));
    }
    var order = sort switch
    {
        "fiyat_artan" => "b.Price ASC",
        "fiyat_azalan" => "b.Price DESC",
        "ad" => "b.Title COLLATE NOCASE ASC",
        _ => "b.Id DESC",
    };
    var sql = Database.BookSelect + (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + " ORDER BY " + order;
    return db.Query(sql, Database.MapBook, args.ToArray());
});

app.MapGet("/api/books/{id:long}", (long id) =>
    db.One(Database.BookSelect + " WHERE b.Id = $id", Database.MapBook, ("$id", id)) is { } book
        ? Results.Ok(book) : Error("Kitap bulunamadı.", 404));

string? ValidateBook(BookInput b)
{
    if (string.IsNullOrWhiteSpace(b.Title)) return "Kitap adı zorunludur.";
    if (string.IsNullOrWhiteSpace(b.Author)) return "Yazar zorunludur.";
    if (b.Condition is not ("sifir" or "ikinci_el")) return "Durum 'sifir' veya 'ikinci_el' olmalıdır.";
    if (b.Price is null or < 0) return "Geçerli bir fiyat girin.";
    if (b.Stock is < 0) return "Stok negatif olamaz.";
    var hex = new Regex("^#[0-9a-fA-F]{6}$");
    if (b.CoverColor is not null && !hex.IsMatch(b.CoverColor)) return "Kapak rengi #RRGGBB biçiminde olmalı.";
    if (b.TextColor is not null && !hex.IsMatch(b.TextColor)) return "Yazı rengi #RRGGBB biçiminde olmalı.";
    return null;
}

(string, object?)[] BookArgs(BookInput b) =>
[
    ("$t", b.Title!.Trim()), ("$a", b.Author!.Trim()), ("$pub", b.Publisher?.Trim()), ("$isbn", b.Isbn?.Trim()),
    ("$cat", b.CategoryId), ("$cond", b.Condition), ("$note", b.Condition == "ikinci_el" ? b.ConditionNote?.Trim() : null),
    ("$p", b.Price), ("$s", b.Stock ?? 0), ("$pg", b.Pages), ("$y", b.Year), ("$d", b.Description?.Trim()),
    ("$cc", b.CoverColor ?? "#7a2e3a"), ("$tc", b.TextColor ?? "#f5e9d3"), ("$img", string.IsNullOrWhiteSpace(b.CoverImage) ? null : b.CoverImage.Trim()),
    ("$3d", b.Has3D == true ? 1 : 0), ("$f", b.Featured == true ? 1 : 0),
];

app.MapPost("/api/books", (HttpContext ctx, BookInput input) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    if (ValidateBook(input) is { } err) return Error(err);
    var id = db.Insert("""
        INSERT INTO Books (Title, Author, Publisher, Isbn, CategoryId, Condition, ConditionNote, Price, Stock, Pages, Year,
                           Description, CoverColor, TextColor, CoverImage, Has3D, Featured, CreatedAt)
        VALUES ($t,$a,$pub,$isbn,$cat,$cond,$note,$p,$s,$pg,$y,$d,$cc,$tc,$img,$3d,$f,$now)
        """, [.. BookArgs(input), ("$now", Database.Now())]);
    return Results.Ok(db.One(Database.BookSelect + " WHERE b.Id = $id", Database.MapBook, ("$id", id)));
});

app.MapPut("/api/books/{id:long}", (long id, HttpContext ctx, BookInput input) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    if (ValidateBook(input) is { } err) return Error(err);
    var n = db.Execute("""
        UPDATE Books SET Title=$t, Author=$a, Publisher=$pub, Isbn=$isbn, CategoryId=$cat, Condition=$cond,
            ConditionNote=$note, Price=$p, Stock=$s, Pages=$pg, Year=$y, Description=$d, CoverColor=$cc,
            TextColor=$tc, CoverImage=$img, Has3D=$3d, Featured=$f
        WHERE Id = $id
        """, [.. BookArgs(input), ("$id", id)]);
    return n == 0 ? Error("Kitap bulunamadı.", 404)
        : Results.Ok(db.One(Database.BookSelect + " WHERE b.Id = $id", Database.MapBook, ("$id", id)));
});

app.MapDelete("/api/books/{id:long}", (long id, HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    return db.Execute("DELETE FROM Books WHERE Id = $id", ("$id", id)) == 0 ? Error("Kitap bulunamadı.", 404) : Results.Ok();
});

// Kapak görseli yükleme (yalnızca yönetici) -> wwwroot/uploads
app.MapPost("/api/upload", async (HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    if (!ctx.Request.HasFormContentType) return Error("Dosya gönderilmedi.");
    var file = (await ctx.Request.ReadFormAsync()).Files.FirstOrDefault();
    if (file is null || file.Length == 0) return Error("Dosya gönderilmedi.");
    if (file.Length > 5 * 1024 * 1024) return Error("Dosya en fazla 5 MB olabilir.");
    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp")) return Error("Yalnızca JPG, PNG veya WEBP yüklenebilir.");

    var dir = Path.Combine(app.Environment.WebRootPath, "uploads");
    Directory.CreateDirectory(dir);
    var name = $"{Guid.NewGuid():N}{ext}";
    await using (var fs = File.Create(Path.Combine(dir, name)))
        await file.CopyToAsync(fs);
    return Results.Ok(new { url = $"/uploads/{name}" });
}).DisableAntiforgery();

// ---------------------------------------------------------------------- favoriler

app.MapGet("/api/favorites", (HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { } user) return NeedLogin();
    return Results.Ok(db.Query(Database.BookSelect + """
         JOIN Favorites f ON f.BookId = b.Id WHERE f.UserId = $u ORDER BY f.CreatedAt DESC
        """, Database.MapBook, ("$u", user.Id)));
});

app.MapPost("/api/favorites/{bookId:long}", (long bookId, HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { } user) return NeedLogin();
    if (db.Scalar("SELECT 1 FROM Books WHERE Id = $b", ("$b", bookId)) is null) return Error("Kitap bulunamadı.", 404);
    db.Execute("INSERT OR IGNORE INTO Favorites (UserId, BookId, CreatedAt) VALUES ($u,$b,$c)",
        ("$u", user.Id), ("$b", bookId), ("$c", Database.Now()));
    return Results.Ok();
});

app.MapDelete("/api/favorites/{bookId:long}", (long bookId, HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { } user) return NeedLogin();
    db.Execute("DELETE FROM Favorites WHERE UserId = $u AND BookId = $b", ("$u", user.Id), ("$b", bookId));
    return Results.Ok();
});

// ---------------------------------------------------------------------- siparişler

app.MapPost("/api/orders", (HttpContext ctx, OrderInput input) =>
{
    if (CurrentUser(ctx) is not { } user) return NeedLogin();
    var items = input.Items?.Where(i => i.Quantity > 0).GroupBy(i => i.BookId)
        .Select(g => new OrderItemInput(g.Key, g.Sum(i => i.Quantity))).ToList();
    if (items is null || items.Count == 0) return Error("Sepetiniz boş.");

    using var conn = db.Open();
    using var tx = conn.BeginTransaction();
    decimal total = 0;
    var lines = new List<(long BookId, string Title, int Qty, decimal Price)>();
    foreach (var item in items)
    {
        using var cmd = Database.Cmd(conn, "SELECT Title, Price, Stock FROM Books WHERE Id = $id", ("$id", item.BookId));
        cmd.Transaction = tx;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return Error("Sepetteki bir kitap artık satışta değil.");
        var (title, price, stock) = ((string)r["Title"], Convert.ToDecimal(r["Price"]), Convert.ToInt32(r["Stock"]));
        if (stock < item.Quantity) return Error($"\"{title}\" için yeterli stok yok (kalan: {stock}).");
        lines.Add((item.BookId, title, item.Quantity, price));
        total += price * item.Quantity;
    }

    using (var cmd = Database.Cmd(conn, "INSERT INTO Orders (UserId, Total, Address, CreatedAt) VALUES ($u,$t,$a,$c); SELECT last_insert_rowid();",
        ("$u", user.Id), ("$t", total), ("$a", input.Address?.Trim()), ("$c", Database.Now())))
    {
        cmd.Transaction = tx;
        var orderId = Convert.ToInt64(cmd.ExecuteScalar());
        foreach (var l in lines)
        {
            using var ins = Database.Cmd(conn, "INSERT INTO OrderItems (OrderId, BookId, Title, Quantity, UnitPrice) VALUES ($o,$b,$t,$q,$p)",
                ("$o", orderId), ("$b", l.BookId), ("$t", l.Title), ("$q", l.Qty), ("$p", l.Price));
            ins.Transaction = tx;
            ins.ExecuteNonQuery();
            using var upd = Database.Cmd(conn, "UPDATE Books SET Stock = Stock - $q WHERE Id = $b", ("$q", l.Qty), ("$b", l.BookId));
            upd.Transaction = tx;
            upd.ExecuteNonQuery();
        }
        tx.Commit();
        return Results.Ok(new { id = orderId, total });
    }
});

app.MapGet("/api/orders", (HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { } user) return NeedLogin();
    var sql = """
        SELECT o.*, u.Username FROM Orders o JOIN Users u ON u.Id = o.UserId
        """ + (user.IsAdmin ? "" : " WHERE o.UserId = $u") + " ORDER BY o.Id DESC";
    var orders = db.Query(sql, r => new
    {
        id = (long)r["Id"], username = (string)r["Username"], total = Convert.ToDecimal(r["Total"]),
        status = (string)r["Status"], address = r["Address"] as string, createdAt = (string)r["CreatedAt"],
    }, ("$u", user.Id));
    var items = db.Query("SELECT OrderId, BookId, Title, Quantity, UnitPrice FROM OrderItems", r => new
    {
        orderId = (long)r["OrderId"], bookId = r["BookId"] as long?, title = (string)r["Title"],
        quantity = Convert.ToInt32(r["Quantity"]), unitPrice = Convert.ToDecimal(r["UnitPrice"]),
    }).ToLookup(i => i.orderId);
    return Results.Ok(orders.Select(o => new { o.id, o.username, o.total, o.status, o.address, o.createdAt, items = items[o.id] }));
});

app.MapPut("/api/orders/{id:long}/status", (long id, HttpContext ctx, StatusInput input) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    if (!orderStatuses.Contains(input.Status)) return Error("Geçersiz sipariş durumu.");
    return db.Execute("UPDATE Orders SET Status = $s WHERE Id = $id", ("$s", input.Status), ("$id", id)) == 0
        ? Error("Sipariş bulunamadı.", 404) : Results.Ok();
});

// ---------------------------------------------------------------------- yönetici özeti

app.MapGet("/api/stats", (HttpContext ctx) =>
{
    if (CurrentUser(ctx) is not { IsAdmin: true }) return NeedAdmin();
    long Count(string sql) => Convert.ToInt64(db.Scalar(sql));
    return Results.Ok(new
    {
        books = Count("SELECT COUNT(*) FROM Books"),
        newBooks = Count("SELECT COUNT(*) FROM Books WHERE Condition = 'sifir'"),
        usedBooks = Count("SELECT COUNT(*) FROM Books WHERE Condition = 'ikinci_el'"),
        lowStock = Count("SELECT COUNT(*) FROM Books WHERE Stock <= 2"),
        orders = Count("SELECT COUNT(*) FROM Orders"),
        revenue = Convert.ToDecimal(db.Scalar("SELECT COALESCE(SUM(Total), 0) FROM Orders WHERE Status <> 'iptal'")),
        users = Count("SELECT COUNT(*) FROM Users"),
    });
});

// ---------------------------------------------------------------------- başlat

app.Lifetime.ApplicationStarted.Register(() =>
{
    var url = app.Urls.FirstOrDefault() ?? "http://localhost:5080";
    Console.WriteLine();
    Console.WriteLine($"  BookHub çalışıyor  ->  {url}");
    Console.WriteLine($"  Veritabanı         ->  {Path.Combine(root, "database", "bookhub.db")}");
    Console.WriteLine("  Kapatmak için bu pencerede Ctrl+C'ye basın.");
    Console.WriteLine();
    if (Environment.GetEnvironmentVariable("BOOKHUB_NO_BROWSER") != "1")
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* tarayıcı açılamazsa sorun değil */ }
});

app.Run();

// wwwroot klasörünü içeren proje kökünü bulur (bat dosyası BOOKHUB_ROOT ile de verebilir)
static string FindRoot()
{
    if (Environment.GetEnvironmentVariable("BOOKHUB_ROOT") is { Length: > 0 } env && Directory.Exists(Path.Combine(env, "wwwroot")))
        return Path.GetFullPath(env);
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "wwwroot")) && Directory.Exists(Path.Combine(dir.FullName, "database")))
                return dir.FullName;
    throw new DirectoryNotFoundException("wwwroot klasörü bulunamadı. BookHub.bat dosyasını proje klasöründen çalıştırın.");
}

public record CategoryInput(string? Name, string? Group);
