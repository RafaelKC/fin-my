import { CurrencyPipe, DatePipe } from '@angular/common';
import { LOCALE_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { appConfig } from './app.config';

describe('App', () => {
  beforeEach(async () => {
    // O toaster (ngx-sonner) lê a preferência de tema do sistema; o jsdom não tem matchMedia.
    window.matchMedia = ((media: string) => ({
      matches: false,
      media,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
    })) as unknown as typeof window.matchMedia;
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [...appConfig.providers, provideHttpClientTesting()],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should use pt-BR as locale', () => {
    expect(TestBed.inject(LOCALE_ID)).toBe('pt-BR');
  });

  it('should format currency and dates in pt-BR', () => {
    const locale = TestBed.inject(LOCALE_ID);
    const currency = new CurrencyPipe(locale, 'BRL').transform(1234.5);
    expect(currency?.replace(/\s/g, ' ')).toBe('R$ 1.234,50');
    expect(new DatePipe(locale).transform('2026-03-05T12:00:00', 'shortDate')).toBe('05/03/2026');
  });
});
