// Applies the remembered theme before the first paint (a file, not inline, to satisfy the CSP).
(function () {
  try {
    var t = localStorage.getItem('noos-theme');
    if (t === 'light' || t === 'dark') document.documentElement.dataset.theme = t;
  } catch {
    /* storage blocked */
  }
})();
