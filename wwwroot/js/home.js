/* Anasayfa: 3B kitap halkası + vitrinler */
(async function () {
  "use strict";
  const { api, $, esc, createBook3D, bookCardHTML } = window.BH;

  const [books, categories] = await Promise.all([api("/books"), api("/categories")]);

  // ---------------------------------------------------------- istatistikler
  $("#s-new").textContent = books.filter((b) => b.condition === "sifir").length;
  $("#s-used").textContent = books.filter((b) => b.condition === "ikinci_el").length;
  $("#s-3d").textContent = books.filter((b) => b.has3D).length;
  $("#s-cat").textContent = categories.length;

  // ---------------------------------------------------------- vitrinler
  const fill = (el, list) => {
    el.innerHTML = list.length ? list.map(bookCardHTML).join("") : '<div class="empty">Henüz kitap yok.</div>';
  };
  fill($("#new-books"), books.filter((b) => b.condition === "sifir").slice(0, 6));
  fill($("#used-books"), books.filter((b) => b.condition === "ikinci_el").slice(0, 6));
  $("#cat-list").innerHTML = categories
    .map((c) => `<a class="cat-card" href="/kitaplar.html?kategori=${c.id}">${esc(c.name)}<span>${c.count} kitap</span></a>`)
    .join("");

  // ---------------------------------------------------------- 3B halka
  let featured = books.filter((b) => b.featured);
  if (featured.length < 5) featured = featured.concat(books.filter((b) => !b.featured)).slice(0, 8);
  if (!featured.length) return;

  const stage = $("#ring-stage");
  const ring = $("#ring");
  const caption = $("#hero-caption");
  const step = 360 / featured.length;

  // Halka boyutunu sahne genişliğine göre ayarla ki dar ekranlarda taşmasın.
  // Sayfa düzeni oturduktan sonra ve pencere boyutu değiştikçe yeniden kurulur.
  let builtWidth = 0;
  function layout() {
    const width = stage.clientWidth;
    if (!width || Math.abs(width - builtWidth) < 20) return;
    builtWidth = width;
    const radius = Math.round(Math.max(140, Math.min(width * 0.36, 260)));
    const bookW = Math.round(Math.max(70, Math.min(128, (2 * Math.PI * radius) / featured.length - 40)));
    ring.replaceChildren(...featured.map((book, i) => {
      const item = document.createElement("div");
      item.className = "ring-item";
      item.style.transform = `rotateY(${i * step}deg) translateZ(${radius}px)`;
      item.appendChild(createBook3D(book, bookW));
      item.dataset.id = book.id;
      return item;
    }));
    // Halkayı arkadan görünecek kadar geriye it ki ekrana sığsın
    ring.style.translate = `0 0 ${-radius * 0.55}px`;
  }
  layout();
  window.addEventListener("load", layout);
  window.addEventListener("resize", layout);

  let angle = 0, velocity = 0, dragging = false, moved = 0, lastX = 0, hover = false;
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function frontIndex() {
    const a = ((-angle % 360) + 360) % 360;
    return Math.round(a / step) % featured.length;
  }
  let lastFront = -1;
  function updateCaption() {
    const i = frontIndex();
    if (i === lastFront) return;
    lastFront = i;
    const b = featured[i];
    caption.innerHTML = `${esc(b.title)} <small>${esc(b.author)} · ${b.condition === "ikinci_el" ? "2. El" : "Sıfır"}</small>`;
  }

  function tick() {
    if (!dragging) {
      if (Math.abs(velocity) > 0.02) { angle += velocity; velocity *= 0.95; }
      else if (!hover && !reduced) angle -= 0.12;
    }
    ring.style.setProperty("--angle", angle + "deg");
    updateCaption();
    requestAnimationFrame(tick);
  }
  requestAnimationFrame(tick);

  stage.addEventListener("pointerenter", () => (hover = true));
  stage.addEventListener("pointerleave", () => (hover = false));
  stage.addEventListener("pointerdown", (e) => {
    dragging = true; moved = 0; lastX = e.clientX; velocity = 0;
    stage.setPointerCapture(e.pointerId);
  });
  stage.addEventListener("pointermove", (e) => {
    if (!dragging) return;
    const dx = e.clientX - lastX;
    lastX = e.clientX;
    moved += Math.abs(dx);
    angle += dx * 0.35;
    velocity = dx * 0.35;
  });
  const end = (e) => {
    if (!dragging) return;
    dragging = false;
    // Sürüklemeden tıklandıysa: tıklanan kitaba git
    if (moved < 6) {
      stage.releasePointerCapture(e.pointerId);
      const hit = document.elementFromPoint(e.clientX, e.clientY);
      const item = hit && hit.closest(".ring-item");
      if (item) location.href = "/kitap.html?id=" + item.dataset.id;
    }
  };
  stage.addEventListener("pointerup", end);
  stage.addEventListener("pointercancel", () => (dragging = false));
})();
