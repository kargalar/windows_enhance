import { useEffect, useImperativeHandle, useRef, useState, type Ref } from 'react';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { t, getLanguage } from './localization';
import { copy } from './moduleDesigns';

type Rule = { id: string; name: string; visible: boolean; group: string; icon: string; contexts?: Record<string, boolean>; separatorBefore?: boolean; separatorAfter?: boolean; extensions?: string };
type Entry = Rule & { originalName: string; source: string; identity?: string; scope?: string; configured?: boolean; iconPreview?: string; hasOriginalIcon: boolean; isSeparator?: boolean };
type Item = Entry & { ids: string[]; sources: string[]; contexts: Record<string, boolean> };
export type ContextMenuState = { kind: 'context-menu'; groups: string[]; groupIcons?: Record<string, { icon: string; iconPreview?: string }>; entries: Entry[]; cleanSeparators?: boolean; showSeparators?: boolean; unquotedCopyPath?: boolean };
type Command = (action: string, data?: Record<string, unknown>) => void;
type VisibilityControl = { setVisibility: (visible: boolean) => void };

const locationsTr = [['file', 'Dosyada'], ['folder', 'Klasörde'], ['background', 'Klasörün boş alanında'], ['desktop', 'Masaüstünün boş alanında'], ['drive', 'Sürücüde'], ['shortcut', 'Kısayolda'], ['application', 'Uygulamada veya betikte'], ['virtual', 'Sanal öğede']] as const;
const locationsEn = [['file', 'On file'], ['folder', 'On folder'], ['background', 'Directory background'], ['desktop', 'Desktop background'], ['drive', 'On drive'], ['shortcut', 'On shortcut'], ['application', 'Application / script'], ['virtual', 'Virtual item']] as const;

const locationNamesTr: Record<string, string> = { file: 'Dosya', folder: 'Klasör', background: 'Boş alan', desktop: 'Masaüstü', drive: 'Sürücü', shortcut: 'Kısayol', application: 'Uygulama / betik', virtual: 'Sanal öğe' };
const locationNamesEn: Record<string, string> = { file: 'File', folder: 'Folder', background: 'Directory background', desktop: 'Desktop', drive: 'Drive', shortcut: 'Shortcut', application: 'Application / script', virtual: 'Virtual item' };

export function combineEntries(entries: Entry[]): Item[] {
  const groups = new Map<string, Entry[]>();
  for (const entry of entries) {
    const identity = entry.identity || entry.originalName.replace(/&/g, '').trim().toLocaleLowerCase();
    groups.set(identity, [...(groups.get(identity) || []), entry]);
  }
  return [...groups.values()].map(rows => {
    const primary = rows.find(row => row.contexts) || rows.find(row => row.configured && !row.scope) || rows.find(row => row.configured) || rows.find(row => !row.scope) || rows[0];
    const icon = rows.find(row => row.icon && row.iconPreview) || rows.find(row => row.iconPreview) || primary;
    const fileVisible = primary.contexts?.file ?? rows.find(row => row.scope === 'file')?.visible ?? primary.visible;
    const contexts = Object.fromEntries(locationsTr.map(([scope]) => [scope, primary.contexts?.[scope] ?? rows.find(row => row.scope === scope)?.visible ?? ((scope === 'shortcut' || scope === 'application') ? fileVisible : primary.visible)]));
    return { ...primary, iconPreview: icon.iconPreview, hasOriginalIcon: rows.some(row => row.hasOriginalIcon), visible: Object.values(contexts).some(Boolean), contexts, ids: rows.map(row => row.id), sources: rows.map(row => row.source) };
  });
}

function MenuEntry({ entry, command, ref }: { entry: Item; command: Command; ref?: Ref<VisibilityControl> }) {
  const isTr = getLanguage() === 'tr';
  const locations = isTr ? locationsTr : locationsEn;
  const locationNames = isTr ? locationNamesTr : locationNamesEn;

  const rule: Omit<Rule, 'group'> = { id: entry.id, name: entry.isSeparator ? '' : entry.name, visible: entry.visible, icon: entry.isSeparator ? '' : entry.icon, contexts: entry.contexts, separatorBefore: entry.separatorBefore ?? false, separatorAfter: entry.separatorAfter ?? false, extensions: entry.extensions ?? '' };
  const [draft, update, cancel] = useAutoSaveDraft(rule, next => command('save-item', { ids: entry.ids, rule: next }));
  const setVisibility = (visible: boolean) => update(previous => ({ ...previous, visible, contexts: Object.fromEntries(locations.map(([scope]) => [scope, visible])) }), true);
  useImperativeHandle(ref, () => ({ setVisibility }));
  return <article data-entry-id={entry.id} className={`context-entry ${draft.visible ? '' : 'is-off'}`}>
    <header><span className="context-entry-icon">{entry.iconPreview ? <img src={entry.iconPreview} alt="" /> : <span aria-label="No icon">◇</span>}</span>
      <div className="context-entry-title">{entry.isSeparator ? <strong>{entry.originalName}</strong> : <input className="white-control" aria-label={`${entry.originalName} ${t('context.displayName')}`} value={draft.name} maxLength={120} onChange={event => update({ ...draft, name: event.target.value })} />}</div>
      {entry.group && <small className="context-group-badge">{entry.group}</small>}
    </header>
    <div className="context-locations" aria-label={`${entry.originalName} locations`}>{locations.map(([scope, label]) => <label key={scope} title={label}><span>{locationNames[scope]}</span><input type="checkbox" aria-label={`${entry.originalName}: ${label}`} checked={draft.contexts?.[scope] ?? draft.visible} onChange={event => {
      const contexts = { ...draft.contexts, [scope]: event.target.checked };
      update({ ...draft, contexts, visible: Object.values(contexts).some(Boolean) }, true);
    }} /></label>)}</div>
    <div className="context-icon-controls" aria-label={`${entry.originalName} ${t('context.allLocations')}`}><button className="secondary-button" onClick={() => setVisibility(true)}>{t('context.openAllLocations')}</button><button className="secondary-button" onClick={() => setVisibility(false)}>{t('context.closeAllLocations')}</button></div>
    {!entry.isSeparator && <details className="context-entry-details"><summary>{t('context.otherSettings')}</summary>
      <label>{t('context.extensions')}<input className="white-control" aria-label={`${entry.originalName} extensions`} value={draft.extensions ?? ''} maxLength={512} placeholder=".zip, .7z" onChange={event => update({ ...draft, extensions: event.target.value })} /></label>
      <label><input type="checkbox" aria-label={`${entry.originalName}: ${t('context.addSeparatorBeforeAria')}`} checked={draft.separatorBefore ?? false} onChange={event => update({ ...draft, separatorBefore: event.target.checked }, true)} /> {t('context.addSeparatorBefore')}</label>
      <label><input type="checkbox" aria-label={`${entry.originalName}: ${t('context.addSeparatorAfterAria')}`} checked={draft.separatorAfter ?? false} onChange={event => update({ ...draft, separatorAfter: event.target.checked }, true)} /> {t('context.addSeparatorAfter')}</label>
      <label>{t('context.iconSource')}<input className="white-control" aria-label={`${entry.originalName} icon`} value={draft.icon} placeholder="shell32.dll,3" onChange={event => update({ ...draft, icon: event.target.value })} /></label><div className="context-icon-controls"><button className="secondary-button" onClick={() => { update(draft, true); command('pick-icon', { id: entry.id, ids: entry.ids }); }}>{entry.iconPreview ? t('context.changeIcon') : t('context.addIcon')}</button><button className="secondary-button" disabled={!draft.icon} onClick={() => update({ ...draft, icon: '' }, true)}>{t('context.originalIcon')}</button><button className="secondary-button" onClick={() => { cancel(); command('reset-item', { ids: entry.ids }); }}>{t('context.resetDefault')}</button></div></details>}
    {entry.isSeparator && <button className="secondary-button" onClick={() => { cancel(); command('reset-item', { ids: entry.ids }); }}>{t('context.resetDefault')}</button>}
  </article>;
}

function GroupEditor({ name, initialIcon, items, command, close }: { name: string; initialIcon: string; items: Item[]; command: Command; close: () => void }) {
  const [title, setTitle] = useState(name);
  const [icon, setIcon] = useState(initialIcon);
  const [selected, setSelected] = useState<string[]>(items.filter(item => name && item.group === name).map(item => item.id));
  const [query, setQuery] = useState('');
  return <form className="context-group-editor" onSubmit={event => { event.preventDefault(); command('save-group', { name: title.trim(), previousName: name, icon, ids: items.filter(item => selected.includes(item.id)).flatMap(item => item.ids) }); close(); }}>
    <label>{t('shortcuts.name')}<input autoFocus required className="white-control" aria-label={t('context.groupName')} maxLength={80} value={title} onChange={event => setTitle(event.target.value)} /></label>
    <label>{t('context.iconSource')}<input className="white-control" aria-label="Group icon" value={icon} placeholder="shell32.dll,3" onChange={event => setIcon(event.target.value)} /></label>
    <label>{t('context.groups')}<input className="white-control" type="search" aria-label="Search items" placeholder={t('context.searchPlaceholder')} value={query} onChange={event => setQuery(event.target.value)} /></label>
    <div className="context-group-choices">{items.filter(item => item.name.toLocaleLowerCase().includes(query.toLocaleLowerCase())).map(item => <label key={item.id} data-group-entry-id={item.id}><input type="checkbox" checked={selected.includes(item.id)} onChange={event => { const checked = event.target.checked; setSelected(current => checked ? [...current, item.id] : current.filter(id => id !== item.id)); }} /><span>{item.name}{item.group && item.group !== name && <small> · {item.group}</small>}</span></label>)}</div>
    <div className="context-icon-controls"><button className="primary-button" disabled={!title.trim()} type="submit">{name ? t('context.editItems') : t('context.createGroup')}</button><button className="secondary-button" type="button" onClick={close}>{t('shortcut.cancel')}</button></div>
  </form>;
}

export function ContextMenuPanel({ state, command, filterQuery='' }: { state: ContextMenuState; command: Command;filterQuery?:string }) {
  const [view,setView]=useState('items');
  const [options, updateOptions] = useAutoSaveDraft({ cleanSeparators: state.cleanSeparators ?? true, showSeparators: state.showSeparators ?? true, unquotedCopyPath: state.unquotedCopyPath ?? true }, next => command('save-options', next));
  const [query, setQuery] = useState('');
  useEffect(()=>{setQuery(filterQuery);if(filterQuery)setView('items');},[filterQuery]);
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
  const search = query.trim().toLocaleLowerCase();
  const entries = search ? items.filter(entry => `${entry.name} ${entry.originalName} ${entry.sources.join(' ')}`.toLocaleLowerCase().includes(search) && (source === 'all' || source === 'separators' && entry.isSeparator || source === 'prepared' && entry.ids.some(id => id.startsWith('prepared:')) || source === 'live' && entry.sources.includes('Gerçek menü öğesi') || source === 'windows' && entry.ids.some(id => id.startsWith('builtin:')) || source === 'extensions' && entry.sources.includes('Kabuk uzantısı'))) : [];
  const visible = items.filter(entry => entry.visible && !entry.isSeparator);
  return <section className="context-menu-panel">
    <nav className="workspace-tabs" aria-label={copy(['Menü çalışma alanı','Menu workspace'])}>{[['items',copy(['Öğeler','Items'])],['groups',t('context.groups')],['options',copy(['Menü davranışı','Menu behavior'])],['preview',t('context.preview')]].map(([key,name])=><button type="button" key={key} aria-current={view===key?'true':undefined} onClick={()=>setView(key)}>{name}</button>)}</nav>
    <section hidden={view!=='options'} className="context-options" aria-label="Separators">{([
      ['cleanSeparators', t('context.cleanSeparators')],
      ['showSeparators', t('context.showSeparators')],
      ['unquotedCopyPath', t('context.unquotedCopyPath')],
    ] as const).map(([key, label]) => <label key={key}><span>{label}</span><button type="button" role="switch" aria-label={label} aria-checked={options[key]} className={`toggle ${options[key] ? 'on' : ''}`} onClick={() => updateOptions({ ...options, [key]: !options[key] }, true)}><span /></button></label>)}<p className="automation-hint">{t('context.optionsHint')}</p></section>
    <details className="context-help"><summary>{t('context.helpTitle')}</summary><p>{t('context.help1')}</p><p>{t('context.help2')}</p></details>
    <section hidden={view!=='groups'} className="context-groups" aria-label="Groups"><header><strong>{t('context.groups')}</strong><button className="secondary-button" onClick={() => setEditingGroup('')}>{t('context.createGroup')}</button></header>
      {state.groups.length === 0 && <p className="automation-hint">{t('context.noGroups')}</p>}
      {state.groups.map(group => <div className="context-group-row" key={group}><span className="context-group-title">{state.groupIcons?.[group]?.iconPreview && <img src={state.groupIcons[group].iconPreview} alt="" />}<strong>{group}</strong><small> · {items.filter(item => item.group === group).length}</small></span><button className="secondary-button" onClick={() => setEditingGroup(group)}>{t('context.editItems')}</button><button className="secondary-button" onClick={() => command('pick-group-icon', { name: group })}>{state.groupIcons?.[group]?.icon ? t('context.changeIcon') : t('context.addIcon')}</button>{state.groupIcons?.[group]?.icon && <button className="secondary-button" onClick={() => command('clear-group-icon', { name: group })}>{t('context.removeIcon')}</button>}<button className="secondary-button" onClick={() => command('delete-group', { name: group })}>{t('context.removeGroup')}</button></div>)}
      {editingGroup !== null && <GroupEditor key={editingGroup} name={editingGroup} initialIcon={state.groupIcons?.[editingGroup]?.icon || ''} items={items.filter(item => !item.isSeparator)} command={command} close={() => setEditingGroup(null)} />}
    </section>
    <div hidden={view!=='items'} className="context-toolbar"><input className="white-control" type="search" aria-label={t('context.searchAria')} placeholder={t('context.searchPlaceholder')} value={query} onChange={event => setQuery(event.target.value)} /><select className="white-control" aria-label={t('context.filterAria')} value={source} onChange={event => setSource(event.target.value)}><option value="separators">{t('context.filterSeparators')}</option><option value="all">{t('context.filterAll')}</option><option value="live">{t('context.filterLive')}</option><option value="windows">{t('context.filterWindows')}</option><option value="prepared">{t('context.filterPrepared')}</option><option value="extensions">{t('context.filterExtensions')}</option></select><button className="secondary-button" onClick={() => command('refresh')}>{t('context.refresh')}</button></div>
    <details hidden={view!=='preview'} open={view==='preview'} className="context-preview"><summary>{t('context.preview')}</summary><ul>{visible.filter(entry => !entry.group).map(entry => <li key={entry.id}>{entry.name}</li>)}{state.groups.map(group => <li key={group}>{state.groupIcons?.[group]?.iconPreview && <img src={state.groupIcons[group].iconPreview} alt="" />}<strong>{group} ▸</strong><ul>{visible.filter(entry => entry.group === group).map(entry => <li key={entry.id}>{entry.name}</li>)}</ul></li>)}</ul></details>
    <div hidden={view!=='items'} className="context-items-workspace">
    <div className="context-toolbar" aria-label={t('context.batchVisibility')}><span>{query || source !== 'all' ? t('context.filteredItems') : t('context.allItems')} · {entries.length}</span><button className="secondary-button" disabled={!entries.length} onClick={() => setAllVisibility(true)}>{t('context.openAll')}</button><button className="secondary-button" disabled={!entries.length} onClick={() => setAllVisibility(false)}>{t('context.closeAll')}</button></div>
    {entries.map(entry => <MenuEntry key={entry.ids.join('|')} entry={entry} command={itemCommand} ref={control => { if (control) controls.current.set(entry.id, control); else controls.current.delete(entry.id); }} />)}
    {!search ? <div className="workbench-empty"><strong>{copy(['Düzenlenecek öğeyi bul','Find the item to edit'])}</strong><p>{t('context.searchPrompt')}</p></div> : entries.length === 0 && <p>{t('context.noMatching')}</p>}
    </div>
  </section>;
}
