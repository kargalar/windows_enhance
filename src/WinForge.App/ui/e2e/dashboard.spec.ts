import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import type { AppState, Setting } from '../src/types';

function setting(key: string, label: string, kind: Setting['kind'], value: unknown, section: string, extra: Partial<Setting> = {}): Setting {
  return { key, label, kind, value, section, min: 0, max: 100, options: [], optionLabels: {}, ...extra };
}
const state: AppState = {
  modules: [
    { id: 'taskbar', name: 'Görev Çubuğu', category: 'Taskbar', description: 'Başlat ve sistem simgeleri, dock animasyonu, tasarım, saat ve fare kısayollarını yönetin.', isActive: true,
      settings: [
        setting('behavior-enabled', 'Fare ve Windows görev çubuğu ayarları', 'toggle', true, 'Bileşenler', { description: 'Görev çubuğunun fare etkileşimlerini ve temel davranışlarını yapılandırın.' }),
        setting('design-enabled', 'Tasarım ve dock animasyonu', 'toggle', true, 'Bileşenler', { description: 'Görev çubuğu stili, simge animasyonları ve görsel efektleri özelleştirin.' }),
        setting('tray-enabled', 'Uygulama tepsisi seçimleri', 'toggle', true, 'Bileşenler', { description: 'Sistem tepsisi öğelerini ve simge davranışlarını yönetin.' }),
        setting('custom-background', 'Özel görev çubuğu arka planı', 'toggle', false, 'Tasarım'),
        setting('background-opacity', 'Arka plan opaklığı (%)', 'number', 72, 'Tasarım', { description: 'Görev çubuğu arka planının opaklık düzeyi.' }),
        setting('background-color', 'Arka plan rengi', 'text', '#171B24', 'Tasarım', { defaultValue: '#171B24' }),
        setting('theme', 'Renk düzeni', 'choice', 'Varsayılan', 'Tasarım', { options: ['Varsayılan', 'Koyu', 'Açık'] }),
        setting('clock-seconds', 'Saatte saniyeleri göster', 'toggle', false, 'Saat'),
      ] },
    { id: 'automation', name: 'Otomasyon', category: 'Verimlilik', description: 'Kısayol komutlarını ve otomasyonları yönetin.', isActive: false, settings: [], panelAvailable: true, panelLoaded: true,
      panel: { paused: false, capturing: false, masterShortcut: 'Ctrl+Alt+F8', rules: [] } },
    { id: 'window-shortcuts', name: 'Pencere Kısayolları', category: 'Verimlilik', description: 'Pencereler için kısayollar atayın.', isActive: false, settings: [], panelAvailable: true, panelLoaded: true,
      panel: { kind: 'shortcut-actions', capturing: false, actions: [{ id: 'center', name: 'Ortala' }], pendingDisplay: false,
        rules: [{ id: 'center', name: 'Pencereyi ortala', enabled: true, shortcut: 'Ctrl+Alt+C', action: 'center', value: '', arguments: '', hasSecret: false }] } },
    { id: 'default-apps', name: 'Varsayılan Programlar', category: 'Sistem', description: 'Dosya türleri için varsayılan uygulamaları yönetin.', isActive: false, settings: [], panelAvailable: true, panelLoaded: true,
      panel: { kind: 'default-apps', state: { status: '', query: '', count: 1, rows: [{ extension: '.txt', program: 'Not Defteri', executable: 'notepad.exe' }] } } },
  ],
  categories: ['Taskbar', 'Verimlilik', 'Sistem'], activeCount: 1, totalCount: 4, version: '1.0.0', persistence: { pending: false },
  appearance: { tone: 'Açık', accent: 'Mavi', glow: false, tones: ['Açık', 'Siyah', 'Grafit'], accents: [{ name: 'Mavi', hex: '#75A7FF' }, { name: 'Yeşil', hex: '#4CD97B' }, { name: 'Kırmızı', hex: '#FF6B6B' }], language: 'tr' },
};

async function connect(page: Page) {
  await page.addInitScript(snapshot => {
    const listeners = new Set<(event: { data: unknown }) => void>();
    const payload = structuredClone(snapshot);
    let version = 0;
    const respond = (requestId: string) => queueMicrotask(() => {
      const data = { type: 'state', protocolVersion: 1, sessionId: 'browser-test', version: ++version, requestId, payload: structuredClone(payload) };
      listeners.forEach(receive => receive({ data }));
    });
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
      addEventListener(_name: string, receive: (event: { data: unknown }) => void) { listeners.add(receive); },
      removeEventListener(_name: string, receive: (event: { data: unknown }) => void) { listeners.delete(receive); },
      postMessage(message: { command: string; requestId: string; payload: Record<string, unknown> }) {
        const p = message.payload;
        if (message.command === 'module.toggle') {
          const module = payload.modules.find(m => m.id === p.id)!;
          module.isActive = Boolean(p.enabled);
          payload.activeCount = payload.modules.filter(m => m.isActive).length;
        }
        if (message.command === 'setting.update') payload.modules.find(m => m.id === p.moduleId)!.settings.find(s => s.key === p.key)!.value = p.value;
        if (message.command === 'appearance.update') Object.assign(payload.appearance, p);
        if (['ui.ready', 'ui.resync', 'module.toggle', 'setting.update', 'appearance.update'].includes(message.command)) respond(message.requestId);
      },
    } } });
  }, state);
  await page.goto('/');
  await expect(page.locator('.tool-card')).toHaveCount(4);
}
async function open(page: Page, name = 'Görev Çubuğu') {
  await page.getByRole('button', { name: `${name}: Ayarları aç`, exact: true }).click();
  await expect(page.locator('.details-title h2')).toHaveText(name);
}
async function accessible(page: Page) {
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa']).analyze();
  expect(result.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => ({ target: n.target, summary: n.failureSummary })) }))).toEqual([]);
}

test('production page supports navigation, settings search, durable edits and focus restoration', async ({ page }) => {
  const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
  await connect(page);
  await open(page);
  await expect(page.locator('.dashboard-back')).toBeFocused();
  await page.keyboard.press('Control+f');
  await expect(page.getByRole('searchbox', { name: 'Modül ayarlarında ara' })).toBeFocused();
  await page.getByRole('searchbox').fill('opaklık');
  await expect(page.getByRole('slider', { name: 'Arka plan opaklığı (%)', exact: true })).toBeVisible();
  await page.getByRole('spinbutton').fill('81');
  await expect(page.getByRole('slider')).toHaveValue('81');
  await page.getByRole('button', { name: 'Modüllere dön' }).click();
  await expect(page.getByRole('button', { name: 'Görev Çubuğu: Ayarları aç', exact: true })).toBeFocused();
  await open(page);
  await page.getByRole('button', { name: /^Görünüm ve dock/ }).click();
  await expect(page.getByRole('slider')).toHaveValue('81');
  await page.getByRole('button', { name: 'Modüllere dön' }).click();
  await page.locator('.catalog-toolbar select').selectOption('inactive');
  await expect(page.locator('.tool-card')).toHaveCount(3);
  expect(errors).toEqual([]);
});

test('each management panel renders inside the shared details layout', async ({ page }) => {
  await connect(page);
  for (const name of ['Otomasyon', 'Pencere Kısayolları', 'Varsayılan Programlar']) {
    await open(page, name);
    await expect(page.locator('.management-section')).toBeVisible();
    await expect(page.locator('.panel-loading')).toHaveCount(0);
    await accessible(page);
    await page.getByRole('button', { name: 'Modüllere dön' }).click();
  }
});

test('light and dark themes are accessible and produce review screenshots', async ({ page }) => {
  await connect(page);
  await accessible(page);
  await open(page);
  await accessible(page);
  await page.screenshot({ path: '../../../artifacts/ui-review/details-light.png' });
  await page.getByRole('button', { name: /^Görünüm ve dock/ }).click();
  await accessible(page);
  await page.screenshot({ path: '../../../artifacts/ui-review/design-light.png' });
  await page.getByRole('button', { name: 'Karanlık temaya geç' }).click();
  await accessible(page);
  await page.screenshot({ path: '../../../artifacts/ui-review/design-dark.png' });
  await page.locator('.dashboard-actions .icon-button').click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await accessible(page);
  await page.getByRole('button', { name: 'Kapat', exact: true }).click();
  await expect(page.locator('.dashboard-actions .icon-button')).toBeFocused();
});

for (const width of [1280, 980, 768, 560]) test(`details remain usable at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 720 });
  await connect(page);
  await open(page);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const nav = page.getByRole('navigation', { name: 'Ayar bölümleri' });
  await expect(nav).toBeVisible();
  await page.getByRole('button', { name: /^Görünüm ve dock/ }).click();
  await expect(page.getByRole('slider')).toBeVisible();
  await accessible(page);
});
