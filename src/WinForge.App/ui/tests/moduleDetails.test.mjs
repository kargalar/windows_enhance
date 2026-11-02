import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { Window } from 'happy-dom';
import { createTestServer } from './testServer.mjs';

const window = new Window({ url: 'https://appassets.winforge/' });
for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'Node', 'Event', 'KeyboardEvent', 'MouseEvent'])
  globalThis[name] = name === 'window' ? window : window[name];
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { createElement: h, act, StrictMode } = await import('react');
const { createRoot } = await import('react-dom/client');
const server = await createTestServer();
const { ModuleDetails } = await server.ssrLoadModule('/src/ModuleDetails.tsx');
const { SettingControl } = await server.ssrLoadModule('/src/SettingControl.tsx');
const { setLanguage } = await server.ssrLoadModule('/src/localization.ts');
setLanguage('en');
after(async () => { await server.close(); await window.happyDOM.close(); });

const setting = (key, kind, value, section = 'General', extra = {}) => ({ key, label: key, kind, value, section, min: 0, max: 100, options: [], optionLabels: {}, ...extra });
const module = { id: 'taskbar', name: 'Taskbar', category: 'Appearance', description: 'Customize your taskbar', isActive: true,
  settings: [setting('seconds', 'toggle', true), setting('opacity', 'number', 72, 'Design', { description: 'Background transparency' }), setting('theme', 'choice', 'Light', 'Design', { options: ['Light', 'Dark'] })] };
async function mount(component) {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  await act(async () => root.render(h(StrictMode, null, component)));
  return { container, async close() { await act(async () => root.unmount()); container.remove(); },
    async render(component) { await act(async () => root.render(h(StrictMode, null, component))); } };
}
async function click(element) { assert.ok(element); await act(async () => element.click()); }
async function type(element, value) {
  assert.ok(element);
  await act(async () => {
    Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(element, value);
    element.dispatchEvent(new window.Event('input', { bubbles: true }));
  });
}

test('module sections show only their settings and search finds labels, descriptions and sections', async () => {
  const writes = [];
  const view = await mount(h(ModuleDetails, { module, onToggle() {}, onUpdate: (...args) => writes.push(args), onExecute() {} }));
  const visible = () => [...view.container.querySelectorAll('.settings-section')].filter(s => !s.hidden && !s.closest('[hidden]'));
  try {
    assert.equal(visible().length, 1);
    assert.match(visible()[0].textContent, /theme/);
    await click([...view.container.querySelectorAll('.settings-navigation button')].find(b => b.textContent.includes('Additional')));
    assert.equal(visible().length, 1);
    assert.match(visible()[0].textContent, /opacity/);
    await type(view.container.querySelector('input[type=search]'), 'BACKGROUND');
    assert.equal(visible().length, 1);
    assert.equal(visible()[0].querySelectorAll('.setting-row').length, 1);
    await type(view.container.querySelector('input[type=search]'), 'Design');
    assert.equal(visible().reduce((n,s)=>n+s.querySelectorAll('.setting-row').length,0), 2);
    await type(view.container.querySelector('input[type=search]'), 'missing');
    assert.equal(visible().length, 0);
    assert.match(view.container.querySelector('.dashboard-empty').textContent, /No results/);
    assert.deepEqual(writes, []);
  } finally { await view.close(); }
});

test('numeric edits survive a stale host reply and flush when leaving the module', async () => {
  const writes = [];
  const props = { setting: module.settings[1], onUpdate: (...args) => writes.push(args), onExecute() {} };
  const view = await mount(h(SettingControl, props));
  await type(view.container.querySelector('input[type=range]'), '81');
  await view.render(h(SettingControl, { ...props, setting: { ...props.setting, value: 73 } }));
  assert.equal(view.container.querySelector('input[type=range]').value, '81');
  assert.equal(view.container.querySelector('input[type=number]').value, '81');
  await view.close();
  assert.deepEqual(writes, [['opacity', 81]]);
});

test('numeric exact value is bounded and toggles use descriptor defaults with accessible descriptions', async () => {
  const writes = [];
  const view = await mount(h(SettingControl, { setting: setting('size', 'number', 10), onUpdate: (...args) => writes.push(args), onExecute() {} }));
  const input = view.container.querySelector('input[type=number]');
  await act(async () => input.focus());
  await type(input, '200');
  assert.equal(input.getAttribute('aria-invalid'), 'true');
  await act(async () => input.blur());
  assert.equal(view.container.querySelector('input[type=range]').value, '100');
  await view.close();
  assert.deepEqual(writes, [['size', 100]]);
  const toggle = await mount(h(SettingControl, { setting: setting('enabled', 'toggle', undefined, 'General', { defaultValue: true, description: 'Helpful description' }), onUpdate() {}, onExecute() {} }));
  try {
    const control = toggle.container.querySelector('[role=switch]');
    assert.equal(control.getAttribute('aria-checked'), 'true');
    assert.equal(document.getElementById(control.getAttribute('aria-describedby')).textContent, 'Helpful description');
  } finally { await toggle.close(); }
});

test('exact numeric entry permits clearing and negative values without duplicate commits', async () => {
  const writes = [];
  const view = await mount(h(SettingControl, { setting: setting('gap', 'number', 12, 'Design', { min: -1, max: 24 }), onUpdate: (...args) => writes.push(args), onExecute() {} }));
  const input = view.container.querySelector('input[type=number]');
  await act(async () => input.focus());
  await type(input, '');
  assert.equal(input.value, '');
  await type(input, '-1');
  await act(async () => input.blur());
  await view.close();
  assert.deepEqual(writes, [['gap', -1]]);
});

test('deferred management errors remain recoverable and save status reflects durable native state', async () => {
  const commands = [];
  window.chrome = { webview: { postMessage: message => commands.push(message) } };
  const props = { module: { ...module, panelAvailable: true, panelError: 'Access denied' }, onToggle() {}, onUpdate() {}, onExecute() {} };
  const view = await mount(h(ModuleDetails, { ...props, persistence: { pending: true } }));
  try {
    assert.match(view.container.querySelector('.save-status').textContent, /Saving changes/);
    assert.match(view.container.querySelector('[role=alert]').textContent, /Access denied/);
    await click(view.container.querySelector('[role=alert] button'));
    assert.equal(commands[0].command, 'module.inspect');
    assert.equal(commands[0].payload.moduleId, 'taskbar');
    await view.render(h(ModuleDetails, { ...props, persistence: { pending: false, error: 'Disk full' } }));
    assert.match(view.container.querySelector('.save-status').textContent, /Disk full/);
    await view.render(h(ModuleDetails, { ...props, persistence: { pending: false } }));
    assert.match(view.container.querySelector('.save-status').textContent, /Changes saved/);
  } finally { await view.close(); delete window.chrome; }
});
