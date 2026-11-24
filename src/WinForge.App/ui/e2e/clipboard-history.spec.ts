import { expect, test, type Page } from '@playwright/test';
import type { ClipboardHistoryState } from '../src/features/ClipboardHistoryView';

const first = { id: 1, copiedAt: 1795184100000, text: 'Teslimat adresini ekip ile paylaş.', thumbnail: null, width: 0, height: 0 };
const second = { id: 2, copiedAt: 1795183560000, text: 'Toplantı notları\nTasarım taslağını cuma günü gözden geçir.', thumbnail: null, width: 0, height: 0 };
const picture = {
  id: 3, copiedAt: 1795182000000, text: null, width: 1280, height: 720,
  thumbnail: 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+XwQZ8QAAAABJRU5ErkJggg==',
};

async function expectMonochromeSurface(page: Page) {
  const colors = await page.evaluate(() => {
    const selectors = [
      '.clipboard-history-root',
      '.clipboard-history',
      '.clipboard-history-search',
      '.clipboard-history-filters button[aria-pressed="true"]',
      '.clipboard-history-item[data-selected="true"]',
      '.clipboard-history-footer',
    ];
    return selectors.flatMap(selector => {
      const element = document.querySelector(selector);
      if (!element) return [];
      const style = getComputedStyle(element);
      return [
        { selector, prop: 'color', value: style.color },
        { selector, prop: 'backgroundColor', value: style.backgroundColor },
        { selector, prop: 'borderTopColor', value: style.borderTopColor },
      ];
    });
  });

  for (const { selector, prop, value } of colors) {
    const match = /^rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*[\d.]+)?\)$/.exec(value);
    expect(match, `${selector} ${prop} should be rgb/rgba, got ${value}`).not.toBeNull();
    const [, r, g, b] = match!;
    expect(r === g && g === b, `${selector} ${prop} must be strictly monochrome (R==G==B), got ${value}`).toBe(true);
  }
}

test('Clipboard History searches, filters, and copies the selected entry in monochrome theme', async ({ page }, info) => {
  const state: ClipboardHistoryState = {
    session: 1, language: 'tr', appearance: 'dark', query: '', kind: 'all',
    loading: false, loadingMore: false, error: '', page: 1, hasNext: false, items: [first, second, picture],
  };
  await page.setViewportSize({ width: 440, height: 560 });
  const commands: { command: string; payload: Record<string, unknown> }[] = [];
  await page.exposeFunction('receiveClipboardCommand', (message: typeof commands[number]) => { commands.push(message); });
  await page.addInitScript(initial => {
    const listeners = new Set<(event: MessageEvent) => void>();
    let snapshot = initial;
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
      addEventListener: (_: string, listener: (event: MessageEvent) => void) => listeners.add(listener),
      removeEventListener: (_: string, listener: (event: MessageEvent) => void) => listeners.delete(listener),
      postMessage(message: { command: string; payload: { query: string; kind: ClipboardHistoryState['kind'] } }) {
        (window as unknown as { receiveClipboardCommand: (message: unknown) => void }).receiveClipboardCommand(message);
        if (message.command === 'clipboard-history.filter') {
          const { query, kind } = message.payload;
          const items = initial.items.filter(item =>
            (kind === 'all' || (kind === 'image' ? item.thumbnail : item.text)) &&
            (!query || item.text?.toLocaleLowerCase('tr').includes(query.toLocaleLowerCase('tr'))));
          snapshot = { ...snapshot, query, kind, items };
        }
        queueMicrotask(() => listeners.forEach(listener =>
          listener(new MessageEvent('message', { data: { type: 'feature.state', payload: snapshot } }))));
      },
    } } });
  }, state);
  await page.goto('/?view=clipboard-history');
  const search = page.getByRole('searchbox', { name: 'Geçmişte ara' });
  await expect(search).toBeFocused();
  await expect(search).toHaveCSS('outline-style', 'none');
  await expect(page.locator('.clipboard-history-search')).toHaveCSS('border-top-color', 'rgb(140, 140, 140)');
  await expect(page.getByRole('dialog', { name: 'Pano Geçmişi' })).toBeVisible();
  await expectMonochromeSurface(page);
  await page.screenshot({ path: info.outputPath('clipboard-history.png') });

  await search.press('ArrowDown');
  await search.press('Enter');
  await expect.poll(() => commands.at(-1)).toEqual({ command: 'clipboard-history.copy', payload: { id: 2 } });

  await search.fill('teslimat');
  await expect(page.getByRole('listitem')).toHaveCount(1);
  await page.getByRole('button', { name: /^Panoya kopyala: Teslimat/ }).click();
  await expect.poll(() => commands.at(-1)).toEqual({ command: 'clipboard-history.copy', payload: { id: 1 } });
  await search.fill('');
  await page.getByRole('button', { name: 'Görseller' }).click();
  await expect(page.getByRole('listitem')).toHaveCount(1);
  await page.getByRole('button', { name: /Panoya kopyala: Görsel 1280/ }).click();
  await expect.poll(() => commands.at(-1)).toEqual({ command: 'clipboard-history.copy', payload: { id: 3 } });
  await page.keyboard.press('Escape');
  await expect.poll(() => commands.at(-1)).toEqual({ command: 'clipboard-history.close', payload: {} });
});

test('Clipboard History supports monochrome light mode and infinite scroll pagination', async ({ page }) => {
  const allItems = Array.from({ length: 30 }, (_, index) => ({
    id: 30 - index,
    copiedAt: 1795184100000 - index * 60000,
    text: `Öğe ${30 - index}`,
    thumbnail: null,
    width: 0,
    height: 0,
  }));
  const initialState: ClipboardHistoryState = {
    session: 2, language: 'tr', appearance: 'light', query: '', kind: 'all',
    loading: false, loadingMore: false, error: '', page: 1, hasNext: true, items: allItems.slice(0, 24),
  };

  await page.setViewportSize({ width: 440, height: 560 });
  const commands: { command: string; payload: Record<string, unknown> }[] = [];
  await page.exposeFunction('receiveClipboardCommand', (message: typeof commands[number]) => { commands.push(message); });
  await page.addInitScript(({ initial, full }) => {
    const listeners = new Set<(event: MessageEvent) => void>();
    let snapshot = initial;
    Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
      addEventListener: (_: string, listener: (event: MessageEvent) => void) => listeners.add(listener),
      removeEventListener: (_: string, listener: (event: MessageEvent) => void) => listeners.delete(listener),
      postMessage(message: { command: string; payload: Record<string, unknown> }) {
        (window as unknown as { receiveClipboardCommand: (message: unknown) => void }).receiveClipboardCommand(message);
        if (message.command === 'clipboard-history.load-more' && snapshot.hasNext) {
          snapshot = { ...snapshot, page: 2, hasNext: false, loadingMore: false, items: full };
        }
        queueMicrotask(() => listeners.forEach(listener =>
          listener(new MessageEvent('message', { data: { type: 'feature.state', payload: snapshot } }))));
      },
    } } });
  }, { initial: initialState, full: allItems });

  await page.goto('/?view=clipboard-history');
  await expect(page.getByRole('listitem')).toHaveCount(24);
  await expect(page.getByRole('button', { name: 'Önceki sayfa' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Sonraki sayfa' })).toHaveCount(0);
  await expectMonochromeSurface(page);

  await page.locator('.clipboard-history-scroll').evaluate(el => {
    el.scrollTop = el.scrollHeight;
    el.dispatchEvent(new Event('scroll'));
  });

  await expect.poll(() => commands.find(entry => entry.command === 'clipboard-history.load-more'))
    .toEqual({ command: 'clipboard-history.load-more', payload: { page: 1 } });
  await expect(page.getByRole('listitem')).toHaveCount(30);
  await expect(page.locator('.clipboard-history-count')).toHaveText('30 öğe');

  await page.getByRole('button', { name: 'Panoya kopyala: Öğe 30', exact: true }).click();
  await expect.poll(() => commands.at(-1)).toEqual({ command: 'clipboard-history.copy', payload: { id: 30 } });

  await page.getByRole('button', { name: 'Panoya kopyala: Öğe 1', exact: true }).click();
  await expect.poll(() => commands.at(-1)).toEqual({ command: 'clipboard-history.copy', payload: { id: 1 } });
});
