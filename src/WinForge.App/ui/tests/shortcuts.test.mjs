import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { Window } from 'happy-dom';
import { createServer } from 'vite';

const window = new Window({ url: 'https://appassets.winforge/' });
for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'Node', 'Event', 'KeyboardEvent', 'MouseEvent']) {
  globalThis[name] = name === 'window' ? window : window[name];
}
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { createElement: h, act, useState, StrictMode } = await import('react');
const { createRoot } = await import('react-dom/client');
const server = await createServer({ server: { middlewareMode: true }, appType: 'custom' });
const { ShortcutField } = await server.ssrLoadModule('/src/ShortcutField.tsx');
const { ShortcutActionsPanel } = await server.ssrLoadModule('/src/ShortcutActionsPanel.tsx');
const { AutomationPanel } = await server.ssrLoadModule('/src/AutomationPanel.tsx');
const { useAutoSaveDraft } = await server.ssrLoadModule('/src/useAutoSaveDraft.ts');
after(async () => { await server.close(); await window.happyDOM.close(); });

async function mount(component) {
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  await act(async () => root.render(h(StrictMode, null, component)));
  return { container, async close() { await act(async () => root.unmount()); container.remove(); } };
}
async function click(element) { assert.ok(element); await act(async () => element.click()); }
async function focus(element) { await act(async () => element.focus()); }
async function key(element, code, key, modifiers = {}) {
  await act(async () => element.dispatchEvent(new window.KeyboardEvent('keydown', { bubbles: true, cancelable: true, code, key, ...modifiers })));
}
async function type(element, text) {
  await act(async () => {
    Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(element, text);
    element.dispatchEvent(new window.Event('input', { bubbles: true }));
  });
}

test('shared field saves the manually edited value and releases native capture on blur', async () => {
  const commands = [], saved = [];
  window.chrome = { webview: { postMessage: message => commands.push(message.command) } };
  function Harness() {
    const [value, setValue] = useState('Ctrl+Space');
    return h(ShortcutField, { value, onChange: setValue, onCommit: next => saved.push(next), label: 'Shortcut' });
  }
  const view = await mount(h(Harness));
  try {
    const input = view.container.querySelector('input');
    await focus(input); await type(input, 'Alt+F7');
    await act(async () => input.blur());
    assert.equal(input.value, 'Alt+F7');
    assert.deepEqual(saved, ['Alt+F7']);
    assert.deepEqual(commands, ['shortcut.capture-begin', 'shortcut.capture-end']);
  } finally { await view.close(); }
});

test('recorded key commits its new value immediately; cancel never restarts recording on blur', async () => {
  const saved = [];
  window.chrome = { webview: { postMessage() {} } };
  function Harness() {
    const [value, setValue] = useState('Ctrl+Space');
    return h(ShortcutField, { value, onChange: setValue, onCommit: next => saved.push(next), label: 'Shortcut' });
  }
  const view = await mount(h(Harness));
  try {
    await click(view.container.querySelector('button'));
    await key(view.container.querySelector('input'), 'KeyJ', 'j', { ctrlKey: true, altKey: true });
    assert.equal(view.container.querySelector('input').value, 'Ctrl+Alt+J');
    assert.deepEqual(saved, ['Ctrl+Alt+J']);
    await click(view.container.querySelector('button'));
    await focus(view.container.querySelector('button'));
    await click(view.container.querySelector('button'));
    assert.equal(view.container.querySelector('input').readOnly, false);
    assert.equal(view.container.querySelector('input').value, 'Ctrl+Alt+J');
  } finally { await view.close(); }
});

test('every shortcut action panel automatically saves recorded and manually edited shortcuts', async () => {
  for (const moduleId of ['media-shortcuts', 'window-shortcuts', 'desktop-shortcuts', 'text-shortcuts', 'program-shortcuts', 'monitor-shortcuts']) {
    const sent = [];
    const rule = { id: 'test', name: 'Test', enabled: true, shortcut: 'Ctrl+Space', action: 'play', value: '', arguments: '', hasSecret: false };
    function Harness() {
      const [state, setState] = useState({ kind: 'shortcut-actions', capturing: false, actions: [{ id: 'play', name: 'Test' }], rules: [rule], pendingDisplay: false });
      return h(ShortcutActionsPanel, { moduleId, active: true, state, command(action, data) {
        sent.push({ action, data });
        if (action.startsWith('capture-')) setState(current => ({ ...current, capturing: action === 'capture-begin' }));
        if (action === 'save') setState(current => ({ ...current, rules: [data.rule] }));
      } });
    }
    const view = await mount(h(Harness));
    try {
      await click([...view.container.querySelectorAll('button')].find(b => b.textContent === 'Tuş / fare ata'));
      await key(view.container.querySelector('input[aria-label]'), 'KeyJ', 'j', { altKey: true });
      assert.equal(sent.findLast(c => c.action === 'save').data.rule.shortcut, 'Alt+J', moduleId);
      assert.match(view.container.textContent, /Kayıtlı kısayol: Alt\+J/, moduleId);
      const input = view.container.querySelector('input[aria-label]');
      await focus(input); await type(input, 'Ctrl+Alt+F9');
      await act(async () => input.blur());
      assert.equal(sent.findLast(c => c.action === 'save').data.rule.shortcut, 'Ctrl+Alt+F9', moduleId + ' manual edit');
      await click([...view.container.querySelectorAll('button')].find(b => b.textContent === 'Tuş / fare ata'));
      await act(async () => {
        // happy-dom's WheelEvent omits the MouseEvent modifier fields browsers provide.
        const wheel = new window.WheelEvent('wheel', { bubbles: true, deltaY: -120 });
        Object.defineProperty(wheel, 'altKey', { value: true });
        input.dispatchEvent(wheel);
      });
      assert.equal(sent.findLast(c => c.action === 'save').data.rule.shortcut, 'Alt+WheelUp', moduleId + ' mouse edit');
    } finally { await view.close(); }
  }
});

test('automation shortcut automatically persists across status refreshes', async () => {
  const sent = [];
  const rule = { id: 'test', name: 'Test', enabled: true, shortcut: 'F1', repeat: true, interval: 100, steps: [{ kind: 'click', value: 'left', delay: 0 }], running: false };
  function Harness() {
    const [state, setState] = useState({ capturing: false, paused: false, masterShortcut: 'Ctrl+Alt+F8', rules: [rule] });
    return h(AutomationPanel, { active: true, state, command(action, data) {
      sent.push({ action, data });
      if (action.startsWith('capture-')) setState(current => ({ ...current, capturing: action === 'capture-begin' }));
      if (action === 'save') setState(current => ({ ...current, rules: [data.rule] }));
    } });
  }
  const view = await mount(h(Harness));
  try {
    const card = view.container.querySelector('article');
    await click([...card.querySelectorAll('button')].find(b => b.textContent === 'Tuş ata'));
    await key(card.querySelector('input[aria-label]'), 'F7', 'F7', { shiftKey: true });
    assert.equal(sent.findLast(c => c.action === 'save').data.rule.shortcut, 'Shift+F7');
  } finally { await view.close(); }
});

test('editing a card autosaves without blur or Save and deletion cancels pending writes', async () => {
  const sent = [];
  const rule = { id: 'p', name: 'Program', enabled: false, shortcut: 'F23', action: 'explorer', value: 'C:\\', arguments: '', hasSecret: false };
  const view = await mount(h(ShortcutActionsPanel, { moduleId: 'program-shortcuts', active: false,
    state: { kind: 'shortcut-actions', capturing: false, actions: [{ id: 'explorer', name: 'Explorer' }], rules: [rule] },
    command: (action, data) => sent.push({ action, data }) }));
  try {
    assert.ok(![...view.container.querySelectorAll('button')].some(button => button.textContent === 'Kaydet'));
    const name = view.container.querySelector('input[maxlength="100"]');
    await type(name, 'My folders');
    await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
    assert.equal(sent.findLast(item => item.action === 'save').data.rule.name, 'My folders');
    await type(name, 'Delete pending edit');
    await click([...view.container.querySelectorAll('button')].find(button => button.textContent === 'Sil'));
    const writes = sent.filter(item => item.action === 'save').length;
    await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
    assert.equal(sent.filter(item => item.action === 'save').length, writes);
  } finally { await view.close(); }
});

test('autosave preserves newer edits across delayed acknowledgements and flushes on navigation', async () => {
  const saved = [];
  let upstream;
  function Harness() {
    const [value, setValue] = useState({ name: 'Initial', running: false }); upstream = setValue;
    const [draft, update] = useAutoSaveDraft(value, next => saved.push(next));
    return h('input', { value: draft.name, onChange: event => update({ ...draft, name: event.target.value }) });
  }
  const view = await mount(h(Harness));
  const input = view.container.querySelector('input');
  await type(input, 'First');
  await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
  await type(input, 'Second');
  await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
  await act(async () => upstream({ name: 'First', running: true }));
  assert.equal(input.value, 'Second');
  await act(async () => upstream({ name: 'Second', running: false }));
  assert.equal(input.value, 'Second');
  await type(input, 'Final'); await view.close();
  assert.equal(saved.at(-1).name, 'Final');
});

test('translator target and automation exe scope save automatically', async () => {
  const sent = [];
  const rule = { id: 'test', name: 'Test', enabled: false, shortcut: 'F1', repeat: true, interval: 100, processName: '', steps: [{ kind: 'wait', value: '', delay: 1000 }], running: false };
  const view = await mount(h(AutomationPanel, { active: false, state: { capturing: false, paused: false, masterShortcut: 'Ctrl+Alt+F8', rules: [rule] }, command: (action, data) => sent.push({ action, data }) }));
  try {
    await type(view.container.querySelector('input[placeholder^="oyun.exe"]'), 'game.exe');
    await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
    assert.equal(sent.findLast(item => item.action === 'save').data.rule.processName, 'game.exe');
  } finally { await view.close(); }
  const translation = await mount(h(ShortcutActionsPanel, { moduleId: 'combination-translator', active: true,
    state: { kind: 'shortcut-actions', capturing: true, actions: [{ id: 'translate', name: 'Translate' }], rules: [{ id: 't', name: 'Test', enabled: false, shortcut: 'Ctrl+D', action: 'translate', value: 'Alt+W', arguments: '', hasSecret: false }] }, command: (action, data) => sent.push({ action, data }) }));
  try {
    const fields = translation.container.querySelectorAll('input[aria-label]');
    await type(fields[1], 'Alt+Q');
    await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
    assert.equal(sent.findLast(item => item.action === 'save').data.rule.value, 'Alt+Q');
  } finally { await translation.close(); }
});
