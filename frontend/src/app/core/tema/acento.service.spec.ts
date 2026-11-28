import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { ACENTOS, AcentoService, CHAVE_ACENTO } from './acento.service';

describe('AcentoService', () => {
  beforeEach(() => localStorage.removeItem(CHAVE_ACENTO));

  it('sem escolha usa o índigo e põe o matiz e o ícone', () => {
    TestBed.inject(AcentoService);
    const doc = TestBed.inject(DOCUMENT);
    expect(doc.documentElement.style.getPropertyValue('--acento-h')).toBe('277');
    expect(doc.querySelector<HTMLLinkElement>('link[rel="icon"]')?.getAttribute('href')).toBe(
      'icons/acento-indigo.svg',
    );
  });

  it('escolher troca o matiz, o ícone da aba e lembra no aparelho', () => {
    const servico = TestBed.inject(AcentoService);
    const doc = TestBed.inject(DOCUMENT);

    servico.escolher('verde');

    expect(servico.atual().id).toBe('verde');
    expect(doc.documentElement.style.getPropertyValue('--acento-h')).toBe('150');
    expect(doc.documentElement.getAttribute('data-acento')).toBe('verde');
    const icones = doc.querySelectorAll<HTMLLinkElement>('link[rel="icon"]');
    expect(icones).toHaveLength(1);
    expect(icones[0].getAttribute('href')).toBe('icons/acento-verde.svg');
    expect(localStorage.getItem(CHAVE_ACENTO)).toBe('verde');
  });

  it('id desconhecido volta para o índigo; cada acento tem o seu ícone', () => {
    const servico = TestBed.inject(AcentoService);
    servico.escolher('xadrez');
    expect(servico.atual().id).toBe('indigo');
    expect(new Set(ACENTOS.map((a) => a.matiz)).size).toBe(ACENTOS.length);
  });
});
