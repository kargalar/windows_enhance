import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { FeatureApp } from './features/FeatureApp';
import { AutomationPanel, type AutomationState } from './AutomationPanel';
import { ShortcutActionsPanel, type ShortcutActionsState } from './ShortcutActionsPanel';
import { ShortcutField } from './ShortcutField';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { ContextMenuPanel, type ContextMenuState } from './ContextMenuPanel';
import { DefaultPathsPanel, DefaultAppsPanel } from './features/DefaultSettingsPanels';
import { groupModules } from './moduleGroups';
import './styles.css';
import './island-design.css';
import './management.css';

type Setting = {
  key: string;
  label: string;
  description?: string;
  section?: string;
  kind: 'toggle' | 'number' | 'text' | 'choice' | 'palette' | 'action';
  defaultValue?: unknown;
  value?: unknown;
  min: number;
  max: number;
  options: string[];
  optionLabels: Record<string, string>;
};

type Module = {
  id: string;
  name: string;
  category: string;
  description: string;
  warning?: string;
  isActive: boolean;
  error?: string;
  settings: Setting[];
  panel?: AutomationState | ShortcutActionsState | ContextMenuState | { kind: 'start-sections'; buttons: { id: string; name: string; visible: boolean }[] } | { kind: 'default-paths' | 'default-apps'; state: Record<string, unknown> | null };
};

type Appearance = {
  tone: string;
  accent: string;
  glow: boolean;
  tones: string[];
  accents: { name: string; hex: string }[];
};

type AppState = {
  modules: Module[];
  categories: string[];
  activeCount: number;
  totalCount: number;
  version: string;
  appearance: Appearance;
  dataDirectory?: string;
};

const previewState: AppState = {
  modules: [
    { id: 'dynamic-island', name: 'Dynamic Island', category: 'Sistem', description: 'Medya, odak ve sistem olayları. Masaüstünün üzerinde yaşayan küçük bir ada.', isActive: false, settings: [
      { key: 'preview', label: 'Adayı göster', kind: 'action', min: 0, max: 0, options: [], optionLabels: {} },
      { key: 'hover', label: 'Üzerine gelince genişlet', kind: 'toggle', value: true, min: 0, max: 1, options: [], optionLabels: {} },
      { key: 'theme', label: 'Kapsül teması', kind: 'choice', value: 'obsidian', min: 0, max: 0, options: ['obsidian', 'graphite', 'evergreen'], optionLabels: { obsidian: 'Obsidyen', graphite: 'Grafit', evergreen: 'Orman' } },
    ] },
    { id: 'taskbar-tools', name: 'Görev çubuğu araçları', category: 'Görev çubuğu', description: 'Windows görev çubuğuna küçük, kullanışlı ayarlar ekler.', isActive: true, settings: [
      { key: 'seconds', label: 'Saatte saniyeleri göster', description: 'Görev çubuğu saatine saniye ekler.', kind: 'toggle', value: true, min: 0, max: 1, options: [], optionLabels: {} },
      { key: 'opacity', label: 'Saydamlık düzeyi', description: 'Görev çubuğunun arka plan opaklığını ayarlar.', kind: 'number', value: 72, min: 0, max: 100, options: [], optionLabels: {} },
      { key: 'action', label: 'Ayarları aç', kind: 'action', min: 0, max: 0, options: [], optionLabels: {} },
    ] },
    { id: 'media-flyout', name: 'Medya ve ses paneli', category: 'Sistem', description: 'Ses ve çalma denetimlerini küçük bir panelde toplar.', isActive: true, settings: [
      { key: 'hotkey-enabled', label: 'Kısayol etkin', kind: 'toggle', value: true, min: 0, max: 1, options: [], optionLabels: {} },
      { key: 'corner', label: 'Panel konumu', kind: 'choice', value: 'bottom-right', min: 0, max: 0, options: ['bottom-right', 'bottom-left'], optionLabels: { 'bottom-right': 'Sağ alt', 'bottom-left': 'Sol alt' } },
      { key: 'palette', label: 'Vurgu rengi', kind: 'palette', value: '#ffffff', min: 0, max: 0, options: ['#ffffff', '#73a8ff', '#53d989'], optionLabels: {} },
    ] },
    { id: 'display-brightness', name: 'Ekran parlaklığı', category: 'Sistem', description: 'Uyumlu ekranların parlaklığını ve günlük planını yönetir.', isActive: false, warning: 'Harici monitör denetimleri DDC/CI desteğine bağlıdır.', settings: [
      { key: 'brightness-step', label: 'Kısayol adımı (%)', section: 'Görünüm', description: 'Her tuş basışında uygulanacak parlaklık farkı.', kind: 'number', value: 8, min: 1, max: 25, options: [], optionLabels: {} },
      { key: 'brightness-hotkey', label: 'Parlaklık kısayolu', kind: 'text', value: 'Ctrl+Alt+F12', min: 0, max: 0, options: [], optionLabels: {} },
    ] },
  ],
  categories: ['Görev çubuğu', 'Sistem'], activeCount: 2, totalCount: 4, version: '1.0.0',
  appearance: { tone: 'Siyah', accent: 'Mavi', glow: false, tones: ['Siyah', 'Grafit'], accents: [{ name: 'Mavi', hex: '#75A7FF' }, { name: 'Yeşil', hex: '#4CD97B' }, { name: 'Kırmızı', hex: '#FF6B6B' }] },
};

function send(command: string, payload: Record<string, unknown> = {}) {
  window.chrome?.webview?.postMessage({ command, payload });
}

function Icon({ name, size = 18 }: { name: string; size?: number }) {
  const paths: Record<string, React.ReactNode> = {
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
    sliders: <><path d="M4 7h9m4 0h3M4 17h3m4 0h9" /><circle cx="15" cy="7" r="2" /><circle cx="9" cy="17" r="2" /></>,
    settings: <><circle cx="12" cy="12" r="3" /><path d="m19.4 15 .1.1 1.4 1.1-1.4 2.4-1.7-.7a8 8 0 0 1-1.7 1l-.3 1.8h-2.8l-.3-1.8a8 8 0 0 1-1.7-1l-1.7.7-1.4-2.4L7.3 15a8 8 0 0 1 0-2l-1.4-1.1 1.4-2.4 1.7.7a8 8 0 0 1 1.7-1l.3-1.8h2.8l.3 1.8a8 8 0 0 1 1.7 1l1.7-.7 1.4 2.4-1.4 1.1a8 8 0 0 1-.1 2Z" /></>,
    power: <><path d="M12 2v10" /><path d="M5.6 5.6a9 9 0 1 0 12.8 0" /></>,
    layers: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5M3 16l9 5 9-5" /></>,
    close: <><path d="m18 6-12 12M6 6l12 12" /></>,
    arrow: <><path d="M5 12h14m-7-7 7 7-7 7" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>,
  };
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name] ?? paths.layers}</svg>;
}

function App() {
  const [state, setState] = useState<AppState | null>(null);
  const [category, setCategory] = useState('Tümü');
  const [query, setQuery] = useState('');
  const [status, setStatus] = useState('all');
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [favorites, setFavorites] = useState<string[]>(() => {
    try {
      const saved: unknown = JSON.parse(localStorage.getItem('winforge.favorites') ?? '[]');
      return Array.isArray(saved) ? saved.filter((id): id is string => typeof id === 'string') : [];
    } catch { return []; }
  });
  const [selectedId, setSelectedId] = useState('');
  const [showAppearance, setShowAppearance] = useState(false);
  const [error, setError] = useState('');
  const clearFilters = () => { setQuery(''); setCategory('Tümü'); setStatus('all'); setFavoritesOnly(false); };
  useEffect(() => {
    try { localStorage.setItem('winforge.favorites', JSON.stringify(favorites)); } catch { /* Session favorites remain available. */ }
  }, [favorites]);

  const receive = useCallback((event: MessageEvent) => {
    const message = event.data as { type: string; payload?: AppState; message?: string };
    if (message.type === 'state' && message.payload) {
      setState(message.payload);
      setSelectedId(current => current || message.payload!.modules[0]?.id || '');
    }
    if (message.type === 'error') {
      setError(message.message ?? 'İşlem tamamlanamadı.');
      if (message.payload) setState(message.payload);
    }
  }, []);

  useEffect(() => {
    const bridge = window.chrome?.webview;
    if (!bridge) {
      if (import.meta.env.DEV) setState(previewState);
    }
    bridge?.addEventListener('message', receive);
    bridge?.postMessage({ command: 'ui.ready', payload: {} });
    const focusSearch = () => document.getElementById('module-search')?.focus();
    window.addEventListener('winforge-search', focusSearch);
    window.addEventListener('keydown', onGlobalSearch);
    function onGlobalSearch(event: KeyboardEvent) {
      if (event.defaultPrevented) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
        event.preventDefault();
        focusSearch();
      }
      if (event.key === 'Escape') setShowAppearance(false);
    }
    return () => {
      bridge?.removeEventListener('message', receive);
      window.removeEventListener('winforge-search', focusSearch);
      window.removeEventListener('keydown', onGlobalSearch);
    };
  }, [receive]);

  const filtered = useMemo(() => (state?.modules ?? []).filter(module => {
    if (category !== 'Tümü' && module.category !== category) return false;
    if (status === 'active' && !module.isActive) return false;
    if (status === 'inactive' && module.isActive) return false;
    if (status === 'error' && !module.error) return false;
    if (favoritesOnly && !favorites.includes(module.id)) return false;
    const needle = query.trim().toLocaleLowerCase('tr');
    return !needle || `${module.name} ${module.description} ${module.category} ${module.settings.map(setting => `${setting.label} ${setting.description ?? ''} ${setting.section ?? ''}`).join(' ')}`.toLocaleLowerCase('tr').includes(needle);
  }), [status, favoritesOnly, favorites, category, query, state]);
  const selected = filtered.find(module => module.id === selectedId) ?? filtered[0];
  const categories = ['Tümü', ...(state?.categories ?? [])];

  useEffect(() => {
    if (selected && selected.id !== selectedId) setSelectedId(selected.id);
  }, [selected, selectedId]);

  if (!state) return <div className="loading"><span className="loader" />WinForge hazırlanıyor</div>;

  const accentHex = state.appearance.accents.find(accent => accent.name === state.appearance.accent)?.hex ?? '#75A7FF';
  return <div className={`app-shell tone-${state.appearance.tone.toLowerCase()} ${state.appearance.glow ? 'glow' : ''}`} style={{ '--accent': accentHex } as React.CSSProperties}>
    <a className="skip-link" href="#module-details">Seçili modülün ayarlarına geç</a>
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><Icon name="layers" size={21} /></div><div><strong>WinForge</strong><span>WINDOWS ARAÇ KUTUSU</span></div></div>
      <div className="nav-label">KATEGORİLER</div>
      <nav className="category-nav" aria-label="Modül kategorileri">
        {categories.map((item, index) => <button key={item} aria-current={category === item ? 'page' : undefined} className={`nav-item ${category === item ? 'selected' : ''}`} onClick={() => setCategory(item)}>
          <span className="nav-icon">{index === 0 ? <Icon name="grid" /> : <Icon name="layers" />}</span><span>{item}</span>
          <span className="nav-count">{item === 'Tümü' ? state.totalCount : state.modules.filter(module => module.category === item).length}</span>
        </button>)}
      </nav>
      <div className="sidebar-bottom">
        <div className="engine-card"><span className="live-dot" /><div><strong>Motor çalışıyor</strong><small>{state.activeCount} araç etkin</small></div></div>
        <button className="nav-item appearance-link" onClick={() => setShowAppearance(true)}><span className="nav-icon"><Icon name="settings" /></span><span>Görünüm</span></button>
        <div className="sidebar-version">WinForge <span>v{state.version}</span></div>
      </div>
    </aside>

    <main className="main-area">
      <header className="topbar">
        <div className="page-intro"><div className="eyebrow">WINFORGE KONTROL PANELİ</div><h1>{category === 'Tümü' ? 'Araçlarını yönet' : category}</h1><p>Aracını bul, durumunu gör ve ayarlarını düzenle.</p></div>
        <div className="top-actions">
          <label className="search-box"><Icon name="search" /><input id="module-search" aria-label="Araç veya ayar ara" value={query} onChange={event => setQuery(event.target.value)} placeholder="Araç veya ayar ara…" /><kbd>Ctrl F</kbd></label>
          <button className="icon-button" title="Görünüm ayarları" aria-label="Görünüm ayarları" onClick={() => setShowAppearance(true)}><Icon name="settings" /></button>
          <button className="icon-button" title="WinForge'dan çık" aria-label="WinForge'dan çık" onClick={() => send('app.exit')}><Icon name="power" /></button>
        </div>
      </header>

      <div className="overview" aria-label="Araç özeti">
        <button aria-pressed={status === 'all' && !favoritesOnly && category === 'Tümü' && !query} onClick={clearFilters}><strong>{state.totalCount}</strong><span>Tüm araçlar</span></button>
        <button aria-pressed={status === 'active'} onClick={() => { clearFilters(); setStatus('active'); }}><strong>{state.activeCount}</strong><span>Etkin araçlar</span></button>
        <button aria-pressed={favoritesOnly} onClick={() => { clearFilters(); setFavoritesOnly(true); }}><strong>{state.modules.filter(module => favorites.includes(module.id)).length}</strong><span>Favorilerim</span></button>
      </div>

      <section className="content-toolbar">
        <div className="result-count"><span role="status"><strong>{filtered.length}</strong> araç</span><span className="toolbar-divider" />
          <label className="status-filter">Durum <select aria-label="Araç durumuna göre filtrele" value={status} onChange={event => setStatus(event.target.value)}><option value="all">Tümü</option><option value="active">Etkin</option><option value="inactive">Kapalı</option><option value="error">Hatalı</option></select></label>
          <button className={`filter-chip ${favoritesOnly ? 'on' : ''}`} aria-pressed={favoritesOnly} onClick={() => setFavoritesOnly(value => !value)}>★ Favoriler</button>
          {(query || category !== 'Tümü' || status !== 'all' || favoritesOnly) && <button className="filter-chip" onClick={clearFilters}>Temizle</button>}
        </div>
        <div className="autosave"><span className="save-dot" />Değişiklikler otomatik kaydedilir</div>
      </section>

      <section className="workspace">
        <div className="module-column">
          <div className="section-heading"><div><h2>{category === 'Tümü' ? 'Tüm modüller' : `${category} modülleri`}</h2><p>Bir aracı seçerek ayarlarını görüntüle.</p></div><span className="count-badge">{filtered.length}</span></div>
          <div className="module-list">
            {groupModules(filtered).map(group => <details className="module-family" key={group.name} open><summary>{group.name}<span>{group.modules.length} modül</span></summary><div className="module-family-children">{group.modules.map(module => <div className="module-entry" key={module.id}><button aria-pressed={selected?.id === module.id} aria-controls="module-details" className={`module-card ${selected?.id === module.id ? 'active' : ''}`} onClick={() => setSelectedId(module.id)}>
              <span className={`module-icon ${module.isActive ? 'enabled' : ''}`}><Icon name={module.category === 'Fare' ? 'grid' : module.category === 'Sistem' ? 'settings' : 'layers'} size={18} /></span>
              <span className="module-copy"><strong>{module.name}</strong><small>{module.description}</small><span className="module-category">{module.category}</span></span>
              <span className={`module-status ${module.error ? 'has-error' : module.isActive ? 'enabled' : ''}`}>{module.error ? 'Hata' : module.isActive ? 'Etkin' : 'Kapalı'}</span>
            </button><button className={`favorite-button ${favorites.includes(module.id) ? 'is-favorite' : ''}`} aria-label={`${module.name}: ${favorites.includes(module.id) ? 'favorilerden çıkar' : 'favorilere ekle'}`} aria-pressed={favorites.includes(module.id)} onClick={() => setFavorites(current => current.includes(module.id) ? current.filter(id => id !== module.id) : [...current, module.id])}>★</button></div>)}</div></details>)}
            {filtered.length === 0 && <div className="empty-state"><div className="empty-icon"><Icon name="search" size={22} /></div><strong>{favoritesOnly ? 'Bu filtrelerde favori araç yok' : 'Sonuç bulunamadı'}</strong><p>{favoritesOnly ? 'Araçların yanındaki yıldızla favorilerini belirle veya filtreleri temizle.' : 'Arama veya kategori filtresini değiştirmeyi dene.'}</p><button className="secondary-button" onClick={clearFilters}>Filtreleri temizle</button></div>}
          </div>
        </div>
        <div className="details-column" id="module-details" tabIndex={-1} aria-label="Seçili modülün ayarları">
          {selected ? <ModuleDetails key={selected.id} module={selected} onToggle={enabled => send('module.toggle', { id: selected.id, enabled })} onUpdate={(key, value) => send('setting.update', { moduleId: selected.id, key, value })} onExecute={key => send('setting.execute', { moduleId: selected.id, key })} /> : <div className="details-empty">Görüntülenecek modül yok.</div>}
        </div>
      </section>
    </main>

    {showAppearance && <AppearanceDialog dataDirectory={state.dataDirectory} onOpenData={() => send('system.openData')} appearance={state.appearance} onClose={() => setShowAppearance(false)} onChange={value => send('appearance.update', value)} onOpenLogs={() => send('system.openLogs')} />}
    {error && <div className="toast-error" role="alert"><span>{error}</span><button onClick={() => setError('')} aria-label="Bildirimi kapat"><Icon name="close" size={15} /></button></div>}
  </div>;
}

function ModuleDetails({ module, onToggle, onUpdate, onExecute }: { module: Module; onToggle: (value: boolean) => void; onUpdate: (key: string, value: unknown) => void; onExecute: (key: string) => void }) {
  const sections = useMemo(() => {
    const groups = new Map<string, Setting[]>();
    module.settings.forEach(setting => {
      const key = setting.section ?? '';
      groups.set(key, [...(groups.get(key) ?? []), setting]);
    });
    return [...groups.entries()];
  }, [module.settings]);

  return <div className="details-scroll">
    <div className="details-header"><div className="details-icon"><Icon name={module.category === 'Sistem' ? 'settings' : 'layers'} size={21} /></div><div className="details-title"><span className="details-category">{module.category}</span><h2>{module.name}</h2></div><Toggle checked={module.isActive} label={`${module.name} modülünü etkinleştir`} onChange={onToggle} /></div>
    <p className="details-description">{module.description}</p>
    <div className={`module-state ${module.isActive ? 'enabled' : ''}`}><span className="state-dot" />{module.isActive ? 'Bu modül etkin' : 'Bu modül kapalı'}</div>
    {module.warning && <div className="warning-box"><strong>Bilgi</strong><p>{module.warning}</p></div>}
    {module.error && <div className="error-box"><strong>Bir hata oluştu</strong><p>{module.error}</p></div>}
    {!module.isActive && <p className="inactive-hint">Ayarları düzenleyebilirsin. Kullanmak için üstteki anahtardan modülü etkinleştir.</p>}
    {sections.length > 1 && <nav className="settings-jumps" aria-label="Ayar bölümleri">{sections.map(([section], index) => <a key={section} href={`#settings-${module.id}-${index}`}>{section || 'Genel'}</a>)}</nav>}
    {module.panel && 'kind' in module.panel && module.panel.kind === 'default-paths' && <DefaultPathsPanel inline state={module.panel.state} sendCommand={(action, data = {}) => send('module.command', { moduleId: module.id, action, data })} />}
    {module.panel && 'kind' in module.panel && module.panel.kind === 'default-apps' && <DefaultAppsPanel inline state={module.panel.state} sendCommand={(action, data = {}) => send('module.command', { moduleId: module.id, action, data })} />}
    {module.panel && 'kind' in module.panel && module.panel.kind === 'start-sections' && <section className="settings-section"><h3>Alt düğmeler</h3><p>Başlat menüsünü açtığınızda mevcut klasör ve ayar düğmeleri burada listelenir. Her birini ayrı ayrı gösterebilir veya gizleyebilirsiniz.</p>{module.panel.buttons.map(button => <div className="setting-row" key={button.id}><strong>{button.name}</strong><Toggle checked={button.visible} label={`${button.name} göster`} onChange={visible => send('module.command', { moduleId: module.id, action: 'place-visibility', data: { id: button.id, visible } })} /></div>)}</section>}
    {module.id === 'automation' && module.panel && <AutomationPanel state={module.panel as AutomationState} active={module.isActive} command={(action, data = {}) => send('module.command', { moduleId: module.id, action, data })} />}
    {module.panel && 'kind' in module.panel && module.panel.kind === 'context-menu' && <ContextMenuPanel state={module.panel} command={(action, data = {}) => send('module.command', { moduleId: module.id, action, data })} />}
    {module.panel && 'kind' in module.panel && module.panel.kind === 'shortcut-actions' && <ShortcutActionsPanel state={module.panel} moduleId={module.id} active={module.isActive} command={(action, data = {}) => send('module.command', { moduleId: module.id, action, data })} />}
    {sections.map(([section, settings], index) => <section className="settings-section" id={`settings-${module.id}-${index}`} key={section || 'general'}>
      <h3>{section || 'Genel ayarlar'}</h3>
      {settings.map(setting => <SettingControl key={setting.key} setting={setting} onUpdate={onUpdate} onExecute={onExecute} />)}
    </section>)}
  </div>;
}

function Toggle({ checked, label, onChange }: { checked: boolean; label: string; onChange: (value: boolean) => void }) {
  return <button role="switch" aria-checked={checked} aria-label={label} className={`toggle ${checked ? 'on' : ''}`} onClick={() => onChange(!checked)}><span /></button>;
}

function SettingControl({ setting, onUpdate, onExecute }: { setting: Setting; onUpdate: (key: string, value: unknown) => void; onExecute: (key: string) => void }) {
  const [text, setText] = useAutoSaveDraft(String(setting.value ?? ''), value => {
    if (setting.kind === 'text' && (!/^#[0-9a-f]{6}$/i.test(String(setting.defaultValue)) || /^#[0-9a-f]{6}$/i.test(value))) onUpdate(setting.key, value.trim());
  });
  const [number, setNumber] = useState(Number(setting.value ?? setting.defaultValue ?? 0));
  useEffect(() => { setNumber(Number(setting.value ?? setting.defaultValue ?? 0)); }, [setting.value, setting.defaultValue]);
  useEffect(() => {
    if (setting.kind !== 'number' || number === Number(setting.value ?? setting.defaultValue ?? 0)) return;
    const timer = window.setTimeout(() => onUpdate(setting.key, number), 180);
    return () => window.clearTimeout(timer);
  }, [number, onUpdate, setting.defaultValue, setting.key, setting.kind, setting.value]);
  const isHotkey = setting.kind === 'text' && /hotkey|shortcut|kısayol/i.test(setting.key + setting.label);
  const saveText = () => setText(text.trim(), true);

  if (setting.kind === 'action') return <div className="setting-row action-row"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><button className="primary-button" onClick={() => onExecute(setting.key)}>{setting.label}<Icon name="arrow" size={15} /></button></div>;
  if (setting.kind === 'toggle') return <div className="setting-row"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><Toggle checked={Boolean(setting.value)} label={setting.label} onChange={value => onUpdate(setting.key, value)} /></div>;
  if (setting.kind === 'number') return <div className="setting-row vertical"><div className="setting-copy"><div className="setting-heading"><strong>{setting.label}</strong><span className="value-pill">{number}</span></div>{setting.description && <p>{setting.description}</p>}</div><input className="range-input" type="range" min={setting.min} max={setting.max} step="1" value={number} onChange={event => setNumber(Number(event.target.value))} aria-label={setting.label} /></div>;
  if (setting.kind === 'choice') return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><select className="white-control" value={String(setting.value ?? setting.defaultValue ?? '')} onChange={event => onUpdate(setting.key, event.target.value)} aria-label={setting.label}>{setting.options.map(option => <option value={option} key={option}>{setting.optionLabels[option] ?? option}</option>)}</select></div>;
  if (setting.kind === 'palette') return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="palette-options">{setting.options.map(option => <button className={`palette-option ${setting.value === option ? 'selected' : ''}`} key={option} title={setting.optionLabels[option] ?? option} style={{ backgroundColor: option }} onClick={() => onUpdate(setting.key, option)} aria-label={setting.optionLabels[option] ?? option} />)}</div></div>;
  if (setting.kind === 'text' && /^#[0-9a-f]{6}$/i.test(String(setting.defaultValue))) return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="input-action-row"><input type="color" value={/^#[0-9a-f]{6}$/i.test(text) ? text : String(setting.defaultValue)} onChange={event => { setText(event.target.value); onUpdate(setting.key, event.target.value); }} aria-label={`${setting.label} seçici`} /><input className="white-control" value={text} maxLength={7} onChange={event => setText(event.target.value)} onBlur={() => { if (/^#[0-9a-f]{6}$/i.test(text)) saveText(); }} onKeyDown={event => { if (event.key === 'Enter' && /^#[0-9a-f]{6}$/i.test(text)) saveText(); }} aria-label={setting.label} aria-invalid={!/^#[0-9a-f]{6}$/i.test(text)} /></div></div>;
  if (isHotkey) return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div>
    <ShortcutField value={text} label={setting.label} onChange={setText} allowMouse={setting.label === 'Bölge yakalama kısayolu'} allowWheel={/brightness.*hotkey/.test(setting.key)}
      onCommit={value => { if (value !== String(setting.value ?? '')) setText(value, true); }} /></div>;
  return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="input-action-row"><input className="white-control" value={text} onChange={event => setText(event.target.value)} onBlur={saveText} onKeyDown={event => { if (event.key === 'Enter') { saveText(); (event.currentTarget as HTMLInputElement).blur(); } }} aria-label={setting.label} /></div></div>;
}

function AppearanceDialog({ appearance, onClose, onChange, onOpenLogs, dataDirectory, onOpenData }: { dataDirectory?: string; onOpenData: () => void; appearance: Appearance; onClose: () => void; onChange: (value: Record<string, unknown>) => void; onOpenLogs: () => void }) {
  const dialogRef = useRef<HTMLElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.querySelector<HTMLButtonElement>('button')?.focus();
    return () => previous?.focus();
  }, []);
  return <div className="modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose(); }}><section ref={dialogRef} className="appearance-dialog" role="dialog" aria-modal="true" aria-labelledby="appearance-title" onKeyDown={event => {
    if (event.key === 'Escape') { event.stopPropagation(); onClose(); }
    if (event.key !== 'Tab') return;
    const controls = dialogRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input, select, a[href], [tabindex="0"]');
    if (!controls?.length) return;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
    if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
  }}>
    <header><div><span className="eyebrow">KİŞİSELLEŞTİRME</span><h2 id="appearance-title">Görünüm</h2></div><button className="icon-button" onClick={onClose} aria-label="Kapat"><Icon name="close" /></button></header>
    <div className="appearance-section"><h3>Yüzey tonu</h3><div className="tone-options">{appearance.tones.map(tone => <button key={tone} className={`tone-option ${appearance.tone === tone ? 'selected' : ''}`} onClick={() => onChange({ tone })}><span className={`tone-preview ${tone.toLowerCase()}`}><i /><i /><i /></span><span>{tone}</span>{appearance.tone === tone && <Icon name="check" size={15} />}</button>)}</div></div>
    <div className="appearance-section"><h3>Vurgu rengi</h3><div className="accent-options">{appearance.accents.map(accent => <button key={accent.name} className={`accent-option ${appearance.accent === accent.name ? 'selected' : ''}`} title={accent.name} aria-label={accent.name} style={{ '--swatch': accent.hex } as React.CSSProperties} onClick={() => onChange({ accent: accent.name })}>{appearance.accent === accent.name && <Icon name="check" size={14} />}</button>)}</div></div>
    <div className="setting-row"><div className="setting-copy"><strong>Arka plan parlaması</strong><p>Başlık alanında hafif vurgu ışığı göster.</p></div><Toggle checked={appearance.glow} label="Arka plan parlaması" onChange={glow => onChange({ glow })} /></div>
    {dataDirectory && <div className="appearance-section"><h3>Kayıt konumu</h3><p style={{ overflowWrap: 'anywhere' }}>{dataDirectory}</p><button className="secondary-button" onClick={onOpenData}>Veri klasörünü aç</button></div>}
    <footer><button className="secondary-button" onClick={onOpenLogs}>Uygulama günlükleri</button><button className="primary-button" onClick={onClose}>Tamam</button></footer>
  </section></div>;
}

const view = new URLSearchParams(window.location.search).get('view');
createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{view ? <FeatureApp view={view} /> : <App />}</React.StrictMode>,
);
