import { test, expect } from '@playwright/test';

for (const state of [
  { message: 'Mikrofon hazırlanıyor… · Esc: iptal', recording: false, audioLevel: 0 },
  { message: 'Dinleniyor… · Esc: iptal', recording: true, audioLevel: 45 },
  { message: 'Konuşma çözümleniyor… · Esc: iptal', recording: false, audioLevel: 0 },
]) {
  test(`dikte göstergesi: ${state.message}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 252, height: 72 });
    await page.addInitScript(initial => {
      let listener: ((event: { data: unknown }) => void) | undefined;
      Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
        addEventListener(_type: string, callback: (event: { data: unknown }) => void) { listener = callback; },
        removeEventListener() { listener = undefined; },
        postMessage(message: { command: string }) {
          if (message.command === 'feature.ready') queueMicrotask(() => listener?.({ data: { type: 'feature.state', payload: initial } }));
        },
      } } });
    }, state);
    await page.goto('/?view=dictation-status');
    const status = page.getByRole('status', { name: state.message });
    await expect(status).toBeVisible();
    await expect(status.locator('strong')).toHaveText(state.message.split(' · ')[0]);
    await expect(status.locator('span').last()).toHaveText('Esc: iptal');
    expect(await status.locator('strong').evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath('status.png') });
  });
}
