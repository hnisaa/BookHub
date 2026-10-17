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
    return `<a class="book-card" href="/kitap.html?id=${book.id}">
        <div class="thumb">${coverHTML(book)}<div class="tags">${tags}</div>${book.stock <= 0 ? '<div class="soldout">Tükendi</div>' : ""}</div>
        <div><h3>${esc(book.title)}</h3><div class="author">${esc(book.author)}</div></div>
        <div class="row"><span class="price">${formatPrice(book.price)}</span>${book.categoryName ? `<span class="muted" style="font-size:.78rem">${esc(book.categoryName)}</span>` : ""}</div>
      </a>`;
  }

  // ------------------------------------------------------------ oturum
  let currentUser = null;
  const ready = api("/auth/me").then((u) => (currentUser = u)).catch(() => null);

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

  // ------------------------------------------------------------ ortak üst / alt kısım
  const ICONS = {
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
    if (user) links.push(["/siparislerim.html", "Siparişlerim"]);
    const nav = links.map(([href, label, c]) => {
      const base = href.split("?")[0];
      return `<a href="${href}" class="${isActive(base, c === undefined ? undefined : c) ? "active" : ""}">${label}</a>`;
    }).join("") + (user && user.role === "admin" ? `<a href="/admin.html" class="admin-link ${path === "/admin.html" ? "active" : ""}">Yönetim Paneli</a>` : "");

    const userArea = user
      ? `<div class="user-chip"><span class="name">${esc(user.fullName)}</span><span class="role ${user.role === "admin" ? "admin" : ""}">${user.role === "admin" ? "Yönetici" : "Müşteri"}</span><button id="logout-btn" type="button">Çıkış</button></div>`
      : `<a class="btn btn-sm" href="/giris.html">Giriş Yap</a>`;

    const header = document.createElement("header");
    header.className = "site-header";
    header.innerHTML = `<div class="wrap">
        <a class="logo" href="/"><span class="logo-mark">${ICONS.book}</span><span>Book<b>Hub</b></span></a>
        <nav class="nav" id="main-nav">${nav}</nav>
        <div class="header-actions">
          <a class="icon-btn" href="/sepet.html" aria-label="Sepet">${ICONS.cart}<span class="badge-count hidden" id="cart-count">0</span></a>
          ${userArea}
          <button class="icon-btn menu-toggle" id="menu-toggle" aria-label="Menü">${ICONS.menu}</button>
        </div>
      </div>`;
    document.body.prepend(header);
    $("#menu-toggle").addEventListener("click", () => $("#main-nav").classList.toggle("open"));
    const lo = $("#logout-btn");
    if (lo) lo.addEventListener("click", logout);
    updateCartBadge();
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

  ready.then((user) => { renderHeader(user); renderFooter(); });

  window.BH = {
    api, $, $$, esc, toast, qs, formatPrice, formatDate, conditionLabel,
    coverHTML, createBook3D, makeRotatable, bookCardHTML,
    ready, requireUser, user: () => currentUser,
    getCart, saveCart, addToCart, cartCount,
  };
})();
