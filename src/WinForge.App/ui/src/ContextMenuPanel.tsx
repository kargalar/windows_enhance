import { useImperativeHandle, useRef, useState, type Ref } from 'react';
import { useAutoSaveDraft } from './useAutoSaveDraft';
type Rule = { id: string; name: string; visible: boolean; group: string; icon: string; contexts?: Record<string, boolean>; separatorBefore?: boolean; separatorAfter?: boolean };
type Entry = Rule & { originalName: string; source: string; identity?: string; scope?: string; configured?: boolean; iconPreview?: string; hasOriginalIcon: boolean; isSeparator?: boolean };
type Item = Entry & { ids: string[]; sources: string[]; contexts: Record<string, boolean> };
export type ContextMenuState = { kind: 'context-menu'; groups: string[]; groupIcons?: Record<string, { icon: string; iconPreview?: string }>; entries: Entry[]; cleanSeparators?: boolean; showSeparators?: boolean; unquotedCopyPath?: boolean };
type Command = (action: string, data?: Record<string, unknown>) => void;
type VisibilityControl = { setVisibility: (visible: boolean) => void };
const locations = [['file', 'Dosyada'], ['folder', 'Klasörde'], ['background', 'Klasörün boş alanında'], ['desktop', 'Masaüstünün boş alanında'], ['drive', 'Sürücüde'], ['shortcut', 'Kısayolda'], ['application', 'Uygulamada veya betikte'], ['virtual', 'Sanal öğede']] as const;
const locationNames: Record<string, string> = { file: 'Dosya', folder: 'Klasör', background: 'Boş alan', desktop: 'Masaüstü', drive: 'Sürücü', shortcut: 'Kısayol', application: 'Uygulama / betik', virtual: 'Sanal öğe' };
export function combineEntries(entries: Entry[]): Item[] {
  const groups = new Map<string, Entry[]>();
  for (const entry of entries) {
    const identity = entry.identity || entry.originalName.replace(/&/g, '').trim().toLocaleLowerCase('tr');
    groups.set(identity, [...(groups.get(identity) || []), entry]);
  }
  return [...groups.values()].map(rows => {
    const primary = rows.find(row => row.contexts) || rows.find(row => row.configured && !row.scope) || rows.find(row => row.configured) || rows.find(row => !row.scope) || rows[0];
    const icon = rows.find(row => row.icon && row.iconPreview) || rows.find(row => row.iconPreview) || primary;
    const fileVisible = primary.contexts?.file ?? rows.find(row => row.scope === 'file')?.visible ?? primary.visible;
    const contexts = Object.fromEntries(locations.map(([scope]) => [scope, primary.contexts?.[scope] ?? rows.find(row => row.scope === scope)?.visible ?? ((scope === 'shortcut' || scope === 'application') ? fileVisible : primary.visible)]));
    return { ...primary, iconPreview: icon.iconPreview, hasOriginalIcon: rows.some(row => row.hasOriginalIcon), visible: Object.values(contexts).some(Boolean), contexts, ids: rows.map(row => row.id), sources: rows.map(row => row.source) };
  });
}
function MenuEntry({ entry, command, ref }: { entry: Item; command: Command; ref?: Ref<VisibilityControl> }) {
  const rule: Omit<Rule, 'group'> = { id: entry.id, name: entry.isSeparator ? '' : entry.name, visible: entry.visible, icon: entry.isSeparator ? '' : entry.icon, contexts: entry.contexts, separatorBefore: entry.separatorBefore ?? false, separatorAfter: entry.separatorAfter ?? false };
  const [draft, update, cancel] = useAutoSaveDraft(rule, next => command('save-item', { ids: entry.ids, rule: next }));
  const setVisibility = (visible: boolean) => update(previous => ({ ...previous, visible, contexts: Object.fromEntries(locations.map(([scope]) => [scope, visible])) }), true);
  useImperativeHandle(ref, () => ({ setVisibility }));
  return <article data-entry-id={entry.id} className={`context-entry ${draft.visible ? '' : 'is-off'}`}>
    <header><span className="context-entry-icon">{entry.iconPreview ? <img src={entry.iconPreview} alt="" /> : <span aria-label="Simge yok">◇</span>}</span>
      <div className="context-entry-title">{entry.isSeparator ? <strong>{entry.originalName}</strong> : <input className="white-control" aria-label={`${entry.originalName} görünen adı`} value={draft.name} maxLength={120} onChange={event => update({ ...draft, name: event.target.value })} />}</div>
      {entry.group && <small className="context-group-badge">{entry.group}</small>}
    </header>
    <div className="context-locations" aria-label={`${entry.originalName} görünürlük konumları`}>{locations.map(([scope, label]) => <label key={scope} title={label}><span>{locationNames[scope]}</span><input type="checkbox" aria-label={`${entry.originalName}: ${label}`} checked={draft.contexts?.[scope] ?? draft.visible} onChange={event => {
      const contexts = { ...draft.contexts, [scope]: event.target.checked };
      update({ ...draft, contexts, visible: Object.values(contexts).some(Boolean) }, true);
    }} /></label>)}</div>
    <div className="context-icon-controls" aria-label={`${entry.originalName} tüm konumlar`}><button className="secondary-button" onClick={() => setVisibility(true)}>Tüm konumları aç</button><button className="secondary-button" onClick={() => setVisibility(false)}>Tüm konumları kapat</button></div>
    {!entry.isSeparator && <details className="context-entry-details"><summary>Diğer ayarlar</summary>
      <label><input type="checkbox" aria-label={`${entry.originalName}: Önüne ayırıcı ekle`} checked={draft.separatorBefore ?? false} onChange={event => update({ ...draft, separatorBefore: event.target.checked }, true)} /> Önüne ayırıcı çizgi ekle</label>
      <label><input type="checkbox" aria-label={`${entry.originalName}: Arkasına ayırıcı ekle`} checked={draft.separatorAfter ?? false} onChange={event => update({ ...draft, separatorAfter: event.target.checked }, true)} /> Arkasına ayırıcı çizgi ekle</label>
      <label>Simge kaynağı<input className="white-control" aria-label={`${entry.originalName} simge kaynağı`} value={draft.icon} placeholder="shell32.dll,3" onChange={event => update({ ...draft, icon: event.target.value })} /></label><div className="context-icon-controls"><button className="secondary-button" onClick={() => { update(draft, true); command('pick-icon', { id: entry.id, ids: entry.ids }); }}>{entry.iconPreview ? 'Simgeyi değiştir' : 'Simge ekle'}</button><button className="secondary-button" disabled={!draft.icon} onClick={() => update({ ...draft, icon: '' }, true)}>Özgün simge</button><button className="secondary-button" onClick={() => { cancel(); command('reset-item', { ids: entry.ids }); }}>Varsayılana döndür</button></div></details>}
    {entry.isSeparator && <button className="secondary-button" onClick={() => { cancel(); command('reset-item', { ids: entry.ids }); }}>Varsayılana döndür</button>}
  </article>;
}
function GroupEditor({ name, initialIcon, items, command, close }: { name: string; initialIcon: string; items: Item[]; command: Command; close: () => void }) {
  const [title, setTitle] = useState(name);
  const [icon, setIcon] = useState(initialIcon);
  const [selected, setSelected] = useState<string[]>(items.filter(item => name && item.group === name).map(item => item.id));
  const [query, setQuery] = useState('');
  return <form className="context-group-editor" onSubmit={event => { event.preventDefault(); command('save-group', { name: title.trim(), previousName: name, icon, ids: items.filter(item => selected.includes(item.id)).flatMap(item => item.ids) }); close(); }}>
    <label>Grup adı<input autoFocus required className="white-control" aria-label="Grup adı" maxLength={80} value={title} onChange={event => setTitle(event.target.value)} /></label>
    <label>Grup simgesi<input className="white-control" aria-label="Grup simge kaynağı" value={icon} placeholder="shell32.dll,3" onChange={event => setIcon(event.target.value)} /></label>
    <label>Gruba eklenecek öğeler<input className="white-control" type="search" aria-label="Gruba eklenecek öğe ara" placeholder="Öğe ara…" value={query} onChange={event => setQuery(event.target.value)} /></label>
    <div className="context-group-choices">{items.filter(item => item.name.toLocaleLowerCase('tr').includes(query.toLocaleLowerCase('tr'))).map(item => <label key={item.id} data-group-entry-id={item.id}><input type="checkbox" checked={selected.includes(item.id)} onChange={event => { const checked = event.target.checked; setSelected(current => checked ? [...current, item.id] : current.filter(id => id !== item.id)); }} /><span>{item.name}{item.group && item.group !== name && <small> · {item.group} grubundan taşınır</small>}</span></label>)}</div>
    <div className="context-icon-controls"><button className="primary-button" disabled={!title.trim()} type="submit">{name ? 'Grubu güncelle' : 'Grubu oluştur'}</button><button className="secondary-button" type="button" onClick={close}>Vazgeç</button></div>
  </form>;
}
export function ContextMenuPanel({ state, command }: { state: ContextMenuState; command: Command }) {
  const [options, updateOptions] = useAutoSaveDraft({ cleanSeparators: state.cleanSeparators ?? true, showSeparators: state.showSeparators ?? true, unquotedCopyPath: state.unquotedCopyPath ?? true }, next => command('save-options', next));
  const [query, setQuery] = useState('');
  const [source, setSource] = useState('all');
  const [editingGroup, setEditingGroup] = useState<string | null>(null);
  const controls = useRef(new Map<string, VisibilityControl>());
  const batch = useRef<Record<string, unknown>[] | null>(null);
  const itemCommand: Command = (action, data) => {
    if (action === 'save-item' && batch.current && data) batch.current.push(data);
    else command(action, data);
  };
  const setAllVisibility = (visible: boolean) => {
    const updates: Record<string, unknown>[] = [];
    batch.current = updates;
    try { entries.forEach(entry => controls.current.get(entry.id)?.setVisibility(visible)); }
    finally { batch.current = null; }
    if (updates.length) command('save-items', { items: updates });
  };
  const items = combineEntries(state.entries);
  const entries = items.filter(entry => `${entry.name} ${entry.originalName} ${entry.sources.join(' ')}`.toLocaleLowerCase('tr').includes(query.toLocaleLowerCase('tr')) && (source === 'all' || source === 'separators' && entry.isSeparator || source === 'prepared' && entry.ids.some(id => id.startsWith('prepared:')) || source === 'live' && entry.sources.includes('Gerçek menü öğesi') || source === 'windows' && entry.ids.some(id => id.startsWith('builtin:')) || source === 'extensions' && entry.sources.includes('Kabuk uzantısı')));
  const visible = items.filter(entry => entry.visible && !entry.isSeparator);
  return <section className="context-menu-panel">
    <section className="context-options" aria-label="Ayırıcı ve yol kopyalama ayarları">{([
      ['cleanSeparators', 'Boşta kalan ayırıcı çizgileri otomatik temizle'],
      ['showSeparators', 'Menünün mevcut ayırıcı çizgilerini göster'],
      ['unquotedCopyPath', 'Yol olarak kopyala: Tırnak işaretlerini kaldır'],
    ] as const).map(([key, label]) => <label key={key}><span>{label}</span><button type="button" role="switch" aria-label={label} aria-checked={options[key]} className={`toggle ${options[key] ? 'on' : ''}`} onClick={() => updateOptions({ ...options, [key]: !options[key] }, true)}><span /></button></label>)}<p className="automation-hint">Mevcut çizgileri menüyü açıp listeyi yeniledikten sonra “Ayırıcı çizgiler” filtresinden tek tek yönetin. Yeni çizgi için öğenin “Diğer ayarlar” bölümünü kullanın.</p></section>
    <details className="context-help"><summary>Kullanım bilgisi</summary><p>Konum seçimleri otomatik kaydedilir. Boş alan, klasörün içindeki boş alandır; masaüstü ayrı ayarlanır. Windows'un o konumda sunmadığı komutlar eklenmez.</p><p>Dinamik öğeler için sağ tık menüsünü açıp “Listeyi yenile”ye basın. Ayarlar klasik menüde / “Daha fazla seçenek göster” bölümünde uygulanır.</p></details>
    <section className="context-groups" aria-label="Menü grupları"><header><strong>Gruplar</strong><button className="secondary-button" onClick={() => setEditingGroup('')}>Grup oluştur</button></header>
      {state.groups.length === 0 && <p className="automation-hint">Bir grup oluşturun, ardından içine eklenecek öğeleri seçin.</p>}
      {state.groups.map(group => <div className="context-group-row" key={group}><span className="context-group-title">{state.groupIcons?.[group]?.iconPreview && <img src={state.groupIcons[group].iconPreview} alt="" />}<strong>{group}</strong><small> · {items.filter(item => item.group === group).length} öğe</small></span><button className="secondary-button" onClick={() => setEditingGroup(group)}>Öğeleri düzenle</button><button className="secondary-button" onClick={() => command('pick-group-icon', { name: group })}>{state.groupIcons?.[group]?.icon ? 'Simgeyi değiştir' : 'Simge ekle'}</button>{state.groupIcons?.[group]?.icon && <button className="secondary-button" onClick={() => command('clear-group-icon', { name: group })}>Simgeyi kaldır</button>}<button className="secondary-button" onClick={() => command('delete-group', { name: group })}>Grubu kaldır</button></div>)}
      {editingGroup !== null && <GroupEditor key={editingGroup} name={editingGroup} initialIcon={state.groupIcons?.[editingGroup]?.icon || ''} items={items.filter(item => !item.isSeparator)} command={command} close={() => setEditingGroup(null)} />}
    </section>
    <div className="context-toolbar"><input className="white-control" type="search" aria-label="Menü öğesi ara" placeholder="Menü öğesi ara…" value={query} onChange={event => setQuery(event.target.value)} /><select className="white-control" aria-label="Menü öğesi türü" value={source} onChange={event => setSource(event.target.value)}><option value="separators">Ayırıcı çizgiler</option><option value="all">Tüm öğeler</option><option value="live">Gerçek menü öğeleri</option><option value="windows">Windows komutları</option><option value="prepared">Hazır eylemler</option><option value="extensions">Kabuk uzantıları</option></select><button className="secondary-button" onClick={() => command('refresh')}>Listeyi yenile</button></div>
    <details className="context-preview"><summary>Menü düzeni önizlemesi</summary><ul>{visible.filter(entry => !entry.group).map(entry => <li key={entry.id}>{entry.name}</li>)}{state.groups.map(group => <li key={group}>{state.groupIcons?.[group]?.iconPreview && <img src={state.groupIcons[group].iconPreview} alt="" />}<strong>{group} ▸</strong><ul>{visible.filter(entry => entry.group === group).map(entry => <li key={entry.id}>{entry.name}</li>)}</ul></li>)}</ul></details>
    <div className="context-toolbar" aria-label="Toplu görünürlük"><span>{query || source !== 'all' ? 'Filtrelenen öğeler' : 'Tüm öğeler'} · {entries.length}</span><button className="secondary-button" disabled={!entries.length} onClick={() => setAllVisibility(true)}>Tümünü aç</button><button className="secondary-button" disabled={!entries.length} onClick={() => setAllVisibility(false)}>Tümünü kapat</button></div>
    <p className="automation-hint">{entries.length} öğe · {items.filter(entry => !entry.visible).length} tüm konumlarda gizli</p>
    {entries.map(entry => <MenuEntry key={entry.ids.join('|')} entry={entry} command={itemCommand} ref={control => { if (control) controls.current.set(entry.id, control); else controls.current.delete(entry.id); }} />)}
    {entries.length === 0 && <p>Bu filtreyle öğe bulunamadı.</p>}
  </section>;
}
