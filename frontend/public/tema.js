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
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', isDark ? '#151519' : '#f9f9fb');
    // Cor de destaque (Perfil › Aparência): matiz dos tokens antes da primeira pintura (AcentoService).
    var matizes = { indigo: 277, azul: 250, turquesa: 195, verde: 150, ambar: 75, laranja: 45, rosa: 355, roxo: 305 };
    var acento = localStorage.getItem('finmy.acento');
    if (acento && matizes[acento]) html.style.setProperty('--acento-h', String(matizes[acento]));
  } catch (_) {
    // armazenamento indisponível: segue no tema claro
  }
})();
