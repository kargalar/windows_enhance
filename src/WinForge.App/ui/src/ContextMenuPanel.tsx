import { useState } from 'react';
import { useAutoSaveDraft } from './useAutoSaveDraft';

type Rule = { id: string; name: string; visible: boolean; group: string; icon: string };
type Entry = Rule & { originalName: string; source: string; iconPreview?: string; hasOriginalIcon: boolean };
export type ContextMenuState = { kind: 'context-menu'; groups: string[]; entries: Entry[] };
type Command = (action: string, data?: Record<string, unknown>) => void;

function MenuEntry({ entry, command }: { entry: Entry; command: Command }) {
  const rule = { id: entry.id, name: entry.name, visible: entry.visible, group: entry.group, icon: entry.icon };
  const [draft, update, cancel] = useAutoSaveDraft<Rule>(rule, next => command('save', { id: next.id, rule: next }));
  return <article data-entry-id={entry.id} className={`context-entry ${draft.visible ? '' : 'is-off'}`}>
    <header><span className="context-entry-icon">{entry.iconPreview ? <img src={entry.iconPreview} alt="" /> : <span aria-label="Simge yok">◇</span>}</span>
      <div className="context-entry-title"><input className="white-control" aria-label={`${entry.originalName} görünen adı`} value={draft.name} maxLength={120} onChange={event => update({ ...draft, name: event.target.value })} /><small>{entry.source}{entry.name !== entry.originalName ? ` · Özgün ad: ${entry.originalName}` : ''}</small></div>
      <label className="context-visible"><input type="checkbox" aria-label={`${entry.originalName} menüde göster`} checked={draft.visible} onChange={event => update({ ...draft, visible: event.target.checked }, true)} /> Göster</label>
    </header>
    <div className="context-entry-options"><label>Alt menü / grup<input className="white-control" list="context-menu-groups" aria-label={`${entry.originalName} grubu`} value={draft.group} maxLength={80} placeholder="Ana menü · örn. Diğer yazın" onChange={event => update({ ...draft, group: event.target.value })} /></label>
      <div className="context-icon-controls"><button className="secondary-button" onClick={() => { update(draft, true); command('pick-icon', { id: entry.id }); }}>{entry.iconPreview ? 'Simgeyi değiştir' : 'Simge ekle'}</button><button className="secondary-button" disabled={!draft.icon} onClick={() => update({ ...draft, icon: '' }, true)}>Özgün simge</button></div>
    </div>
    <details className="context-entry-details"><summary>Simge kaynağı ve sıfırlama</summary><label>Simge dosyası veya DLL/EXE kaynağı<input className="white-control" aria-label={`${entry.originalName} simge kaynağı`} value={draft.icon} placeholder="C:\Simgeler\ornek.ico veya shell32.dll,3" onChange={event => update({ ...draft, icon: event.target.value })} /></label><button className="secondary-button" onClick={() => { cancel(); command('reset', { id: entry.id }); }}>Bu öğeyi varsayılana döndür</button></details>
  </article>;
}

export function ContextMenuPanel({ state, command }: { state: ContextMenuState; command: Command }) {
  const [query, setQuery] = useState('');
  const [source, setSource] = useState('all');
  const entries = state.entries.filter(entry => `${entry.name} ${entry.originalName} ${entry.source}`.toLocaleLowerCase('tr').includes(query.toLocaleLowerCase('tr')) &&
    (source === 'all' || source === 'live' && entry.source === 'Gerçek menü öğesi' || source === 'windows' && entry.id.startsWith('builtin:') || source === 'extensions' && entry.source === 'Kabuk uzantısı'));
  const visible = state.entries.filter(entry => entry.visible);
  return <section className="context-menu-panel">
    <p className="automation-hint">Öğeyi simgesiyle tanıyın; adını, görünürlüğünü ve grubunu düzenleyin. Grup alanına <strong>Diğer</strong> yazarsanız öğe sağ tık menüsünde bu alt menüye taşınır. Boş bırakırsanız ana menüde kalır. Değişiklikler otomatik uygulanır.</p>
    <p className="automation-hint">Dinamik öğeler için ilgili dosya veya klasörde sağ tık menüsünü bir kez açıp “Listeyi yenile”ye basın. Simge yoksa kendiniz seçebilirsiniz. Gruplama klasik menüde / “Daha fazla seçenek göster” bölümünde çalışır.</p>
    <div className="context-toolbar"><input className="white-control" type="search" aria-label="Menü öğesi ara" placeholder="Menü öğesi ara…" value={query} onChange={event => setQuery(event.target.value)} /><select className="white-control" aria-label="Menü öğesi türü" value={source} onChange={event => setSource(event.target.value)}><option value="all">Tüm öğeler</option><option value="live">Gerçek menü öğeleri</option><option value="windows">Windows komutları</option><option value="extensions">Kabuk uzantıları</option></select><button className="secondary-button" onClick={() => command('refresh')}>Listeyi yenile</button></div>
    <datalist id="context-menu-groups">{state.groups.map(group => <option key={group} value={group} />)}<option value="Diğer" /></datalist>
    <details className="context-preview"><summary>Menü düzeni önizlemesi</summary><ul>{visible.filter(entry => !entry.group).map(entry => <li key={entry.id}>{entry.iconPreview && <img src={entry.iconPreview} alt="" />}{entry.name}</li>)}{state.groups.map(group => <li key={group}><strong>{group} ▸</strong><ul>{visible.filter(entry => entry.group === group).map(entry => <li key={entry.id}>{entry.iconPreview && <img src={entry.iconPreview} alt="" />}{entry.name}</li>)}</ul></li>)}</ul></details>
    <p className="automation-hint">{entries.length} öğe · {state.entries.filter(entry => !entry.visible).length} gizli</p>
    {entries.map(entry => <MenuEntry key={entry.id} entry={entry} command={command} />)}
    {entries.length === 0 && <p>Bu filtreyle öğe bulunamadı.</p>}
  </section>;
}
