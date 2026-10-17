# BookHub

Sıfır ve ikinci el kitapların satıldığı, kitapların 3 boyutlu incelenebildiği dijital kütüphane.

## Çalıştırma

`BookHub.bat` dosyasına çift tıklayın. Bat dosyası:

1. Sunucuyu derleyip başlatır (ilk açılış 20-30 sn sürebilir),
2. `database/bookhub.db` yoksa veritabanını oluşturup örnek verileri ekler,
3. Tarayıcıda **http://localhost:5080** adresini açar.

Kapatmak için siyah pencerede `Ctrl+C`'ye basın ya da pencereyi kapatın.
`VeritabaniniAc.bat` dosyası veritabanını *DB Browser for SQLite* ile açar.

Gereksinim: .NET 10 SDK (bu bilgisayarda kurulu).

## Hesaplar

| Kullanıcı | Şifre  | Rol      | Yetkiler                                        |
|-----------|--------|----------|-------------------------------------------------|
| admin     | 123456 | Yönetici | Kitap ekle/düzenle/sil, kapak yükle, siparişleri yönet, kategori ekle |
| misafir   | 123456 | Müşteri  | Kitapları gez, 3B incele, sepete ekle, satın al |

Giriş yapmadan da kitaplar gezilebilir; satın almak için giriş gerekir.

## Klasör yapısı

```
BookHub/
├─ BookHub.bat            Tek tıkla başlatma
├─ VeritabaniniAc.bat     Veritabanını DB Browser ile açar
├─ database/
│  ├─ schema.sql          Tablo tanımları (her açılışta "yoksa oluştur")
│  └─ bookhub.db          SQLite veritabanı (otomatik oluşur)
├─ server/                ASP.NET Core API (C#)
│  ├─ Program.cs          Uç noktalar (endpoint) ve yetki kontrolleri
│  └─ Database.cs         Tüm SQL, şifreleme, örnek veriler
└─ wwwroot/               Site (saf HTML + CSS + Vanilla JS)
   ├─ index.html          Anasayfa – 3B dönen kitap rafı
   ├─ kitaplar.html       Katalog (Sıfır / İkinci El / kategori / arama)
   ├─ kitap.html          Kitap detayı – 3B görüntüleyici
   ├─ sepet.html          Sepet ve satın alma
   ├─ siparislerim.html   Kullanıcının siparişleri
   ├─ giris.html          Giriş
   ├─ admin.html          Yönetim paneli (yalnızca admin)
   ├─ css/  js/           Stiller ve scriptler
   └─ uploads/            Yüklenen kapak görselleri
```

## 3B kitaplar

3B görüntü kütüphane kullanılmadan **CSS 3D transform** ile çizilir (`js/app.js → createBook3D`).
Her kitabın kalınlığı sayfa sayısına göre hesaplanır. Yönetici her kitap için:

- **3B görüntüleyici açık** → detay sayfasında sürüklenerek döndürülebilir 3B kitap, kapalıysa düz kapak,
- **Anasayfa 3B rafında göster** → anasayfadaki dönen halkada yer alır.

Kapak görseli yüklenmezse kitap, seçilen renklerle otomatik bir kapak olarak çizilir.

## API

| Metot | Adres                      | Yetki     |
|-------|----------------------------|-----------|
| POST  | /api/auth/login            | herkes    |
| POST  | /api/auth/logout           | herkes    |
| GET   | /api/auth/me               | herkes    |
| GET   | /api/books?condition=sifir\|ikinci_el&category=&q=&sort= | herkes |
| GET   | /api/books/{id}            | herkes    |
| POST / PUT / DELETE | /api/books[/{id}] | admin |
| POST  | /api/upload                | admin     |
| GET / POST | /api/categories       | herkes / admin |
| POST  | /api/orders                | giriş yapmış |
| GET   | /api/orders                | kendi siparişleri (admin: hepsi) |
| PUT   | /api/orders/{id}/status    | admin     |
| GET   | /api/stats                 | admin     |

## İnternete taşırken

- Site ile API ayrı katmanlardır; `wwwroot` başka bir sunucuya taşınsa da aynı `/api` adreslerini kullanır.
- Veritabanı değişecekse (MySQL, PostgreSQL, SQL Server) yalnızca `server/Database.cs` ve `database/schema.sql`
  güncellenir. Şemadaki notlar tip karşılıklarını gösterir.
- Yayına almadan önce: `admin` / `misafir` şifrelerini değiştirin, HTTPS kullanın,
  giriş sayfasındaki "deneme hesapları" kutusunu kaldırın.
- Şifreler PBKDF2-SHA256 ile tuzlanarak saklanır; oturumlar `HttpOnly` çerez ile tutulur.
