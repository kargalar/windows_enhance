import { useEffect, useRef, useState } from 'react';

type ClipboardCard = { id: string; kind: string; title: string; description: string; created: string; tags: string[]; pinned: boolean; saved: boolean; preview: string };
type ClipboardRow = { index: number; title: string; visible: boolean; count: number; page: number; pageCount: number; items: ClipboardCard[] };
type ClipboardAction = { id: number; name: string };
export type ClipboardShelfState = {
  libraryMode: boolean; status: string; query: string; searchMode: number; pinnedOnly: boolean; trash: boolean; selectMode: boolean;
  type: number; date: number; domain: string; tagsFilter: string; sort: number; scope: string; selected: string[];
  title: string; content: string; notes: string; itemTags: string; inspectorEnabled: boolean; inspectorContentReadOnly: boolean;
  folders: { id: string; name: string; depth: number; selected: boolean }[]; tagList: { name: string; selected: boolean }[];
  actions: ClipboardAction[]; rows: ClipboardRow[]; items: ClipboardCard[]; page: number; pageCount: number;
  listView: boolean; cardSize: string; showImages: boolean; showTexts: boolean; showLinks: boolean; count: number; selectionCount: number; searchFocusGeneration: number; actionsOpenGeneration: number; folderPickerGeneration: number; folderPickerPin: boolean; deleteConfirmGeneration: number; permanentDeleteCount: number;
};
export type ClipboardPreviewState = { file: string; index: number; count: number; kind: 'image' | 'link' | 'recording' | 'text' | 'message'; message: string; image: string; mediaUrl: string; text: string; author: string; zoom: number; editingText: boolean; canPrevious: boolean; canNext: boolean; canEditImage: boolean; canEditText: boolean; canOpenLink: boolean; canTrim: boolean };
export type ClipboardSettingsState = {
  captureText: boolean; captureImages: boolean; captureLinks: boolean; fetchLinkMetadata: boolean; indexImages: boolean;
  showImages: boolean; showTexts: boolean; showLinks: boolean; listView: boolean; sounds: boolean;
  imageRetentionDays: number; textRetentionDays: number; linkRetentionDays: number; cardSize: string;
  watchFolders: string[]; shortcuts: string; status: string;
};
export type ClipboardPromptState = { title: string; value: string };
export type RecordingTrimState = { file: string; mediaUrl: string; duration: number; start: string; end: string; progress: number; status: string; working: boolean; ready: boolean };
export function ClipboardShelfView({ state }: { state: ClipboardShelfState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const [actionsOpen, setActionsOpen] = useState(false);
  const [folderChooserOpen, setFolderChooserOpen] = useState(false);
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
  useEffect(() => { if (state.searchFocusGeneration > 0) document.querySelector<HTMLInputElement>('.clipboard-search input')?.focus(); }, [state.searchFocusGeneration]);
  useEffect(() => { if (state.actionsOpenGeneration > 0) setActionsOpen(true); }, [state.actionsOpenGeneration]);
  useEffect(() => { if (state.folderPickerGeneration > 0) setFolderChooserOpen(true); }, [state.folderPickerGeneration]);
  useEffect(() => { if (state.deleteConfirmGeneration > 0) setDeleteConfirmOpen(true); }, [state.deleteConfirmGeneration]);
  const runAction = (id: number) => {
    if (state.actions.find(item => item.id === id)?.name === 'Klasöre ekle') {
      setFolderChooserOpen(true);
      setActionsOpen(false);
      return;
    }
    send('clipboard.action', { id });
  };
  const action = (name: string) => state.actions.find(item => item.name === name);
  const selected = new Set(state.selected);
  const selectedFolder = state.folders.find(folder => folder.selected);
  const selectedTag = state.tagList.find(tag => tag.selected);
  const chooseItem = (item: ClipboardCard, event: React.MouseEvent) => {
    if (state.selectMode || state.libraryMode || event.ctrlKey || event.metaKey) {
      const next = event.ctrlKey || event.metaKey ? selected.has(item.id) ? state.selected.filter(id => id !== item.id) : [...state.selected, item.id] : [item.id];
      send('clipboard.select', { ids: next });
      return;
    }
    send('clipboard.quick-copy', { ids: [item.id] });
  };
  const renderCard = (item: ClipboardCard) => <button key={item.id} className={`clipboard-card ${selected.has(item.id) ? 'selected' : ''}`} onClick={event => chooseItem(item, event)} onDoubleClick={() => send('clipboard.preview', { ids: [item.id] })} title={item.title}>
    {item.preview ? <img src={item.preview} alt="" /> : <span className={`clipboard-card-icon kind-${item.kind}`}>{item.kind === 'image' ? '▧' : item.kind === 'recording' ? '◉' : item.kind === 'link' ? '↗' : 'T'}</span>}
    <strong>{item.pinned ? '★ ' : ''}{item.title}</strong>{item.description && <small>{item.description}</small>}{item.tags.length > 0 && <small className="clipboard-tags">{item.tags.join(' · ')}</small>}<small className="clipboard-created">{item.created}</small>
  </button>;
  const primaryNames = ['Kopyala', 'Yapıştır', 'Önizle', 'Sabitle', 'Kaydı kırp', 'Klasöre ekle', 'Dışa aktar', 'Etiket ekle', 'Ekran al'];
  const primaryActions = primaryNames.map(name => action(name)).filter((item): item is ClipboardAction => item !== undefined);
  const selectionRequired = new Set(['Kopyala', 'Yapıştır', 'Önizle', 'Sabitle', 'Kaydı kırp', 'Klasöre ekle', 'Adlandır ve sabitle', 'Dışa aktar', 'Etiket ekle', 'Tanı nan metni kopyala', 'Bağlantıyı aç', 'Bağlantı bilgilerini yenile', 'Raftan kaldır', 'Çöpe taşı', 'Geri yükle']);
  const actionButton = (item: ClipboardAction) => <button key={item.id} disabled={selectionRequired.has(item.name) && state.selectionCount === 0} onClick={() => runAction(item.id)}>{item.name}</button>;
  const filter = (key: string, value: unknown) => send('clipboard.filter', { key, value });
  const modeButton = (library: boolean) => <button className={state.libraryMode === library ? 'active' : ''} onClick={() => send('clipboard.mode', { library })}>{library ? 'Kitaplık' : 'Raflar'}</button>;
  const listOptions = <div className="clipboard-view-options"><label><input type="checkbox" checked={state.showImages} onChange={event => send('clipboard.preference', { key: 'showImages', value: event.target.checked })} /> Görseller</label><label><input type="checkbox" checked={state.showTexts} onChange={event => send('clipboard.preference', { key: 'showTexts', value: event.target.checked })} /> Metinler</label><label><input type="checkbox" checked={state.showLinks} onChange={event => send('clipboard.preference', { key: 'showLinks', value: event.target.checked })} /> Bağlantılar</label></div>;
  return <div className="feature-root feature-root-clipboard" tabIndex={0} onKeyDown={event => { const target = event.target; if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target instanceof HTMLButtonElement && (event.key === ' ' || event.key === 'Enter') || event.repeat) return; if (event.key === ' ' || event.key.startsWith('Arrow')) event.preventDefault(); send('clipboard.key', { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey }); }}>
    <header className="clipboard-topbar"><div className="clipboard-brand"><span>▤</span><strong>ClipBoardLine</strong></div><nav>{modeButton(false)}{modeButton(true)}</nav><label className="clipboard-search"><span>⌕</span><input value={state.query} onChange={event => send('clipboard.search', { value: event.target.value })} placeholder="Metin, OCR, başlık, etiket ara" /><select value={state.searchMode} onChange={event => filter('searchMode', Number(event.target.value))}><option value={0}>Metin / OCR</option><option value={1}>Görsel</option><option value={2}>Metin + görsel</option></select></label><button className="clipboard-gear" onClick={() => action('Ayarlar') && runAction(action('Ayarlar')!.id)}>Ayarlar</button></header>
    <section className="clipboard-filters"><label><input type="checkbox" checked={state.pinnedOnly} onChange={event => filter('pinned', event.target.checked)} /> Sabitlenenler</label><label><input type="checkbox" checked={state.trash} onChange={event => filter('trash', event.target.checked)} /> Çöp</label><label><input type="checkbox" checked={state.selectMode} onChange={event => filter('selectMode', event.target.checked)} /> Çoklu seçim</label><select value={state.type} onChange={event => filter('type', Number(event.target.value))}><option value={0}>Tüm türler</option><option value={1}>Görseller</option><option value={2}>Metinler</option><option value={3}>Bağlantılar</option><option value={4}>Kayıtlar</option></select><select value={state.date} onChange={event => filter('date', Number(event.target.value))}><option value={0}>Tüm tarihler</option><option value={1}>Bugün</option><option value={2}>Son 7 gün</option><option value={3}>Son 30 gün</option></select><input value={state.domain} onChange={event => filter('domain', event.target.value)} placeholder="Alan adı" /><input value={state.tagsFilter} onChange={event => filter('tags', event.target.value)} placeholder="Etiketler (virgülle)" /><select value={state.sort} onChange={event => filter('sort', Number(event.target.value))}><option value={0}>Varsayılan</option><option value={1}>Son kopyalanan</option><option value={2}>Başlık A–Z</option><option value={3}>Başlık Z–A</option><option value={4}>En eski</option><option value={5}>En yeni</option><option value={6}>Tür</option><option value={7}>Etiketler</option></select></section>
    <section className="clipboard-actions"><div>{primaryActions.map(actionButton)}</div><button className={actionsOpen ? 'active' : ''} onClick={() => setActionsOpen(value => !value)}>Tüm eylemler {actionsOpen ? '⌃' : '⌄'}</button>{actionsOpen && <div className="clipboard-action-menu">{state.actions.filter(item => !primaryNames.includes(item.name) && item.name !== 'Eylemler' && item.name !== 'Ayarlar').map(actionButton)}</div>}</section>
    {state.libraryMode ? <div className="clipboard-library-layout"><aside className="clipboard-sidebar"><strong>Görünümler</strong>{[['all', 'Tümü'], ['saved', 'Kaydedilenler'], ['recent', 'Son kopyalananlar'], ['unorganized', 'Düzenlenmemiş']].map(([value, label]) => <button key={value} className={state.scope === value ? 'active' : ''} onClick={() => send('clipboard.scope', { value })}>{label}</button>)}<div className="clipboard-sidebar-heading"><strong>Klasörler</strong></div>{state.folders.map(folder => <button key={folder.id} className={folder.selected ? 'active' : ''} style={{ paddingLeft: 9 + folder.depth * 12 }} onClick={() => send('clipboard.folder', { id: folder.id })}>{folder.name}</button>)}<div className="clipboard-sidebar-heading"><strong>Etiketler</strong></div>{state.tagList.map(tag => <button key={tag.name} className={tag.selected ? 'active' : ''} onClick={() => send('clipboard.tag', { value: tag.name })}># {tag.name}</button>)}{listOptions}<label className="clipboard-select-field"><span>Kart boyutu</span><select value={state.cardSize} onChange={event => send('clipboard.preference', { key: 'cardSize', value: event.target.value })}><option value="small">Küçük</option><option value="medium">Orta</option><option value="large">Büyük</option></select></label><label><input type="checkbox" checked={state.listView} onChange={event => send('clipboard.preference', { key: 'listView', value: event.target.checked })} /> Liste görünümü</label></aside><main className={`clipboard-library-content ${state.listView ? 'list-mode' : ''}`}><div className="clipboard-results-heading"><strong>{state.count} kayıt</strong><span>Sayfa {state.page + 1} / {state.pageCount}</span><div><button disabled={state.page <= 0} onClick={() => send('clipboard.page', { row: 0, delta: -1 })}>Önceki</button><button disabled={state.page + 1 >= state.pageCount} onClick={() => send('clipboard.page', { row: 0, delta: 1 })}>Sonraki</button></div></div><div className="clipboard-card-grid">{state.items.map(renderCard)}{state.items.length === 0 && <p className="clipboard-empty">Bu görünümde kayıt yok.</p>}</div></main><aside className="clipboard-inspector"><strong>Kayıt ayrıntıları</strong>{!state.inspectorEnabled ? <p>Bir kayıt seçerek ayrıntılarını düzenle.</p> : <><label>Başlık<input value={state.title} onChange={event => send('clipboard.detail', { key: 'title', value: event.target.value })} /></label><label>İçerik<textarea readOnly={state.inspectorContentReadOnly} value={state.content} onChange={event => send('clipboard.detail', { key: 'content', value: event.target.value })} /></label><label>Notlar<textarea value={state.notes} onChange={event => send('clipboard.detail', { key: 'notes', value: event.target.value })} /></label><label>Etiketler<input value={state.itemTags} onChange={event => send('clipboard.detail', { key: 'tags', value: event.target.value })} /></label><small>Değişiklikler otomatik kaydedilir.</small></>}</aside></div> : <main className="clipboard-shelves">{state.rows.filter(row => row.visible).map(row => <section className="clipboard-shelf-row" key={row.index}><header><strong>{row.title}</strong><span>{row.count} kayıt · {row.page + 1}/{row.pageCount}</span><button disabled={row.page <= 0} onClick={() => send('clipboard.page', { row: row.index, delta: -1 })}>‹</button><button disabled={row.page + 1 >= row.pageCount} onClick={() => send('clipboard.page', { row: row.index, delta: 1 })}>›</button></header><div className="clipboard-card-strip">{row.items.map(renderCard)}{row.items.length === 0 && <p className="clipboard-empty">Henüz kayıt yok.</p>}</div></section>)}</main>}
    {state.libraryMode && selectedFolder && <div className="clipboard-selection-tools"><span>Klasör: {selectedFolder.name}</span><button onClick={() => send('clipboard.folder.rename', { id: selectedFolder.id })}>Yeniden adlandır</button><button className="danger" onClick={() => send('clipboard.folder.remove', { id: selectedFolder.id })}>Klasörü kaldır</button></div>}
    {state.libraryMode && selectedTag && <div className="clipboard-selection-tools"><span>Etiket: #{selectedTag.name}</span><button onClick={() => send('clipboard.tag.rename', { name: selectedTag.name })}>Yeniden adlandır</button><button className="danger" onClick={() => send('clipboard.tag.remove', { name: selectedTag.name })}>Etiketi kaldır</button></div>}
    <footer className="clipboard-status"><span role="status">{state.status}</span><span>{state.selectionCount ? `${state.selectionCount} seçili` : 'Space · Önizle  Enter · Yapıştır  P · Sabitle'}</span></footer>
    {folderChooserOpen && <div className="clipboard-folder-picker-backdrop"><section className="clipboard-folder-picker" role="dialog" aria-modal="true" aria-labelledby="clipboard-folder-picker-title">
      <header><div><span className="nashi-eyebrow">KÜTÜPHANE</span><h2 id="clipboard-folder-picker-title">{state.folderPickerPin ? 'Klasöre ekle ve sabitle' : 'Klasöre ekle'}</h2></div><button onClick={() => setFolderChooserOpen(false)} aria-label="Kapat">×</button></header>
      <div>{state.folders.map(folder => <button key={folder.id} style={{ paddingLeft: 10 + folder.depth * 12 }} onClick={() => { send('clipboard.assign-folder', { id: folder.id, ids: state.selected }); setFolderChooserOpen(false); }}>{folder.name}</button>)}{state.folders.length === 0 && <p>Henüz klasör yok.</p>}</div>
      <footer><button onClick={() => send('clipboard.folder.create', { ids: state.selected })}>＋ Yeni klasör</button><button onClick={() => setFolderChooserOpen(false)}>Vazgeç</button></footer>
    </section></div>}
    {deleteConfirmOpen && <div className="clipboard-folder-picker-backdrop"><section className="clipboard-delete-confirm" role="alertdialog" aria-modal="true" aria-labelledby="clipboard-delete-title">
      <span className="clipboard-delete-icon">!</span><h2 id="clipboard-delete-title">Kayıtlar kalıcı olarak silinsin mi?</h2><p>{state.permanentDeleteCount} kayıt geri alınamayacak şekilde silinecek.</p><footer><button onClick={() => { send('clipboard.delete-confirm', { confirm: false }); setDeleteConfirmOpen(false); }}>Vazgeç</button><button className="danger" onClick={() => { send('clipboard.delete-confirm', { confirm: true }); setDeleteConfirmOpen(false); }}>Kalıcı sil</button></footer>
    </section></div>}
  </div>;
}

export function ClipboardSettingsView({ state }: { state: ClipboardSettingsState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const toggle = (key: keyof ClipboardSettingsState, label: string, detail: string) => <label className="clipboard-settings-toggle" key={key}>
    <span><strong>{label}</strong><small>{detail}</small></span>
    <input type="checkbox" checked={Boolean(state[key])} onChange={event => send('clipboard-settings.toggle', { key, value: event.target.checked })} />
  </label>;
  const retention = (key: 'imageRetentionDays' | 'textRetentionDays' | 'linkRetentionDays', label: string) => <label className="clipboard-settings-select" key={key}>
    <span>{label}</span><select value={state[key]} onChange={event => send('clipboard-settings.select', { key, value: Number(event.target.value) })}>
      {[1, 3, 7, 15, 30, state[key]].filter((value, index, values) => values.indexOf(value) === index).sort((a, b) => a - b).map(days => <option key={days} value={days}>{days} gün</option>)}
    </select>
  </label>;
  return <div className="feature-root feature-root-clipboard-settings"><main>
    <header><div><span className="nashi-eyebrow">KİŞİSELLEŞTİR</span><h1>Pano ayarları</h1><p>Yakalama, arşiv ve yerel kısayolları düzenle.</p></div><button onClick={() => send('clipboard-settings.close')} aria-label="Kapat">×</button></header>
    <section className="clipboard-settings-scroll">
      <h2>Yakalama ve gizlilik</h2>
      {toggle('captureText', 'Metinleri yakala', 'Kopyaladığın metinleri bu cihazda sakla.')}
      {toggle('captureImages', 'Görselleri yakala', 'Kopyalanan görseller yerel pano geçmişine eklenir.')}
      {toggle('captureLinks', 'Bağlantıları yakala', 'Tek başına kopyalanan bağlantıları da ekle.')}
      {toggle('fetchLinkMetadata', 'Bağlantı önizlemeleri', 'Genel HTTPS sayfalarından başlık ve küçük resim al.')}
      {toggle('indexImages', 'Görsellerde OCR', 'Görsel metinlerini cihazda indeksle.')}
      <h2>Görünüm</h2>
      {toggle('showImages', 'Görsel rafı', 'Görsel kayıtlarını ana rafta göster.')}
      {toggle('showTexts', 'Metin rafı', 'Metin kayıtlarını ana rafta göster.')}
      {toggle('showLinks', 'Bağlantı rafı', 'Bağlantı kayıtlarını ana rafta göster.')}
      {toggle('listView', 'Liste görünümü', 'Kartlar yerine yoğun liste düzeni kullan.')}
      {toggle('sounds', 'Kopyalama sesi', 'Bir kayıt panoya kopyalandığında ses çal.')}
      <label className="clipboard-settings-select"><span>Kart boyutu</span><select value={state.cardSize} onChange={event => send('clipboard-settings.select', { key: 'cardSize', value: event.target.value })}><option value="small">Küçük</option><option value="medium">Orta</option><option value="large">Büyük</option></select></label>
      <h2>Saklama süresi</h2>
      {retention('imageRetentionDays', 'Görseller')}{retention('textRetentionDays', 'Metinler')}{retention('linkRetentionDays', 'Bağlantılar')}
      <h2>İzlenen klasörler <small>(en fazla 3)</small></h2>
      <div className="clipboard-settings-folders">{state.watchFolders.map((folder, index) => <div key={`${folder}-${index}`}><span title={folder}>{folder}</span><button onClick={() => send('clipboard-settings.folder-remove', { index })} aria-label="Klasörü kaldır">Kaldır</button></div>)}{state.watchFolders.length < 3 && <button onClick={() => send('clipboard-settings.folder-add')}>＋ Klasör ekle</button>}</div>
      <h2>Kısayollar</h2>
      <p className="clipboard-settings-help">Her satıra <code>eylem=tuşlar</code> yazın. Boş değer ilgili kısayolu kapatır.</p>
      <textarea className="clipboard-settings-shortcuts" spellCheck={false} value={state.shortcuts} onChange={event => send('clipboard-settings.shortcuts', { value: event.target.value })} />
    </section>
    <footer><span role="status">{state.status}</span><div><button onClick={() => send('clipboard-settings.close')}>Kapat</button></div></footer>
  </main></div>;
}

export function ClipboardPromptView({ state }: { state: ClipboardPromptState }) {
  const send = (command: string, value?: string) => window.chrome?.webview?.postMessage({ command, payload: value === undefined ? {} : { value } });
  return <div className="feature-root feature-root-clipboard-prompt"><main>
    <header><div><span className="nashi-eyebrow">PANO KÜTÜPHANESİ</span><h1>{state.title}</h1></div><button onClick={() => send('clipboard-prompt.cancel')} aria-label="Kapat">×</button></header>
    <input autoFocus value={state.value} onChange={event => send('clipboard-prompt.change', event.target.value)} onKeyDown={event => { if (event.key === 'Enter') send('clipboard-prompt.save'); if (event.key === 'Escape') send('clipboard-prompt.cancel'); }} />
    <footer><button onClick={() => send('clipboard-prompt.cancel')}>Vazgeç</button><button className="nashi-primary" onClick={() => send('clipboard-prompt.save')}>Tamam</button></footer>
  </main></div>;
}

export function ClipboardPreviewView({ state }: { state: ClipboardPreviewState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const editorRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { if (state.editingText) editorRef.current?.focus(); }, [state.editingText, state.index]);
  const keyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const target = event.target;
    const editing = target instanceof HTMLElement && ['INPUT', 'TEXTAREA'].includes(target.tagName);
    if (editing) {
      if (event.key === 'Escape' || event.ctrlKey && event.key === 'Enter') {
        event.preventDefault(); send('clipboard-preview.key', { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey });
      }
      return;
    }
    if (['Escape', ' ', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || event.ctrlKey || event.altKey || event.shiftKey) {
      if (event.key.startsWith('Arrow') || event.key === ' ') event.preventDefault();
      send('clipboard-preview.key', { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey });
    }
  };
  return <div className="feature-root feature-root-clipboard-preview" onKeyDown={keyDown} tabIndex={0} autoFocus><main><header><div className="clipboard-preview-nav"><button disabled={!state.canPrevious} onClick={() => send('clipboard-preview.navigate', { delta: -1 })}>‹</button><div><strong title={state.file}>{state.file}</strong><small>{state.index} / {state.count}</small></div><button disabled={!state.canNext} onClick={() => send('clipboard-preview.navigate', { delta: 1 })}>›</button></div><div className="clipboard-preview-actions">
    {state.canEditImage && <><button onClick={() => send('clipboard-preview.edit-image', { tool: 'crop' })}>Kırp</button><button onClick={() => send('clipboard-preview.edit-image', { tool: 'annotate' })}>İşaretle</button><label className="clipboard-preview-zoom">{Math.round(state.zoom * 100)}%<input type="range" min="1" max="4" step="0.05" value={state.zoom} onChange={event => send('clipboard-preview.zoom', { value: Number(event.target.value) })} /></label></>}
    {state.canEditText && <button onClick={() => send(state.editingText ? 'clipboard-preview.text-finish' : 'clipboard-preview.text-edit')}>{state.editingText ? 'Düzenlemeyi bitir' : 'Düzenle'}</button>}
    {state.canTrim && <button onClick={() => send('clipboard-preview.trim')}>Kaydı kırp</button>}{state.canOpenLink && <button onClick={() => send('clipboard-preview.open-link')}>Bağlantıyı aç ↗</button>}
    <button onClick={() => send('clipboard-preview.copy')}>Kopyala</button><button onClick={() => send('clipboard-preview.paste')}>Yapıştır</button><button className="clipboard-preview-close" onClick={() => send('clipboard-preview.close')}>×</button>
  </div></header><section className={`clipboard-preview-content kind-${state.kind}`} onWheel={event => { if (state.kind === 'image' && (event.ctrlKey || event.deltaY !== 0)) { event.preventDefault(); send('clipboard-preview.zoom', { value: Math.max(1, Math.min(4, state.zoom * (event.deltaY < 0 ? 1.12 : 1 / 1.12))) }); } }}>
    {state.kind === 'image' && <img src={state.image} alt={state.file} style={{ transform: `scale(${state.zoom})` }} onDoubleClick={() => send('clipboard-preview.zoom', { value: 1 })} />}
    {state.kind === 'link' && <article className="clipboard-preview-link">{state.image && <img src={state.image} alt="" />}<div>{state.author && <small>{state.author}</small>}<textarea ref={editorRef} readOnly={!state.editingText} value={state.text} onChange={event => send('clipboard-preview.text-change', { value: event.target.value })} /></div></article>}
    {state.kind === 'text' && <textarea ref={editorRef} className="clipboard-preview-text" readOnly={!state.editingText} value={state.text} onChange={event => send('clipboard-preview.text-change', { value: event.target.value })} />}
    {state.kind === 'recording' && <video src={state.mediaUrl} controls playsInline />}
    {state.kind === 'message' && <p>{state.message}</p>}
  </section><footer><span>{state.message || '← / → · Gezin  Esc · Kapat  Ctrl+Enter · Yapıştır'}</span><span>{state.kind === 'image' ? 'Tekerlekle yakınlaştır · Çift tıkla sığdır' : 'Değişiklikler otomatik kaydedilir'}</span></footer></main></div>;
}

export function RecordingTrimView({ state }: { state: RecordingTrimState }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [position, setPosition] = useState(0);
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const setPoint = (field: 'start' | 'end') => send('recording-trim.set-point', { field, position: videoRef.current?.currentTime ?? position });
  return <div className="feature-root feature-root-recording-trim" tabIndex={0} onKeyDown={event => { if (event.key === 'Escape') send('recording-trim.cancel'); else if (event.key === 'Enter' && !(event.target instanceof HTMLInputElement)) send('recording-trim.save', { start: state.start, end: state.end }); }}><main><header><div><span className="nashi-eyebrow">KAYIT DÜZENLEME</span><h1>Kayıt videosunu kırp</h1><p>{state.file} · Yeni kopya oluşturulur</p></div><button onClick={() => send('recording-trim.cancel')} aria-label="Kapat">×</button></header><video ref={videoRef} src={state.mediaUrl} controls playsInline onTimeUpdate={event => setPosition(event.currentTarget.currentTime)} /><label className="recording-trim-position"><span>{position.toFixed(2)} sn</span><input type="range" min="0" max={Math.max(.01, state.duration)} step="0.01" value={Math.min(position, state.duration || position)} onChange={event => { const time = Number(event.target.value); setPosition(time); if (videoRef.current) videoRef.current.currentTime = time; }} disabled={!state.ready || state.working} /></label><section className="recording-trim-range"><label><span>Başlangıç (saniye)</span><input inputMode="decimal" value={state.start} onChange={event => send('recording-trim.range', { start: event.target.value, end: state.end })} disabled={state.working} /></label><button onClick={() => setPoint('start')} disabled={!state.ready || state.working}>Geçerli konumu başlangıç yap</button><label><span>Bitiş (saniye)</span><input inputMode="decimal" value={state.end} onChange={event => send('recording-trim.range', { start: state.start, end: event.target.value })} disabled={state.working} /></label><button onClick={() => setPoint('end')} disabled={!state.ready || state.working}>Geçerli konumu bitiş yap</button></section><footer><div className="recording-trim-status" role="status">{state.status || 'Kırpılacak aralığı seçin.'}{state.working && <div className="progress-track"><i style={{ width: `${Math.max(0, Math.min(1, state.progress)) * 100}%` }} /></div>}</div><div><button disabled={state.working} onClick={() => send('recording-trim.cancel')}>{state.working ? 'İptal et' : 'Vazgeç'}</button><button className="nashi-primary" disabled={!state.ready || state.working} onClick={() => send('recording-trim.save', { start: state.start, end: state.end })}>{state.working ? 'Kırpılıyor…' : 'Kırp ve kaydet'}</button></div></footer></main></div>;
}

