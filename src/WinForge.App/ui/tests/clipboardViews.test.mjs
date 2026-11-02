import assert from 'node:assert/strict';
import test, { after } from 'node:test';
import { Window } from 'happy-dom';
import { createServer } from 'vite';

const window = new Window({ url: 'https://appassets.winforge/' });
for (const name of ['window', 'document', 'HTMLElement', 'HTMLInputElement', 'HTMLTextAreaElement', 'HTMLSelectElement', 'HTMLButtonElement', 'Node', 'Element', 'Event', 'KeyboardEvent', 'MouseEvent'])
  globalThis[name] = name === 'window' ? window : window[name];
globalThis.IS_REACT_ACT_ENVIRONMENT = true;
const { act, createElement: h } = await import('react');
const { createRoot } = await import('react-dom/client');
const server = await createServer({ server: { middlewareMode: true, hmr: false, ws: false }, appType: 'custom' });
const { FeatureApp } = await server.ssrLoadModule('/src/features/FeatureApp.tsx');
after(async () => { await server.close(); await window.happyDOM.close(); });

async function mount(view, state) {
  const commands = [];
  let receive;
  window.chrome = { webview: {
    addEventListener(_name, callback) { receive = callback; },
    removeEventListener() {}, postMessage(message) { commands.push(message); },
  } };
  const container = document.createElement('div'); document.body.append(container);
  const root = createRoot(container);
  await act(async () => root.render(h(FeatureApp, { view })));
  assert.deepEqual(commands.shift(), { command: 'feature.ready', payload: {} });
  await act(async () => receive({ data: { type: 'feature.state', payload: state } }));
  return { container, commands,
    async update(next) { await act(async () => receive({ data: { type: 'feature.state', payload: next } })); },
    async close() { await act(async () => root.unmount()); container.remove(); },
  };
}
async function click(node, modifiers) {
  assert.ok(node);
  await act(async () => modifiers ? node.dispatchEvent(new window.MouseEvent('click', { bubbles: true, ...modifiers })) : node.click());
}
async function key(node, key, modifiers = {}) {
  await act(async () => node.dispatchEvent(new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...modifiers })));
}

test('clipboard shelf keeps quick copy, multi-selection and preview commands distinct', async () => {
  const item = { id: 'item-1', kind: 'text', title: 'Saved text', description: '', created: 'Today', tags: [], pinned: false, saved: true, preview: '' };
  const state = { libraryMode: false, status: '', query: '', searchMode: 0, pinnedOnly: false, trash: false, selectMode: false,
    type: 0, date: 0, domain: '', tagsFilter: '', sort: 0, scope: 'all', selected: [], title: '', content: '', notes: '', itemTags: '',
    inspectorEnabled: false, inspectorContentReadOnly: false, folders: [], tagList: [], actions: [], items: [],
    rows: [{ index: 0, title: 'Text', visible: true, count: 1, page: 0, pageCount: 1, items: [item] }], page: 0, pageCount: 1,
    listView: false, cardSize: 'medium', showImages: true, showTexts: true, showLinks: true, count: 1, selectionCount: 0,
    searchFocusGeneration: 0, actionsOpenGeneration: 0, folderPickerGeneration: 0, folderPickerPin: false, deleteConfirmGeneration: 0, permanentDeleteCount: 0 };
  const ui = await mount('clipboard-shelf', state);
  try {
    const card = ui.container.querySelector('.clipboard-card');
    await click(card);
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard.quick-copy', payload: { ids: ['item-1'] } });
    await click(card, { ctrlKey: true });
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard.select', payload: { ids: ['item-1'] } });
    await act(async () => card.dispatchEvent(new window.MouseEvent('dblclick', { bubbles: true })));
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard.preview', payload: { ids: ['item-1'] } });
    await ui.update({ ...state, selected: ['item-1'], selectMode: true, selectionCount: 1 });
    await click(ui.container.querySelector('.clipboard-card'), { ctrlKey: true });
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard.select', payload: { ids: [] } });
  } finally { await ui.close(); }
});

test('clipboard text preview preserves editing keys and forwards explicit paste and close shortcuts', async () => {
  const state = { file: 'note.txt', index: 1, count: 1, kind: 'text', message: '', image: '', mediaUrl: '', text: 'Initial text',
    author: '', zoom: 1, editingText: true, canPrevious: false, canNext: false, canEditImage: false, canEditText: true, canOpenLink: false, canTrim: false };
  const ui = await mount('clipboard-preview', state);
  try {
    const editor = ui.container.querySelector('textarea');
    assert.equal(editor.value, 'Initial text');
    assert.equal(editor.readOnly, false);
    await key(editor, 'ArrowLeft');
    assert.equal(ui.commands.length, 0);
    await key(editor, 'Enter', { ctrlKey: true });
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard-preview.key', payload: { key: 'Enter', ctrl: true, shift: false, alt: false } });
    await key(editor, 'Escape');
    assert.equal(ui.commands.pop().command, 'clipboard-preview.key');
    assert.equal(ui.container.querySelector('.clipboard-preview-nav button').disabled, true);
    await ui.update({ ...state, editingText: false, text: 'Durable host text' });
    assert.equal(editor.readOnly, true);
    assert.equal(editor.value, 'Durable host text');
    await click(ui.container.querySelector('.clipboard-preview-close'));
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard-preview.close', payload: {} });
  } finally { await ui.close(); }
});

test('clipboard prompt and recording trim retain save and cancel behavior at the feature boundary', async () => {
  const prompt = await mount('clipboard-prompt', { title: 'Rename', value: 'Saved item' });
  try {
    await key(prompt.container.querySelector('input'), 'Enter');
    assert.deepEqual(prompt.commands.pop(), { command: 'clipboard-prompt.save', payload: {} });
    await key(prompt.container.querySelector('input'), 'Escape');
    assert.deepEqual(prompt.commands.pop(), { command: 'clipboard-prompt.cancel', payload: {} });
  } finally { await prompt.close(); }
  const state = { file: 'recording.mp4', mediaUrl: 'https://appassets.winforge/recording.mp4', duration: 10, start: '1.5', end: '8', progress: 0, status: '', working: false, ready: false };
  const trim = await mount('clipboard-recording-trim', state);
  try {
    assert.equal(trim.container.querySelector('.nashi-primary').disabled, true);
    await trim.update({ ...state, ready: true });
    await click(trim.container.querySelector('.nashi-primary'));
    assert.deepEqual(trim.commands.pop(), { command: 'recording-trim.save', payload: { start: '1.5', end: '8' } });
    await key(trim.container.querySelector('.feature-root'), 'Escape');
    assert.deepEqual(trim.commands.pop(), { command: 'recording-trim.cancel', payload: {} });
  } finally { await trim.close(); }
});

test('clipboard settings send explicit privacy choices and cap watched folders at three', async () => {
  const state = { captureText: true, captureImages: true, captureLinks: false, fetchLinkMetadata: false, indexImages: false,
    showImages: true, showTexts: true, showLinks: true, listView: false, sounds: false,
    imageRetentionDays: 3, textRetentionDays: 7, linkRetentionDays: 15, cardSize: 'medium',
    watchFolders: ['C:/one', 'C:/two', 'C:/three'], shortcuts: '', status: 'Ready' };
  const ui = await mount('clipboard-settings', state);
  try {
    const toggle = [...ui.container.querySelectorAll('.clipboard-settings-toggle')].find(label => label.textContent.includes('Bağlantı önizlemeleri'));
    await click(toggle.querySelector('input'));
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard-settings.toggle', payload: { key: 'fetchLinkMetadata', value: true } });
    assert.equal([...ui.container.querySelectorAll('button')].some(button => button.textContent.includes('Klasör ekle')), false);
    await click(ui.container.querySelector('[aria-label="Klasörü kaldır"]'));
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard-settings.folder-remove', payload: { index: 0 } });
    await ui.update({ ...state, watchFolders: [] });
    const add = [...ui.container.querySelectorAll('button')].find(button => button.textContent.includes('Klasör ekle'));
    await click(add);
    assert.deepEqual(ui.commands.pop(), { command: 'clipboard-settings.folder-add', payload: {} });
  } finally { await ui.close(); }
});
