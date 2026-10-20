/* =====================================================================
   BookHub – ortak JS (saf Vanilla JS, kütüphane yok)
   Her sayfa önce bu dosyayı yükler; window.BH altında yardımcılar sunar.
   ===================================================================== */
(function () {
  "use strict";

  // ------------------------------------------------------------ API
  async function api(path, options = {}) {
    const opts = { credentials: "same-origin", ...options, headers: { ...(options.headers || {}) } };
    if (opts.body && !(opts.body instanceof FormData) && typeof opts.body !== "string") {
      opts.headers["Content-Type"] = "application/json";
      opts.body = JSON.stringify(opts.body);
    }
    const res = await fetch("/api" + path, opts);
    const text = await res.text();
    const data = text ? JSON.parse(text) : null;
    if (!res.ok) throw new Error((data && data.error) || "Bir hata oluştu (" + res.status + ").");
    return data;
  }

  // ------------------------------------------------------------ yardımcılar
  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));

  function esc(value) {
    return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }

  const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY" });
  const formatPrice = (n) => money.format(Number(n) || 0);
  const formatDate = (iso) => new Date(iso).toLocaleString("tr-TR", { dateStyle: "medium", timeStyle: "short" });
  const conditionLabel = (c) => (c === "ikinci_el" ? "2. El" : "Sıfır");
  const qs = (name) => new URLSearchParams(location.search).get(name);

  function toast(message, type = "info") {
    let host = $(".toast-host");
    if (!host) { host = document.createElement("div"); host.className = "toast-host"; document.body.appendChild(host); }
    const el = document.createElement("div");
    el.className = "toast" + (type === "error" ? " error" : "");
    el.textContent = message;
    host.appendChild(el);
    setTimeout(() => el.remove(), 3200);
  }

  // ------------------------------------------------------------ kapak & 3B kitap
  function coverHTML(book) {
    const style = `--cover:${esc(book.coverColor)};--text:${esc(book.textColor)}`;
    if (book.coverImage) {
      return `<div class="cover has-img" style="${style}"><img src="${esc(book.coverImage)}" alt="${esc(book.title)} kapağı" loading="lazy"></div>`;
    }
    return `<div class="cover" style="${style}">
        <span class="frame"></span>
        <div><div class="c-title">${esc(book.title)}</div><div class="c-orn"></div></div>
        <div><div class="c-author">${esc(book.author)}</div></div>
        <div class="c-mark">BookHub</div>
      </div>`;
  }

  /** Bir kitap için CSS ile 3B kutu oluşturur. width px; yükseklik 1.5x, kalınlık sayfa sayısına göre. */
  function createBook3D(book, width = 200) {
    const h = Math.round(width * 1.5);
    const pages = book.pages || 250;
    const d = Math.round(Math.min(Math.max(pages / 14, 14), 60) * (width / 200));
    const el = document.createElement("div");
    el.className = "book3d";
    el.style.cssText = `--w:${width}px;--h:${h}px;--d:${d}px;--cover:${book.coverColor};--text:${book.textColor}`;
    el.innerHTML = `
      <div class="f-front">${coverHTML(book)}</div>
      <div class="f-back"></div>
      <div class="f-spine"><span>${esc(book.title)}</span></div>
      <div class="f-pages"></div>
      <div class="f-top"></div>
      <div class="f-bottom"></div>
      <div class="shadow"></div>`;
    return el;
  }

  /** Fare / dokunmatik ile sürükleyerek döndürme. */
  function makeRotatable(el, start = { x: -8, y: -30 }) {
    let rx = start.x, ry = start.y, dragging = false, px = 0, py = 0;
    const apply = () => { el.style.setProperty("--rx", rx + "deg"); el.style.setProperty("--ry", ry + "deg"); };
    apply();
    const target = el.parentElement || el;
    target.style.touchAction = "none";
    target.style.cursor = "grab";
    target.addEventListener("pointerdown", (e) => {
      dragging = true; px = e.clientX; py = e.clientY;
      el.classList.add("dragging"); target.style.cursor = "grabbing";
      target.setPointerCapture(e.pointerId);
    });
    target.addEventListener("pointermove", (e) => {
      if (!dragging) return;
      ry += (e.clientX - px) * 0.5;
      rx = Math.max(-60, Math.min(60, rx - (e.clientY - py) * 0.4));
      px = e.clientX; py = e.clientY;
      apply();
    });
    const end = () => { dragging = false; el.classList.remove("dragging"); target.style.cursor = "grab"; };
    target.addEventListener("pointerup", end);
    target.addEventListener("pointercancel", end);
    return {
      set(x, y) { rx = x; ry = y; apply(); },
      get() { return { x: rx, y: ry }; },
    };
  }

  function bookCardHTML(book) {
    const tags = [
      `<span class="tag ${book.condition === "ikinci_el" ? "tag-used" : "tag-new"}">${conditionLabel(book.condition)}</span>`,
      book.has3D ? `<span class="tag tag-3d">3B</span>` : "",
    ].join("");
    return `<div class="book-card">
        <a class="card-link" href="/kitap.html?id=${book.id}">
          <div class="thumb">${coverHTML(book)}<div class="tags">${tags}</div>${book.stock <= 0 ? '<div class="soldout">Tükendi</div>' : ""}</div>
          <div><h3>${esc(book.title)}</h3><div class="author">${esc(book.author)}</div></div>
          <div class="row"><span class="price">${formatPrice(book.price)}</span>${book.categoryName ? `<span class="muted" style="font-size:.78rem">${esc(book.categoryName)}</span>` : ""}</div>
        </a>
        ${favButtonHTML(book.id)}
      </div>`;
  }

  // ------------------------------------------------------------ oturum
  let currentUser = null;
  const favIds = new Set();
  const ready = api("/auth/me").then(async (u) => {
    currentUser = u;
    if (u) {
      try { (await api("/favorites")).forEach((b) => favIds.add(b.id)); } catch { /* favoriler yüklenemezse kalpler boş görünür */ }
    }
    return u;
  }).catch(() => null);

  async function logout() {
    await api("/auth/logout", { method: "POST" });
    location.href = "/";
  }

  /** Sayfayı yalnızca belirli kullanıcılara açar. role: "any" | "admin" */
  async function requireUser(role = "any") {
    const user = await ready;
    if (!user) { location.href = "/giris.html?r=" + encodeURIComponent(location.pathname + location.search); return null; }
    if (role === "admin" && user.role !== "admin") { location.href = "/"; return null; }
    return user;
  }

  // ------------------------------------------------------------ sepet (tarayıcıda saklanır)
  const CART_KEY = "bookhub_cart";
  function getCart() {
    try { return JSON.parse(localStorage.getItem(CART_KEY)) || []; } catch { return []; }
  }
  function saveCart(items) {
    try { localStorage.setItem(CART_KEY, JSON.stringify(items)); } catch { /* depolama kapalı olabilir */ }
    updateCartBadge();
  }
  function addToCart(book, qty = 1) {
    const cart = getCart();
    const line = cart.find((i) => i.bookId === book.id);
    const next = (line ? line.quantity : 0) + qty;
    if (next > book.stock) { toast(`Stokta yalnızca ${book.stock} adet var.`, "error"); return false; }
    if (line) line.quantity = next; else cart.push({ bookId: book.id, quantity: qty });
    saveCart(cart);
    toast(`"${book.title}" sepete eklendi.`);
    return true;
  }
  const cartCount = () => getCart().reduce((n, i) => n + i.quantity, 0);
  function updateCartBadge() {
    const badge = $("#cart-count");
    if (!badge) return;
    const n = cartCount();
    badge.textContent = n;
    badge.classList.toggle("hidden", n === 0);
  }

  // ------------------------------------------------------------ favoriler (veritabanında, kullanıcıya özel)
  const favLabel = (on) => (on ? "Favorilerden çıkar" : "Favorilere ekle");

  /** Kalp butonu. variant "card" kapağın üstünde yuvarlak, "inline" yazılı buton. */
  function favButtonHTML(id, variant = "card") {
    const on = favIds.has(id);
    const cls = variant === "inline" ? "btn btn-ghost fav-inline" : "fav-btn";
    const label = variant === "inline" ? `<span class="fav-label">${on ? "Favorilerde" : "Favorilere ekle"}</span>` : "";
    return `<button type="button" class="${cls}${on ? " on" : ""}" data-fav="${id}" aria-pressed="${on}" aria-label="${favLabel(on)}" title="${favLabel(on)}">${ICONS.heart}${label}</button>`;
  }

  function syncFavButtons() {
    $$("[data-fav]").forEach((b) => {
      const on = favIds.has(Number(b.dataset.fav));
      b.classList.toggle("on", on);
      b.setAttribute("aria-pressed", on);
      b.setAttribute("aria-label", favLabel(on));
      b.title = favLabel(on);
      const label = b.querySelector(".fav-label");
      if (label) label.textContent = on ? "Favorilerde" : "Favorilere ekle";
    });
  }

  async function toggleFavorite(id) {
    if (!currentUser) {
      toast("Favorilere eklemek için giriş yapın.");
      setTimeout(() => (location.href = "/giris.html?r=" + encodeURIComponent(location.pathname + location.search)), 900);
      return;
    }
    const add = !favIds.has(id);
    if (add) favIds.add(id); else favIds.delete(id);
    syncFavButtons();
    try {
      await api("/favorites/" + id, { method: add ? "POST" : "DELETE" });
      toast(add ? "Favorilere eklendi." : "Favorilerden çıkarıldı.");
      document.dispatchEvent(new CustomEvent("bh:favorites", { detail: { id, add } }));
    } catch (err) {
      if (add) favIds.delete(id); else favIds.add(id);
      syncFavButtons();
      toast(err.message, "error");
    }
  }

  document.addEventListener("click", (e) => {
    const btn = e.target.closest("[data-fav]");
    if (!btn) return;
    e.preventDefault();
    toggleFavorite(Number(btn.dataset.fav));
  });

  // ------------------------------------------------------------ kategoriler (gruplu açılır menü)
  const GROUP_ORDER = ["Edebiyat", "Düşünce & Bilim", "Çocuk & Gençlik", "Yaşam & Sanat"];
  let categoriesPromise = null;
  const loadCategories = () => (categoriesPromise ??= api("/categories"));

  /** Kategorileri ana gruplara ayırır; bilinen gruplar sabit sırada, diğerleri alfabetik, "Diğer" en sonda. */
  function groupCategories(cats) {
    const map = new Map();
    for (const c of cats) {
      const g = c.group || "Diğer";
      if (!map.has(g)) map.set(g, []);
      map.get(g).push(c);
    }
    const rank = (g) => (g === "Diğer" ? 999 : GROUP_ORDER.includes(g) ? GROUP_ORDER.indexOf(g) : 100);
    return [...map.entries()]
      .sort(([a], [b]) => rank(a) - rank(b) || a.localeCompare(b, "tr"))
      .map(([name, items]) => ({
        name,
        items: items.sort((a, b) => a.name.localeCompare(b.name, "tr")),
        count: items.reduce((n, c) => n + c.count, 0),
      }));
  }

  /** <details> tabanlı açılır kategori menüsü. activeId'nin grubu açık gelir; yoksa openFirst ile ilk grup,
      openAll ile tüm gruplar açılır. */
  function categoryMenuHTML(cats, { activeId = null, openFirst = false, openAll = false } = {}) {
    return groupCategories(cats).map((g, i) => {
      const open = openAll || g.items.some((c) => c.id === activeId) || (openFirst && activeId === null && i === 0);
      return `<details class="cat-group"${open ? " open" : ""}>
          <summary><span class="cat-group-name">${esc(g.name)}</span><span class="cat-count">${g.count}</span>${ICONS.chevron}</summary>
          <ul>${g.items.map((c) => `<li><a href="/kitaplar.html?kategori=${c.id}" data-cat="${c.id}"${c.id === activeId ? ' class="active" aria-current="page"' : ""}>${esc(c.name)}<span>${c.count}</span></a></li>`).join("")}</ul>
        </details>`;
    }).join("");
  }

  // Soldan açılan çekmece (her sayfada üst menüdeki "Kategoriler" butonuyla açılır)
  function setupDrawer() {
    const overlay = document.createElement("div");
    overlay.className = "drawer-overlay";
    const drawer = document.createElement("aside");
    drawer.className = "drawer";
    drawer.id = "cat-drawer";
    drawer.setAttribute("aria-label", "Kategoriler");
    drawer.setAttribute("aria-hidden", "true");
    drawer.inert = true;
    drawer.innerHTML = `
      <div class="drawer-head"><h3>Kategoriler</h3><button class="icon-btn" type="button" data-close-drawer aria-label="Kapat">${ICONS.close}</button></div>
      <nav class="drawer-quick">
        <a href="/kitaplar.html">Tüm Kitaplar</a>
        <a href="/kitaplar.html?durum=sifir">Sıfır</a>
        <a href="/kitaplar.html?durum=ikinci_el">İkinci El</a>
      </nav>
      <div class="drawer-body"><div class="muted" style="padding:12px 4px">Yükleniyor…</div></div>`;
    document.body.append(overlay, drawer);

    let loaded = false, lastFocus = null;
    async function open() {
      lastFocus = document.activeElement;
      drawer.inert = false;
      drawer.setAttribute("aria-hidden", "false");
      drawer.classList.add("open");
      overlay.classList.add("show");
      document.body.classList.add("no-scroll");
      $("#cat-toggle")?.setAttribute("aria-expanded", "true");
      $("#main-nav")?.classList.remove("open");
      drawer.querySelector("[data-close-drawer]").focus();
      if (!loaded) {
        try {
          const active = location.pathname === "/kitaplar.html" ? Number(qs("kategori")) || null : null;
          drawer.querySelector(".drawer-body").innerHTML = categoryMenuHTML(await loadCategories(), { activeId: active });
          loaded = true;
        } catch (err) {
          drawer.querySelector(".drawer-body").innerHTML = `<div class="muted" style="padding:12px 4px">${esc(err.message)}</div>`;
        }
      }
    }
    function close() {
      drawer.classList.remove("open");
      overlay.classList.remove("show");
      drawer.setAttribute("aria-hidden", "true");
      drawer.inert = true;
      document.body.classList.remove("no-scroll");
      $("#cat-toggle")?.setAttribute("aria-expanded", "false");
      if (lastFocus && lastFocus.focus) lastFocus.focus();
    }
    overlay.addEventListener("click", close);
    drawer.querySelector("[data-close-drawer]").addEventListener("click", close);
    document.addEventListener("keydown", (e) => { if (e.key === "Escape" && drawer.classList.contains("open")) close(); });
    document.addEventListener("click", (e) => {
      const opener = e.target.closest("[data-open-cats]");
      if (opener) { e.preventDefault(); open(); }
    });
    // Katalog sayfasında aynı sayfa içi seçimlerde çekmeceyi kapat
    drawer.addEventListener("click", (e) => { if (e.target.closest("a")) setTimeout(close, 0); });
    return { open, close };
  }

  // Yukarı çık butonu
  function setupBackToTop() {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "to-top";
    btn.setAttribute("aria-label", "Sayfanın başına dön");
    btn.title = "Yukarı çık";
    btn.innerHTML = ICONS.up;
    document.body.appendChild(btn);
    const update = () => btn.classList.toggle("show", window.scrollY > 500);
    window.addEventListener("scroll", update, { passive: true });
    update();
    btn.addEventListener("click", () => {
      const smooth = !window.matchMedia("(prefers-reduced-motion: reduce)").matches;
      window.scrollTo({ top: 0, behavior: smooth ? "smooth" : "auto" });
    });
  }

  // ------------------------------------------------------------ ortak üst / alt kısım
  const ICONS = {
    grid: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6h16M4 12h10M4 18h13"/></svg>',
    chevron: '<svg class="chev" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m9 6 6 6-6 6"/></svg>',
    close: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M6 6l12 12M18 6 6 18"/></svg>',
    up: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 19V5M5 12l7-7 7 7"/></svg>',
    heart: '<svg viewBox="0 0 24 24" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"/></svg>',
    sun: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4"/><path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4"/></svg>',
    moon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg>',
    book: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4 19.5V5a2 2 0 0 1 2-2h13v16H6.5A2.5 2.5 0 0 0 4 21.5 2.5 2.5 0 0 0 6.5 24H19"/><path d="M8 7h7"/></svg>',
    cart: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="20" r="1.4"/><circle cx="18" cy="20" r="1.4"/><path d="M2 3h3l2.6 12.2a2 2 0 0 0 2 1.6h8.2a2 2 0 0 0 2-1.5L21.5 8H6"/></svg>',
    menu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 7h16M4 12h16M4 17h16"/></svg>',
  };

  function renderHeader(user) {
    const path = location.pathname;
    const cond = qs("durum");
    const isActive = (href, c) => {
      if (href === "/") return path === "/" || path === "/index.html";
      return path === href && (c === undefined || c === cond);
    };
    const links = [
      ["/", "Anasayfa"],
      ["/kitaplar.html", "Tüm Kitaplar", null],
      ["/kitaplar.html?durum=sifir", "Sıfır", "sifir"],
      ["/kitaplar.html?durum=ikinci_el", "İkinci El", "ikinci_el"],
    ];
    if (user) links.push(["/favoriler.html", "Favorilerim"], ["/siparislerim.html", "Siparişlerim"]);
    const nav = links.map(([href, label, c]) => {
      const base = href.split("?")[0];
      return `<a href="${href}" class="${isActive(base, c === undefined ? undefined : c) ? "active" : ""}">${label}</a>`;
    }).join("") + (user && user.role === "admin" ? `<a href="/admin.html" class="admin-link ${path === "/admin.html" ? "active" : ""}">Yönetim Paneli</a>` : "")
      + `<a href="#" class="nav-cats" data-open-cats>Kategoriler</a>`;

    const userArea = user
      ? `<div class="user-chip"><span class="name">${esc(user.fullName)}</span><span class="role ${user.role === "admin" ? "admin" : ""}">${user.role === "admin" ? "Yönetici" : "Müşteri"}</span><button id="logout-btn" type="button">Çıkış</button></div>`
      : `<a class="btn btn-sm" href="/giris.html">Giriş Yap</a>`;

    const header = document.createElement("header");
    header.className = "site-header";
    header.innerHTML = `<div class="wrap">
        <a class="logo" href="/"><span class="logo-mark">${ICONS.book}</span><span>Book<b>Hub</b></span></a>
        <button class="cat-toggle" id="cat-toggle" type="button" data-open-cats aria-controls="cat-drawer" aria-expanded="false">${ICONS.grid}<span>Kategoriler</span></button>
        <nav class="nav" id="main-nav">${nav}</nav>
        <div class="header-actions">
          <button class="icon-btn" id="theme-toggle" type="button"></button>
          <a class="icon-btn" href="/sepet.html" aria-label="Sepet">${ICONS.cart}<span class="badge-count hidden" id="cart-count">0</span></a>
          ${userArea}
          <button class="icon-btn menu-toggle" id="menu-toggle" aria-label="Menü">${ICONS.menu}</button>
        </div>
      </div>`;
    document.body.prepend(header);
    $("#menu-toggle").addEventListener("click", () => $("#main-nav").classList.toggle("open"));
    const lo = $("#logout-btn");
    if (lo) lo.addEventListener("click", logout);
    setupThemeToggle($("#theme-toggle"));
    updateCartBadge();
  }

  function setupThemeToggle(btn) {
    const theme = window.BookHubTheme;
    if (!theme) { btn.remove(); return; }
    const paint = () => {
      const dark = theme.get() === "dark";
      btn.innerHTML = dark ? ICONS.sun : ICONS.moon;
      btn.setAttribute("aria-label", dark ? "Açık temaya geç" : "Koyu temaya geç");
      btn.title = btn.getAttribute("aria-label");
    };
    paint();
    btn.addEventListener("click", () => { theme.toggle(); paint(); });
  }

  function renderFooter() {
    const footer = document.createElement("footer");
    footer.className = "site-footer";
    footer.innerHTML = `<div class="wrap">
        <div class="cols">
          <div><h4>BookHub</h4><p>Sıfır ve ikinci el kitapların buluştuğu dijital kütüphane. Rafları 3 boyutlu gez, sevdiğin kitabı eve getir.</p></div>
          <div><h4>Keşfet</h4><ul><li><a href="/kitaplar.html?durum=sifir">Sıfır Kitaplar</a></li><li><a href="/kitaplar.html?durum=ikinci_el">İkinci El Kitaplar</a></li><li><a href="/kitaplar.html">Tüm Katalog</a></li></ul></div>
          <div><h4>Hesap</h4><ul><li><a href="/giris.html">Giriş Yap</a></li><li><a href="/sepet.html">Sepetim</a></li><li><a href="/siparislerim.html">Siparişlerim</a></li></ul></div>
        </div>
        <div class="copy">© ${new Date().getFullYear()} BookHub · Yerel geliştirme sürümü</div>
      </div>`;
    document.body.appendChild(footer);
  }

  ready.then((user) => { renderHeader(user); renderFooter(); setupDrawer(); setupBackToTop(); syncFavButtons(); });

  window.BH = {
    api, $, $$, esc, toast, qs, formatPrice, formatDate, conditionLabel,
    coverHTML, createBook3D, makeRotatable, bookCardHTML, favButtonHTML,
    loadCategories, groupCategories, categoryMenuHTML,
    ready, requireUser, user: () => currentUser,
    getCart, saveCart, addToCart, cartCount,
  };
})();
