/* Tema (açık / koyu) – sayfa çizilmeden önce <head> içinde çalışır ki ekran yanıp sönmesin.
   Kullanıcının seçimi tarayıcıda saklanır; seçim yoksa bilgisayarın ayarı kullanılır. */
(function () {
  "use strict";
  var KEY = "bookhub_theme";
  var saved = null;
  try { saved = localStorage.getItem(KEY); } catch (e) { /* depolama kapalı olabilir */ }
  var system = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  document.documentElement.dataset.theme = saved === "dark" || saved === "light" ? saved : system;

  window.BookHubTheme = {
    get: function () { return document.documentElement.dataset.theme; },
    toggle: function () {
      var next = this.get() === "dark" ? "light" : "dark";
      document.documentElement.dataset.theme = next;
      try { localStorage.setItem(KEY, next); } catch (e) { /* yoksay */ }
      return next;
    },
  };
})();
