import assert from 'node:assert/strict';
import test from 'node:test';
import { Window } from 'happy-dom';
import { createServer } from 'vite';

test('dashboard keeps navigation, theme selection and module details available without a sidebar', async () => {
  const window = new Window({ url: 'http://localhost/' });
  for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'Node', 'Event', 'KeyboardEvent', 'MouseEvent', 'localStorage']) {
    globalThis[name] = name === 'window' ? window : window[name];
  }
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const { act } = await import('react');
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  document.body.innerHTML = '<div id="root"></div>';
  try {
    await act(async () => { await server.ssrLoadModule('/src/main.tsx'); });
    const click = async selector => {
      const button = document.querySelector(selector);
      assert.ok(button, selector);
      await act(async () => button.click());
    };
    assert.equal(document.querySelector('.dashboard-sidebar'), null);
    assert.equal(document.querySelectorAll('.dashboard-navigation button').length, 8);
    assert.ok(document.querySelector('.light-dashboard'));
    await click('.dashboard-actions .secondary-button');
    assert.ok(document.querySelector('.dark-dashboard'));
    await click('.dashboard-actions .secondary-button');
    assert.ok(document.querySelector('.light-dashboard'));
    assert.equal(document.querySelector('.tool-card .tool-settings-button'), null);
    await click('.tool-card .toggle');
    assert.equal(document.querySelector('#module-details'), null);
    await click('.tool-favorite');
    assert.equal(document.querySelector('#module-details'), null);
    await click('.tool-favorite');
    await click('.tool-card-open');
    assert.ok(document.querySelector('.dashboard-workspace.with-details'));
    assert.ok(document.querySelector('#module-details .setting-copy strong'));
    await click('.dashboard-back');
    assert.equal(document.querySelector('#module-details'), null);
    await click('.dashboard-actions .icon-button');
    assert.equal(document.querySelectorAll('.tone-option').length, 3);
    await click('.tone-option:last-child');
    assert.ok(document.querySelector('.dark-dashboard.tone-grafit'));
    await click('.appearance-dialog .icon-button');
    await click('.dashboard-navigation button:nth-child(2)');
    assert.ok(document.querySelector('.dashboard-empty'));
    await click('.dashboard-navigation button:first-child');
    assert.equal(document.querySelectorAll('.tool-card').length, 3);
  } finally {
    await server.close();
    await window.happyDOM.close();
  }
});

test('dashboard shows a persistence failure and uses the durable acknowledgement to clear it', async () => {
  const window = new Window({ url: 'https://appassets.winforge/' });
  for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'Node', 'Event', 'KeyboardEvent', 'MouseEvent', 'localStorage'])
    globalThis[name] = name === 'window' ? window : window[name];
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  let receive;
  window.chrome = { webview: {
    addEventListener(_name, callback) { receive = callback; },
    removeEventListener() {},
    postMessage() {},
  } };
  const { act } = await import('react');
  const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
  document.body.innerHTML = '<div id="root"></div>';
  const payload = {
    modules: [], categories: [], activeCount: 0, totalCount: 0, version: 'test',
    appearance: { tone: 'Açık', accent: 'Mavi', glow: false, tones: ['Açık'], accents: [{ name: 'Mavi', hex: '#75A7FF' }] },
    persistence: { pending: true, error: 'Disk full' },
  };
  try {
    await act(async () => { await server.ssrLoadModule('/src/main.tsx'); });
    await act(async () => receive({ data: { type: 'state', payload } }));
    assert.match(document.querySelector('[role="alert"]').textContent, /Ayarlar kaydedilemedi: Disk full/);
    await act(async () => receive({ data: { type: 'state', payload, persistence: { pending: false, error: null } } }));
    assert.equal(document.querySelector('[role="alert"]'), null);
  } finally {
    await server.close();
    await window.happyDOM.close();
  }
});
