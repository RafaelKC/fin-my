// Aplica o tema (claro/escuro/sistema) antes da primeira pintura, sem piscar.
// Arquivo externo (não inline) por causa da CSP `script-src 'self'` do nginx.
(function () {
  var html = document.documentElement;
  try {
    var theme = localStorage.getItem('theme');
    var prefersDark =
      window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    var isSystem = theme === null || theme === 'system';
    var isDark = theme === 'dark' || (isSystem && prefersDark);
    html.classList.toggle('dark', isDark);
    html.setAttribute('data-theme', theme || 'system');
  } catch (_) {
    // armazenamento indisponível: segue no tema claro
  }
})();
