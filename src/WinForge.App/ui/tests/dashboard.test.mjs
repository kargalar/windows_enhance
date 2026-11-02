import assert from 'node:assert/strict';
import test from 'node:test';
import { Window } from 'happy-dom';
import { createTestServer } from './testServer.mjs';

test('dashboard provides persistent navigation, a dedicated details page and theme selection', async () => {
  const window = new Window({ url: 'http://localhost/' });
  for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'Node', 'Event', 'KeyboardEvent', 'MouseEvent', 'localStorage']) {
    globalThis[name] = name === 'window' ? window : window[name];
  }
  globalThis.IS_REACT_ACT_ENVIRONMENT = true;
  const { act } = await import('react');
  const { createRoot } = await import('react-dom/client');
  const server = await createTestServer();
  document.body.innerHTML = '<div id="root"></div>';
  const root = createRoot(document.getElementById('root'));
  try {
    const { App } = await server.ssrLoadModule('/src/App.tsx');
    await act(async () => root.render((await import('react')).createElement(App)));
    const click = async selector => {
      const button = document.querySelector(selector);
      assert.ok(button, selector);
      await act(async () => button.click());
    };
    assert.ok(document.querySelector('.dashboard-sidebar'));
    assert.equal(document.querySelectorAll('.dashboard-navigation button').length, 9);
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
    assert.equal(document.querySelector('.dashboard-catalog').hidden, true);
    assert.ok(document.querySelector('.details-body.single-design-page'));
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
    await act(async () => root.unmount());
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
  const { createRoot } = await import('react-dom/client');
  const server = await createTestServer();
  document.body.innerHTML = '<div id="root"></div>';
  const root = createRoot(document.getElementById('root'));
  const payload = {
    modules: [
      { id: 'window-shortcuts', name: 'Window Shortcuts', category: 'Verimlilik', description: '', isActive: false, settings: [] },
      { id: 'keyboard-manager', name: 'Keyboard Manager', category: 'Verimlilik', description: '', isActive: false, settings: [] },
      { id: 'automation', name: 'Automation', category: 'Verimlilik', description: '', isActive: false, settings: [] },
    ], categories: [], activeCount: 0, totalCount: 3, version: 'test',
    appearance: { tone: 'Açık', accent: 'Mavi', glow: false, tones: ['Açık'], accents: [{ name: 'Mavi', hex: '#75A7FF' }] },
    persistence: { pending: true, error: 'Disk full' },
  };
  try {
    const { App } = await server.ssrLoadModule('/src/App.tsx');
    await act(async () => root.render((await import('react')).createElement(App)));
    await act(async () => receive({ data: { type: 'state', payload } }));
    assert.match(document.querySelector('[role="alert"]').textContent, /(?:Ayarlar kaydedilemedi|Settings could not be saved): Disk full/);
    await act(async () => receive({ data: { type: 'state', payload, persistence: { pending: false, error: null } } }));
    assert.equal(document.querySelector('[role="alert"]'), null);
    const nav = [...document.querySelectorAll('.dashboard-navigation button')];
    await act(async () => nav.find(button => /K?sayollar|Shortcuts/.test(button.textContent)).click());
    assert.equal(document.querySelectorAll('.tool-card').length, 2);
    assert.match(document.querySelector('.dashboard-workspace').textContent, /Keyboard Manager/);
    await act(async () => nav.find(button => /Verimlilik|Productivity/.test(button.textContent)).click());
    assert.equal(document.querySelectorAll('.tool-card').length, 1);
    assert.match(document.querySelector('.dashboard-workspace').textContent, /Automation/);
  } finally {
    await act(async () => root.unmount());
    await server.close();
    await window.happyDOM.close();
  }
});
