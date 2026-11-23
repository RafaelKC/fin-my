import { expect, test } from '@playwright/test';

// Passo 8 do roteiro (instalar como PWA) na medida do possível sem um celular: o navegador só
// oferece "Adicionar à tela inicial" com manifest válido (ícones 192/512, `standalone`) e service
// worker registrado. O build de produção (servido pelo `web` do compose) é o que registra o worker;
// o servidor de desenvolvimento do Angular não, então estes testes exigem `E2E_BASE_URL`.
test.skip(!process.env['E2E_BASE_URL'], 'precisa do build de produção (defina E2E_BASE_URL)');

// A configuração global bloqueia service workers (eles escapam do `page.route` dos outros e2e).
test.use({ serviceWorkers: 'allow' });

test('manifest instalável: link no HTML, modo standalone e ícones 192 e 512 alcançáveis', async ({
  page,
  request,
}) => {
  await page.goto('/entrar');

  const href = await page.locator('link[rel="manifest"]').getAttribute('href');
  expect(href).toBeTruthy();
  const url = new URL(href!, page.url()).toString();
  const resposta = await request.get(url);
  expect(resposta.ok()).toBe(true);

  const manifest = (await resposta.json()) as {
    name: string;
    display: string;
    start_url: string;
    icons: { src: string; sizes: string; type: string }[];
  };
  expect(manifest.name).toBe('Fin-My');
  expect(manifest.display).toBe('standalone');
  expect(manifest.start_url).toBeTruthy();

  const tamanhos = manifest.icons.map((icone) => icone.sizes);
  expect(tamanhos).toEqual(expect.arrayContaining(['192x192', '512x512']));
  for (const icone of manifest.icons.filter((i) => ['192x192', '512x512'].includes(i.sizes))) {
    const png = await request.get(new URL(icone.src, url).toString());
    expect(png.ok()).toBe(true);
    expect(png.headers()['content-type']).toContain('image/png');
  }

  await expect(page.locator('meta[name="theme-color"]')).toHaveAttribute('content', /^#/);
});

test('o service worker é registrado e guarda só o app shell, nunca dados da API', async ({
  page,
}) => {
  test.setTimeout(90_000);
  // Sem sessão: o app tenta o refresh e cai em /entrar; a chamada passa pelo SW de verdade.
  await page.goto('/entrar');
  await expect(page.getByRole('heading', { level: 1, name: 'Entrar' })).toBeVisible();

  // O Angular registra o worker quando o app estabiliza (`registerWhenStable`).
  const escopo = await page.evaluate(async () => {
    const registro = await navigator.serviceWorker.ready;
    return registro.scope;
  });
  expect(escopo).toBe(new URL('/', page.url()).toString());

  // Uma chamada à API depois do worker ativo (constituição: dados nunca ficam em cache offline).
  await page.evaluate(() => fetch('/api/v1/auth/refresh', { method: 'POST' }));

  const urlsEmCache = async () =>
    page.evaluate(async () => {
      const urls: string[] = [];
      for (const nome of await caches.keys()) {
        const cache = await caches.open(nome);
        for (const req of await cache.keys()) urls.push(req.url);
      }
      return urls;
    });

  // O worker faz o pré-cache do app shell (index e scripts) logo após instalar.
  await expect
    .poll(async () => (await urlsEmCache()).some((u) => u.endsWith('/index.html')))
    .toBe(true);
  expect((await urlsEmCache()).some((u) => u.includes('/api/'))).toBe(false);
});
