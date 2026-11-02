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
const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
const { ShortcutField } = await server.ssrLoadModule('/src/ShortcutField.tsx');
const { ShortcutActionsPanel } = await server.ssrLoadModule('/src/ShortcutActionsPanel.tsx');
const { AutomationPanel } = await server.ssrLoadModule('/src/AutomationPanel.tsx');
const { useAutoSaveDraft } = await server.ssrLoadModule('/src/useAutoSaveDraft.ts');
const { ContextMenuPanel } = await server.ssrLoadModule('/src/ContextMenuPanel.tsx');
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

test('media shortcut volume amounts save independently for increase and decrease', async () => {
  const sent = [];
  const rules = ['volume-up', 'volume-down'].map((action, index) => ({
    id: action, name: action, enabled: false, shortcut: index ? 'Alt+WheelDown' : 'Alt+WheelUp',
    action, value: '', arguments: '', hasSecret: false,
  }));
  const state = { kind: 'shortcut-actions', capturing: false, actions: rules.map(rule => ({ id: rule.action, name: rule.name })), rules, pendingDisplay: false };
  const view = await mount(h(ShortcutActionsPanel, { moduleId: 'media-shortcuts', active: true, state,
    command: (action, data) => sent.push({ action, data }) }));
  try {
    const amounts = view.container.querySelectorAll('input[type="number"]');
    assert.equal(amounts.length, 2);
    assert.equal(amounts[0].value, '2');
    await type(amounts[0], '17');
    await type(amounts[1], '9');
    await act(async () => new Promise(resolve => setTimeout(resolve, 600)));
    const saved = sent.filter(command => command.action === 'save').map(command => command.data.rule);
    assert.equal(saved.find(rule => rule.id === 'volume-up').value, '17');
    assert.equal(saved.find(rule => rule.id === 'volume-down').value, '9');
  } finally { await view.close(); }
});

test('every shortcut action panel automatically saves recorded and manually edited shortcuts', async () => {
  for (const moduleId of ['media-shortcuts', 'window-shortcuts', 'system-shortcuts', 'desktop-shortcuts', 'text-shortcuts', 'program-shortcuts', 'monitor-shortcuts']) {
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

test('system shortcuts save the selected power action and appear in keyboard navigation', async () => {
  const { groupModules } = await server.ssrLoadModule('/src/moduleGroups.ts');
  assert.equal(groupModules([{ id: 'system-shortcuts', category: 'Verimlilik' }])[0].name, 'Kısayollar ve Klavye');
  const sent = [];
  const rule = { id: 'shutdown', name: 'Bilgisayarı kapat', enabled: false, shortcut: 'Ctrl+Alt+End', action: 'shutdown', value: '', arguments: '', hasSecret: false };
  const view = await mount(h(ShortcutActionsPanel, { moduleId: 'system-shortcuts', active: true,
    state: { kind: 'shortcut-actions', capturing: false, actions: [{ id: 'shutdown', name: 'Bilgisayarı kapat' }, { id: 'restart', name: 'Bilgisayarı yeniden başlat' }], rules: [rule] },
    command: (action, data) => sent.push({ action, data }) }));
  try {
    assert.match(view.container.textContent, /seçilen işlemi doğrudan başlatır/);
    const select = view.container.querySelector('select');
    await act(async () => { select.value = 'restart'; select.dispatchEvent(new window.Event('change', { bubbles: true })); });
    await click(view.container.querySelector('input[type="checkbox"]'));
    const saved = sent.findLast(c => c.action === 'save').data.rule;
    assert.equal(saved.action, 'restart'); assert.equal(saved.enabled, true);
    assert.equal(saved.shortcut, 'Ctrl+Alt+End');
    assert.equal(sent.some(c => ['shutdown', 'restart'].includes(c.action)), false);
  } finally { await view.close(); }
});

test('text expansion mode autosaves an abbreviation without shortcut capture', async () => {
  const sent = [];
  const rule = { id: 'mail', name: 'Mail', enabled: true, shortcut: 'Ctrl+P', action: 'text', value: '', arguments: '', hasSecret: true };
  const view = await mount(h(ShortcutActionsPanel, {
    moduleId: 'text-shortcuts', active: true,
    state: { capturing: false, actions: [{ id: 'text', name: 'Shortcut' }, { id: 'text-expand', name: 'Expansion' }], rules: [rule] },
    command: (action, data) => sent.push({ action, data }),
  }));
  try {
    const select = view.container.querySelector('select');
    await act(async () => { select.value = 'text-expand'; select.dispatchEvent(new window.Event('change', { bubbles: true })); });
    const input = view.container.querySelector('input[aria-label="Kısaltma"]');
    assert.ok(input);
    await type(input, '.mail');
    await act(async () => new Promise(resolve => setTimeout(resolve, 1000)));
    const saved = sent.findLast(c => c.action === 'save').data.rule;
    assert.equal(saved.action, 'text-expand'); assert.equal(saved.shortcut, '.mail'); assert.equal(saved.value, '');
    assert.equal(sent.some(c => c.action === 'capture-begin'), false);
    assert.equal([...view.container.querySelectorAll('button')].some(b => b.textContent === 'Tuş / fare ata'), false);
  } finally { await view.close(); }
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

test('context menu combines registrations and saves independent location switches', async () => {
  const sent = [];
  const base = { originalName: 'Open Git Bash here', name: 'Open Git Bash here', visible: true, group: '', icon: '', identity: 'open git bash here', hasOriginalIcon: true, iconPreview: 'data:image/png;base64,AA==' };
  const entries = [{ ...base, id: 'verb:Directory:git', scope: 'folder', source: 'Directory ? komut' }, { ...base, id: 'verb:Directory\\Background:git', scope: 'background', source: 'Directory\\Background ? komut' }, { ...base, id: 'menu:git', source: 'Ger?ek men? ??esi' }];
  const view = await mount(h(ContextMenuPanel, { state: { kind: 'context-menu', groups: [], entries }, command: (action, data) => sent.push({action, data}) }));
  try {
    const search = view.container.querySelector('.context-toolbar input[type="search"]');
    assert.equal(view.container.querySelectorAll('.context-entry').length, 0);
    await type(search, '   ');
    assert.equal(view.container.querySelectorAll('.context-entry').length, 0);
    await type(search, '  git  ');
    assert.equal(view.container.querySelectorAll('.context-entry').length, 1);
    assert.equal(view.container.querySelectorAll('.context-entry .context-locations input[type="checkbox"]').length, 8);
    assert.equal(view.container.querySelector('input[aria-label$="grubu"]'), null);
    const switches = view.container.querySelectorAll('.context-entry .context-locations input[type="checkbox"]');
    await click(switches[1]);
    const saved = sent.at(-1);
    assert.equal(saved.action, 'save-item');
    assert.equal(saved.data.ids.length, 3);
    assert.equal(saved.data.rule.contexts.folder, false);
    assert.equal(saved.data.rule.contexts.desktop, true);
    await click(switches[3]);
    assert.equal(sent.at(-1).data.rule.contexts.folder, false);
    assert.equal(sent.at(-1).data.rule.contexts.desktop, false);
    await type(view.container.querySelector('input[aria-label$="görünen adı"]'), 'Git Bash');
    await act(async () => new Promise(resolve => setTimeout(resolve, 500)));
    assert.equal(sent.at(-1).data.rule.name, 'Git Bash');
    await click([...view.container.querySelectorAll('button')].find(button => button.textContent === 'Simgeyi değiştir'));
    assert.equal(sent.at(-1).action, 'pick-icon');
    assert.equal(sent.at(-1).data.ids.length, 3);
    await type(search, '');
    assert.equal(view.container.querySelectorAll('.context-entry').length, 0);
  } finally { await view.close(); }
});

test('context visibility supports row and filtered batch changes while preserving pending edits', async () => {
  const sent = [];
  const base = { visible: true, group: 'Araçlar', icon: '', source: 'Windows komutu', hasOriginalIcon: false };
  function Harness() {
    const [state, setState] = useState({ kind: 'context-menu', groups: ['Araçlar'], entries: [
      { ...base, id: 'builtin:properties', originalName: 'Özellikler', name: 'Özellikler' },
      { ...base, id: 'builtin:copy', originalName: 'Kopyala', name: 'Kopyala' },
    ] });
    return h(ContextMenuPanel, { state, command(action, data) {
      sent.push({ action, data });
      const updates = action === 'save-items' ? data.items : action === 'save-item' ? [data] : [];
      setState(current => ({ ...current, entries: current.entries.map(entry => {
        const update = updates.find(item => item.ids.includes(entry.id));
        return update ? { ...entry, ...update.rule } : entry;
      }) }));
    } });
  }
  const view = await mount(h(Harness));
  await type(view.container.querySelector('.context-toolbar input[type="search"]'), 'Windows');
  const button = text => [...view.container.querySelectorAll('button')].find(button => button.textContent === text);
  const allSwitches = value => {
    const toggles = [...view.container.querySelectorAll('.context-entry .context-locations input[type="checkbox"]')];
    return toggles.length > 0 && toggles.every(toggle => toggle.checked === value);
  };
  try {
    await type(view.container.querySelector('input[aria-label$="görünen adı"]'), 'Yeni özellikler');
    await click(button('Tümünü kapat'));
    assert.equal(sent.length, 1);
    assert.equal(sent[0].action, 'save-items');
    assert.equal(sent[0].data.items.length, 2);
    assert.equal(sent[0].data.items[0].rule.name, 'Yeni özellikler');
    assert.ok(allSwitches(false));
    await click(button('Tümünü aç'));
    assert.ok(allSwitches(true));
    await type(view.container.querySelector('[aria-label="Menü öğesi ara"]'), 'Yeni özellikler');
    await click(button('Tümünü kapat'));
    assert.deepEqual(sent.at(-1).data.items.map(item => item.ids), [['builtin:properties']]);
    await click(button('Tüm konumları aç'));
    assert.equal(sent.at(-1).action, 'save-item');
    assert.ok(allSwitches(true));
    await click(button('Tüm konumları kapat'));
    assert.ok(allSwitches(false));
    await type(view.container.querySelector('[aria-label="Menü öğesi ara"]'), 'Kopyala');
    assert.ok(allSwitches(true));
    const count = sent.length;
    await act(async () => new Promise(resolve => setTimeout(resolve, 600)));
    assert.equal(sent.length, count, 'pending autosave must not undo the batch');
    await type(view.container.querySelector('[aria-label="Menü öğesi ara"]'), 'Bulunamadı');
    assert.equal(button('Tümünü aç').disabled, true);
    assert.equal(button('Tümünü kapat').disabled, true);
  } finally { await view.close(); }
});

test('context menu separates shortcuts and preserves separator placement and global options', async () => {
  const sent = [];
  const entry = { id: 'builtin:copy', originalName: 'Kopyala', name: 'Kopyala', visible: true, group: '', icon: '', source: 'Windows komutu', hasOriginalIcon: false, contexts: { file: false, folder: true } };
  const separator = { ...entry, id: 'separator:ABC', identity: 'separator:ABC', originalName: 'Ayırıcı: Kopyala / Sil', name: 'Ayırıcı: Kopyala / Sil', isSeparator: true, contexts: undefined };
  const view = await mount(h(ContextMenuPanel, { state: { kind: 'context-menu', groups: [], entries: [entry, separator] }, command: (action, data) => sent.push({ action, data }) }));
  try {
    await type(view.container.querySelector('.context-toolbar input[type="search"]'), 'Kopyala');
    const copy = view.container.querySelector('[data-entry-id="builtin:copy"]');
    const shortcut = copy.querySelector('[aria-label="Kopyala: Kısayolda"]');
    assert.equal(shortcut.checked, false, 'old file settings are inherited by shortcut scope');
    await click(shortcut);
    assert.equal(sent.at(-1).data.rule.contexts.shortcut, true);
    assert.equal(sent.at(-1).data.rule.contexts.file, false);
    await click(copy.querySelector('[aria-label="Kopyala: Önüne ayırıcı ekle"]'));
    assert.equal(sent.at(-1).data.rule.separatorBefore, true);
    await click(copy.querySelector('[aria-label="Kopyala: Arkasına ayırıcı ekle"]'));
    assert.equal(sent.at(-1).data.rule.separatorBefore, true);
    assert.equal(sent.at(-1).data.rule.separatorAfter, true);
    assert.equal(sent.at(-1).data.rule.contexts.shortcut, true);
    const line = view.container.querySelector('[data-entry-id="separator:ABC"]');
    assert.equal(line.querySelector('input:not([type="checkbox"])'), null, 'separators do not expose a command name or icon editor');
    await click(line.querySelector('[aria-label$="Kısayolda"]'));
    assert.equal(sent.at(-1).data.ids[0], 'separator:ABC');
    await click(view.container.querySelector('[aria-label="Yol olarak kopyala: Tırnak işaretlerini kaldır"]'));
    assert.equal(sent.at(-1).action, 'save-options');
    assert.equal(sent.at(-1).data.unquotedCopyPath, false);
    assert.equal(sent.at(-1).data.cleanSeparators, true);
    await click(view.container.querySelector('[aria-label="Boşta kalan ayırıcı çizgileri otomatik temizle"]'));
    assert.equal(sent.at(-1).data.cleanSeparators, false);
    assert.equal(sent.at(-1).data.unquotedCopyPath, false);
    const filter = view.container.querySelector('[aria-label="Menü öğesi türü"]');
    await act(async () => { filter.value = 'separators'; filter.dispatchEvent(new window.Event('change', { bubbles: true })); });
    assert.equal(view.container.querySelectorAll('.context-entry').length, 1);
    assert.ok(view.container.querySelector('[data-entry-id="separator:ABC"]'));
  } finally { await view.close(); }
});

test('context groups are created separately with a member selection', async () => {
  const sent = [];
  const entry = { id: 'builtin:properties', originalName: 'Özellikler', name: 'Özellikler', visible: true, group: '', icon: '', source: 'Windows komutu', hasOriginalIcon: true };
  const view = await mount(h(ContextMenuPanel, { state: { kind: 'context-menu', groups: [], entries: [entry] }, command: (action, data) => sent.push({action, data}) }));
  try {
    await click([...view.container.querySelectorAll('button')].find(button => button.textContent === 'Grup oluştur'));
    await type(view.container.querySelector('[aria-label="Grup adı"]'), 'Diğer');
    await click(view.container.querySelector('.context-group-choices input'));
    await act(async () => view.container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.equal(sent.at(-1).action, 'save-group');
    assert.equal(sent.at(-1).data.name, 'Diğer');
    assert.deepEqual(sent.at(-1).data.ids, ['builtin:properties']);
  } finally { await view.close(); }
});

test('native recorder commits Windows and side mouse chords without a browser key event', async () => {
  for (const shortcut of ['Win+D', 'Alt+MouseX1']) {
    const saved = [], messages = [];
    const listeners = new Set();
    window.chrome = { webview: {
      postMessage(message) { messages.push(message.command); },
      addEventListener(type, handler) { listeners.add(handler); },
      removeEventListener(type, handler) { listeners.delete(handler); },
    } };
    function Harness() {
      const [value, setValue] = useState('Ctrl+Space');
      return h(ShortcutField, { value, onChange: setValue, onCommit: next => saved.push(next), label: 'Native capture', allowMouse: true });
    }
    const view = await mount(h(Harness));
    try {
      await click(view.container.querySelector('button'));
      assert.ok(messages.includes('shortcut.record-begin'));
      await act(async () => { for (const listener of listeners) listener({ data: { type: 'shortcut-recorded', shortcut } }); });
      assert.deepEqual(saved, [shortcut]);
      assert.equal(view.container.querySelector('input').value, shortcut);
      assert.equal(messages.at(-1), 'shortcut.capture-end');
    } finally { await view.close(); }
    assert.equal(listeners.size, 0);
  }
});

test('module families preserve all modules and their independent identities', async () => {
  const { groupModules } = await server.ssrLoadModule('/src/moduleGroups.ts');
  const modules = [{id:'taskbar',category:'Taskbar'}, {id:'net-speed',category:'System'}, {id:'default-paths',category:'System'}, {id:'default-apps',category:'System'}];
  const groups = groupModules(modules);
  assert.equal(groups.length, 2);
  assert.deepEqual(groups[0].modules.map(m => m.id), ['taskbar', 'net-speed']);
  assert.deepEqual(groups.flatMap(g => g.modules), modules);
});


const { OsdControls } = await server.ssrLoadModule('/src/features/OsdControls.tsx');

for (const kind of ['volume', 'brightness']) {
  test(`${kind} slider updates immediately and preserves a drag across delayed host states`, async () => {
    const messages = [];
    const send = (command, payload) => messages.push({ command, payload });
    let update;
    function Harness() {
      const [percent, setPercent] = useState(40);
      update = setPercent;
      return h(OsdControls, { kind, percent, send });
    }
    const ui = await mount(h(Harness));
    try {
      const slider = ui.container.querySelector('input');
      slider.setPointerCapture = () => {};
      await act(async () => slider.dispatchEvent(new window.MouseEvent('pointerdown', { bubbles: true, button: 0 })));
      await type(slider, '73');
      assert.equal(slider.value, '73');
      assert.equal(ui.container.querySelector('strong').textContent, '73%');
      assert.deepEqual(messages.at(-1), { command: `${kind}.set`, payload: { percent: 73 } });
      await act(async () => update(48));
      assert.equal(slider.value, '73');
      await act(async () => slider.dispatchEvent(new window.Event('lostpointercapture', { bubbles: true })));
      assert.deepEqual(messages.at(-1), { command: `${kind}.drag`, payload: { value: false } });
      await act(async () => update(80));
      assert.equal(slider.value, '80');
      await key(slider, 'Escape', 'Escape');
      assert.equal(messages.at(-1).command, `${kind}.dismiss`);
    } finally { await ui.close(); }
  });
}

test('volume icon toggles mute and exposes the current state', async () => {
  const messages = [];
  const ui = await mount(h(OsdControls, { kind: 'volume', percent: 40, isMuted: true, send: command => messages.push(command) }));
  try {
    const button = ui.container.querySelector('button');
    assert.equal(button.getAttribute('aria-pressed'), 'true');
    assert.equal(button.getAttribute('aria-label'), 'Sesi a\u00e7');
    await click(button);
    assert.deepEqual(messages, ['volume.toggle-mute']);
  } finally { await ui.close(); }
});


test('group icons can be edited, picked, cleared and previewed independently of item icons', async () => {
  const sent = [];
  const state = { kind: 'context-menu', groups: ['Tools'], entries: [],
    groupIcons: { Tools: { icon: 'shell32.dll,3', iconPreview: 'data:image/png;base64,AA==' } } };
  const view = await mount(h(ContextMenuPanel, { state, command: (action, data) => sent.push({ action, data }) }));
  try {
    assert.equal(view.container.querySelectorAll('.context-group-title img, .context-preview img').length, 2);
    const buttons = view.container.querySelectorAll('.context-group-row button');
    await click(buttons[1]);
    assert.deepEqual(sent.pop(), { action: 'pick-group-icon', data: { name: 'Tools' } });
    await click(buttons[2]);
    assert.deepEqual(sent.pop(), { action: 'clear-group-icon', data: { name: 'Tools' } });
    await click(buttons[0]);
    const inputs = view.container.querySelectorAll('.context-group-editor input');
    assert.equal(inputs[1].value, 'shell32.dll,3');
    await type(inputs[0], 'Utilities'); await type(inputs[1], 'shell32.dll,4');
    await act(async () => view.container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.deepEqual(sent.pop(), { action: 'save-group', data: { name: 'Utilities', previousName: 'Tools', icon: 'shell32.dll,4', ids: [] } });
    assert.equal(state.groupIcons.Tools.icon, 'shell32.dll,3');
  } finally { await view.close(); }
});
