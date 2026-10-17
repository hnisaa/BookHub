/* Yönetim paneli: özet, kitap CRUD, sipariş durumu, kategoriler */
(async function () {
  "use strict";
  const { api, $, $$, esc, toast, formatPrice, formatDate, conditionLabel, coverHTML, createBook3D, makeRotatable } = window.BH;
  if (!(await BH.requireUser("admin"))) return;

  const STATUS = { hazirlaniyor: "Hazırlanıyor", kargoda: "Kargoda", teslim_edildi: "Teslim edildi", iptal: "İptal" };
  let books = [], categories = [], editingId = null;

  // ---------------------------------------------------------- sekmeler
  function show(tab) {
    $$("#tabs button").forEach((b) => b.classList.toggle("on", b.dataset.tab === tab));
    $$("[data-pane]").forEach((p) => p.classList.toggle("hidden", p.dataset.pane !== tab));
    if (tab === "ozet") loadStats();
    if (tab === "siparisler") loadOrders();
    if (tab === "ekle") updatePreview();
  }
  $("#tabs").addEventListener("click", (e) => {
    const b = e.target.closest("button"); if (!b) return;
    if (b.dataset.tab === "ekle" && editingId === null) resetForm();
    show(b.dataset.tab);
  });

  // ---------------------------------------------------------- özet
  async function loadStats() {
    const s = await api("/stats");
    const items = [
      ["Toplam kitap", s.books], ["Sıfır", s.newBooks], ["İkinci el", s.usedBooks],
      ["Azalan stok (≤2)", s.lowStock], ["Sipariş", s.orders], ["Ciro", formatPrice(s.revenue)], ["Kullanıcı", s.users],
    ];
    $("#kpis").innerHTML = items.map(([k, v]) => `<div class="panel kpi"><span>${k}</span><strong>${v}</strong></div>`).join("");
  }

  // ---------------------------------------------------------- kategoriler
  async function loadCategories() {
    categories = await api("/categories");
    $("#f-category").innerHTML = '<option value="">— Seçiniz —</option>' + categories.map((c) => `<option value="${c.id}">${esc(c.name)}</option>`).join("");
    $("#cats").innerHTML = categories.map((c) => `<span class="chip">${esc(c.name)}<span>${c.count}</span></span>`).join("");
  }
  $("#cat-form").addEventListener("submit", async (e) => {
    e.preventDefault();
    try {
      await api("/categories", { method: "POST", body: { name: $("#cat-name").value } });
      $("#cat-name").value = "";
      toast("Kategori eklendi.");
      loadCategories();
    } catch (err) { toast(err.message, "error"); }
  });

  // ---------------------------------------------------------- kitap listesi
  async function loadBooks() {
    books = await api("/books");
    renderRows();
  }
  function renderRows() {
    const f = $("#filter").value.trim().toLocaleLowerCase("tr");
    const list = f ? books.filter((b) => (b.title + " " + b.author).toLocaleLowerCase("tr").includes(f)) : books;
    $("#book-rows").innerHTML = list.map((b) => `
      <tr data-id="${b.id}">
        <td><div class="mini-cover">${coverHTML(b)}</div></td>
        <td><div class="t-title">${esc(b.title)}</div><div class="t-sub">${esc(b.author)}</div></td>
        <td><span class="tag ${b.condition === "ikinci_el" ? "tag-used" : "tag-new"}">${conditionLabel(b.condition)}</span></td>
        <td>${esc(b.categoryName || "—")}</td>
        <td>${formatPrice(b.price)}</td>
        <td class="${b.stock <= 2 ? "low" : ""}">${b.stock}</td>
        <td>${b.has3D ? "✓" : "–"}${b.featured ? " ★" : ""}</td>
        <td><div class="row-actions">
          <a class="btn btn-ghost btn-sm" href="/kitap.html?id=${b.id}" target="_blank">Gör</a>
          <button class="btn btn-ghost btn-sm" data-a="edit">Düzenle</button>
          <button class="btn btn-danger btn-sm" data-a="del">Sil</button>
        </div></td>
      </tr>`).join("") || `<tr><td colspan="8" class="empty">Kitap bulunamadı.</td></tr>`;
  }
  $("#filter").addEventListener("input", renderRows);
  $("#new-book").addEventListener("click", () => { resetForm(); show("ekle"); });
  $("#book-rows").addEventListener("click", async (e) => {
    const btn = e.target.closest("button[data-a]"); if (!btn) return;
    const id = Number(btn.closest("tr").dataset.id);
    const book = books.find((b) => b.id === id);
    if (btn.dataset.a === "edit") { fillForm(book); show("ekle"); }
    if (btn.dataset.a === "del") {
      if (!confirm(`"${book.title}" kalıcı olarak silinsin mi?`)) return;
      try { await api("/books/" + id, { method: "DELETE" }); toast("Kitap silindi."); loadBooks(); loadCategories(); }
      catch (err) { toast(err.message, "error"); }
    }
  });

  // ---------------------------------------------------------- form + canlı 3B önizleme
  const form = $("#book-form");
  let previewStage = $("#preview-stage");
  let rotator = null, lastRot = { x: -10, y: -32 };

  function readForm() {
    const fd = new FormData(form);
    const num = (k) => (fd.get(k) === "" ? null : Number(fd.get(k)));
    return {
      title: fd.get("title").trim(), author: fd.get("author").trim(), publisher: fd.get("publisher").trim() || null,
      isbn: fd.get("isbn").trim() || null, categoryId: num("categoryId"), condition: fd.get("condition"),
      conditionNote: fd.get("conditionNote").trim() || null, price: num("price"), stock: num("stock") ?? 0,
      pages: num("pages"), year: num("year"), description: fd.get("description").trim() || null,
      coverColor: fd.get("coverColor"), textColor: fd.get("textColor"), coverImage: fd.get("coverImage") || null,
      has3D: form.elements.has3D.checked, featured: form.elements.featured.checked,
    };
  }

  function updatePreview() {
    const b = readForm();
    $("#note-field").classList.toggle("hidden", b.condition !== "ikinci_el");
    $("#clear-img").classList.toggle("hidden", !b.coverImage);
    if (rotator) lastRot = rotator.get();
    // sahneyi yenisiyle değiştir ki eski sürükleme dinleyicileri birikmesin
    const stage = previewStage.cloneNode(false);
    previewStage.replaceWith(stage);
    previewStage = stage;
    const el = createBook3D({ ...b, title: b.title || "Kitap Adı", author: b.author || "Yazar" }, 200);
    stage.appendChild(el);
    rotator = makeRotatable(el, lastRot);
  }
  let previewTimer;
  form.addEventListener("input", () => { clearTimeout(previewTimer); previewTimer = setTimeout(updatePreview, 120); });
  form.addEventListener("change", updatePreview);

  function resetForm() {
    editingId = null;
    form.reset();
    form.elements.coverImage.value = "";
    $("#f-file").value = "";
    $("#form-title").textContent = "Yeni Kitap";
    $("#tab-ekle").textContent = "Kitap Ekle";
    updatePreview();
  }

  function fillForm(b) {
    editingId = b.id;
    const set = (name, v) => (form.elements[name].value = v ?? "");
    set("title", b.title); set("author", b.author); set("publisher", b.publisher); set("isbn", b.isbn);
    set("categoryId", b.categoryId); set("condition", b.condition); set("conditionNote", b.conditionNote);
    set("price", b.price); set("stock", b.stock); set("pages", b.pages); set("year", b.year);
    set("description", b.description); set("coverColor", b.coverColor); set("textColor", b.textColor); set("coverImage", b.coverImage);
    form.elements.has3D.checked = b.has3D; form.elements.featured.checked = b.featured;
    $("#f-file").value = "";
    $("#form-title").textContent = "Düzenle: " + b.title;
    $("#tab-ekle").textContent = "Kitabı Düzenle";
    updatePreview();
  }

  $("#f-file").addEventListener("change", async (e) => {
    const file = e.target.files[0]; if (!file) return;
    const fd = new FormData(); fd.append("file", file);
    try {
      const { url } = await api("/upload", { method: "POST", body: fd });
      form.elements.coverImage.value = url;
      updatePreview();
      toast("Kapak görseli yüklendi.");
    } catch (err) { toast(err.message, "error"); e.target.value = ""; }
  });
  $("#clear-img").addEventListener("click", () => { form.elements.coverImage.value = ""; $("#f-file").value = ""; updatePreview(); });

  $("#cancel").addEventListener("click", () => { resetForm(); show("kitaplar"); });

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const data = readForm();
    if (!data.title || !data.author || data.price === null) { toast("Kitap adı, yazar ve fiyat zorunludur.", "error"); return; }
    $("#save").disabled = true;
    try {
      if (editingId) await api("/books/" + editingId, { method: "PUT", body: data });
      else await api("/books", { method: "POST", body: data });
      toast(editingId ? "Kitap güncellendi." : "Kitap eklendi.");
      resetForm();
      await Promise.all([loadBooks(), loadCategories()]);
      show("kitaplar");
    } catch (err) { toast(err.message, "error"); }
    finally { $("#save").disabled = false; }
  });

  // ---------------------------------------------------------- siparişler
  async function loadOrders() {
    const orders = await api("/orders");
    $("#orders").innerHTML = orders.length ? orders.map((o) => `
      <div class="panel order" data-id="${o.id}">
        <div class="order-head">
          <div><strong>Sipariş #${o.id}</strong> · ${esc(o.username)}<div class="muted">${formatDate(o.createdAt)}</div></div>
          <select class="input" style="width:auto" aria-label="Durum">${Object.entries(STATUS).map(([k, v]) => `<option value="${k}" ${k === o.status ? "selected" : ""}>${v}</option>`).join("")}</select>
        </div>
        <ul>${o.items.map((i) => `<li><span>${esc(i.title)} × ${i.quantity}</span><span>${formatPrice(i.unitPrice * i.quantity)}</span></li>`).join("")}</ul>
        <div class="order-foot"><span class="muted">${esc(o.address || "")}</span><strong>${formatPrice(o.total)}</strong></div>
      </div>`).join("") : '<div class="panel empty"><h3>Henüz sipariş yok</h3></div>';
  }
  $("#orders").addEventListener("change", async (e) => {
    if (e.target.tagName !== "SELECT") return;
    const id = e.target.closest(".order").dataset.id;
    try { await api(`/orders/${id}/status`, { method: "PUT", body: { status: e.target.value } }); toast("Sipariş durumu güncellendi."); }
    catch (err) { toast(err.message, "error"); }
  });

  // ---------------------------------------------------------- başlangıç
  await Promise.all([loadCategories(), loadBooks(), loadStats()]);
})();
