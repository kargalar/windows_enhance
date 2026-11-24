import { test, expect } from '@playwright/test';

for (const { phase, label, bars } of [
  { phase: 'preparing', label: 'Mikrofon açılıyor', bars: 3 },
  { phase: 'listening', label: 'Dinleniyor', bars: 5 },
  { phase: 'processing', label: 'Çözümleniyor', bars: 5 },
]) {
  test(`dikte göstergesi: ${label}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 76, height: 48 });
    await page.addInitScript(initial => {
      let listener: ((event: { data: unknown }) => void) | undefined;
      window.addEventListener('message', event => listener?.(event));
      Object.defineProperty(window, 'chrome', { configurable: true, value: { webview: {
        addEventListener(_type: string, callback: (event: { data: unknown }) => void) { listener = callback; },
        removeEventListener() { listener = undefined; },
        postMessage(message: { command: string }) {
          if (message.command === 'feature.ready') queueMicrotask(() => listener?.({ data: { type: 'feature.state', payload: initial } }));
        },
      } } });
    }, { phase, audioLevel: 45 });
    await page.goto('/?view=dictation-status');
    const status = page.getByRole('status', { name: label });
    await expect(status).toBeVisible();
    await expect(status).toHaveText('');
    const wave = status.locator(`.dictation-waves.${phase}`);
    await expect(status).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
    await expect(wave).toHaveCSS('background-color', 'rgb(27, 27, 30)');
    await expect(wave.locator('span:visible')).toHaveCount(bars);
    if (phase === 'listening') {
      const center = wave.locator('span:nth-child(3)');
      const initialHeight = (await center.boundingBox())!.height;
      await page.evaluate(() => window.postMessage({ type: 'feature.state', payload: { phase: 'listening', audioLevel: 100 } }, location.origin));
      await expect.poll(async () => (await center.boundingBox())!.height).toBeGreaterThan(initialHeight);
    }
    await page.screenshot({ path: testInfo.outputPath('status.png'), omitBackground: true });
  });
}
