import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { Window } from 'happy-dom';
import { createTestServer } from './testServer.mjs';

const window = new Window({ url: 'https://appassets.winforge/' });
for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'Node', 'Element', 'Event'])
  globalThis[name] = name === 'window' ? window : window[name];
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { act, createElement: h } = await import('react');
const { createRoot } = await import('react-dom/client');
const server = await createTestServer();
const { FeatureApp } = await server.ssrLoadModule('/src/features/FeatureApp.tsx');
after(async () => { await server.close(); await window.happyDOM.close(); });

test('PC History shows network totals and refreshes to an empty date range', async () => {
  let receive;
  window.chrome = { webview: { addEventListener(_name, callback) { receive = callback; }, removeEventListener() {}, postMessage() {} } };
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  const state = { from: '', until: '', status: '', summary: '', applications: [], days: [], dayApplications: [], contexts: [], tags: [],
    network: [{ date: '03.11.2026', received: 2 * 1024 ** 3, sent: 512 * 1024 ** 2 }] };
  try {
    await act(async () => root.render(h(FeatureApp, { view: 'capture-usage-statistics' })));
    await act(async () => receive({ data: { type: 'feature.state', payload: state } }));
    const button = [...container.querySelectorAll('button')].find(node => node.textContent === 'İnternet kullanımı');
    assert.ok(button);
    await act(async () => button.click());
    assert.match(container.querySelector('.statistics-summary').textContent, /İndirme: 2.00 GB · Yükleme: 512.0 MB · Toplam: 2.50 GB/);
    assert.equal(container.querySelector('[role=table] [role=cell]').textContent, '03.11.2026');
    await act(async () => receive({ data: { type: 'feature.state', payload: { ...state, network: [] } } }));
    assert.equal(container.querySelector('[role=table]'), null);
    assert.match(container.textContent, /Seçilen tarih aralığında internet kullanımı kaydı yok/);
    assert.match(container.querySelector('.statistics-summary').textContent, /Toplam: 0 B/);
  } finally {
    await act(async () => root.unmount()); container.remove();
  }
});
