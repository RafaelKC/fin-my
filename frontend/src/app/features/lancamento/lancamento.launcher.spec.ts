import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed } from '@angular/core/testing';
import { LancamentoLauncher } from './lancamento.launcher';

describe('LancamentoLauncher', () => {
  function configurar(desktop: boolean) {
    vi.stubGlobal('matchMedia', (consulta: string) => ({
      matches: desktop && consulta.includes('1024'),
      media: consulta,
    }));
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    return TestBed.inject(LancamentoLauncher);
  }
  afterEach(() => {
    vi.unstubAllGlobals();
    TestBed.inject(HttpTestingController).match(() => true);
    localStorage.clear();
  });

  it('no celular abre em tela cheia', () => {
    const ref = configurar(false).abrir();
    expect(ref.componentRef).toBeDefined();
    const painel = TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelector('.lancamento-tela-cheia');
    expect(painel).not.toBeNull();
    ref.close();
  });

  it('no computador abre como diálogo, sem a classe de tela cheia', () => {
    const ref = configurar(true).abrir();
    const overlay = TestBed.inject(OverlayContainer).getContainerElement();
    expect(overlay.querySelector('app-lancamento-sheet')).not.toBeNull();
    expect(overlay.querySelector('.lancamento-tela-cheia')).toBeNull();
    ref.close();
  });

  it('chamar abrir() com o formulário aberto reaproveita o mesmo', () => {
    const launcher = configurar(true);
    const ref = launcher.abrir();
    expect(launcher.abrir()).toBe(ref);
    ref.close();
  });
});
