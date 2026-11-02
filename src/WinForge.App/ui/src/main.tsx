import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { FeatureApp } from './features/FeatureApp';
import { AutomationPanel, type AutomationState } from './AutomationPanel';
import { ShortcutActionsPanel, type ShortcutActionsState } from './ShortcutActionsPanel';
import { ShortcutField } from './ShortcutField';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { ContextMenuPanel, type ContextMenuState } from './ContextMenuPanel';
import { DefaultPathsPanel, DefaultAppsPanel } from './features/DefaultSettingsPanels';
import './styles.css';
import './island-design.css';
import './management.css';
import './dashboard.css';

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
  categories: ['Görev çubuğu', 'Sistem'], activeCount: 2, totalCount: 3, version: '1.0.0',
  appearance: { tone: 'Açık', accent: 'Mavi', glow: false, tones: ['Açık', 'Siyah', 'Grafit'], accents: [{ name: 'Mavi', hex: '#75A7FF' }, { name: 'Yeşil', hex: '#4CD97B' }, { name: 'Kırmızı', hex: '#FF6B6B' }] },
};

function send(command: string, payload: Record<string, unknown> = {}) {
  window.chrome?.webview?.postMessage({ command, payload });
}

function Icon({ name, size = 18 }: { name: string; size?: number }) {
  const paths: Record<string, React.ReactNode> = {
    home: <><path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z" /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z" />,
    monitor: <><rect x="3" y="4" width="18" height="13" rx="1.5" /><path d="M12 17v4m-4 0h8" /></>,
    window: <><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M3 8h18" /></>,
    folder: <path d="M3 7V5a2 2 0 0 1 2-2h4l3 4h7a2 2 0 0 1 2 2v10H3V7Z" />,
    bolt: <path d="m13 2-10 12h8l-1 8 11-13h-8Z" />,
    back: <path d="m10 5-7 7 7 7M3 12h18" />,
    taskbar: <><rect x="2" y="6" width="20" height="13" rx="1" /><path d="M3 16h18m-16 0h.1m3 0h.1" /></>,
    windows: <><rect x="3" y="9" width="13" height="13" rx="1" /><path d="M9 9V3h13v13h-6" /></>,
    text: <><path d="M3 7V3h4m10 0h4v4m0 10v4h-4M7 21H3v-4M8 8h8m-4 0v9" /></>,
    pipette: <><path d="m14 5 5 5M3 21l4-1L20 7a3 3 0 0 0-4-4L3 16v5Z" /><path d="m12 7 5 5" /></>,
    files: <><path d="M11 14H4V2h8l4 4v3M11 2v5h5" /><rect x="10" y="11" width="12" height="11" rx="1" /><path d="M13 15h6m-6 3h6" /></>,
    volume: <><path d="m11 3-6 5H2v8h3l6 5V3Zm5 4a7 7 0 0 1 0 10m3-13a11 11 0 0 1 0 16" /></>,
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
    sliders: <><path d="M4 7h9m4 0h3M4 17h3m4 0h9" /><circle cx="15" cy="7" r="2" /><circle cx="9" cy="17" r="2" /></>,
    settings: <><path d="m9.5 3-.6 2.1-2 .9-2.1-.6-2.3 4 1.5 1.5v2.2L2.5 15l2.3 4 2.1-.6 2 .9.6 2.1h5l.6-2.1 2-.9 2.1.6 2.3-4-1.5-1.6v-2.2l1.5-1.5-2.3-4-2.1.6-2-.9-.6-2.1Z" /><circle cx="12" cy="12" r="3" /></>,
    power: <><path d="M12 2v10" /><path d="M5.6 5.6a9 9 0 1 0 12.8 0" /></>,
    layers: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5M3 16l9 5 9-5" /></>,
    close: <><path d="m18 6-12 12M6 6l12 12" /></>,
    arrow: <><path d="M5 12h14m-7-7 7 7-7 7" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>,
  };
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name] ?? paths.layers}</svg>;
}

function moduleGroup(module: Module) {
  if (['Taskbar', 'Görev çubuğu', 'Başlat', 'Görünüm'].includes(module.category)) return 'Görünüm';
  if (['Fare', 'Arama', 'Pencereler'].includes(module.category)) return 'Verimlilik';
  return module.category;
}

function moduleIcon(module: Module) {
  const text = `${module.id} ${module.name}`.toLocaleLowerCase('tr');
  if (/taskbar|görev çubuğu/.test(text)) return 'taskbar';
  if (/window|pencere|tiling/.test(text)) return 'windows';
  if (/text|metin|ocr/.test(text)) return 'text';
  if (/color|renk/.test(text)) return 'pipette';
  if (/rename|adlandır/.test(text)) return 'files';
  if (/media|medya|ses/.test(text)) return 'volume';
  if (module.category === 'Gezgin') return 'folder';
  return moduleGroup(module) === 'Sistem' ? 'settings' : 'grid';
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
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [navigation, setNavigation] = useState('Genel bakış');
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
      if (event.key === 'Escape') { setShowAppearance(false); setDetailsOpen(false); }
    }
    return () => {
      bridge?.removeEventListener('message', receive);
      window.removeEventListener('winforge-search', focusSearch);
      window.removeEventListener('keydown', onGlobalSearch);
    };
  }, [receive]);

  const filtered = useMemo(() => (state?.modules ?? []).filter(module => {
    if (category === 'Pencereler') {
      if (module.category !== 'Pencereler' && moduleIcon(module) !== 'windows') return false;
    } else if (category !== 'Tümü' && moduleGroup(module) !== category && module.category !== category) return false;
    if (status === 'active' && !module.isActive) return false;
    if (status === 'inactive' && module.isActive) return false;
    if (status === 'error' && !module.error) return false;
    if (favoritesOnly && !favorites.includes(module.id)) return false;
    const needle = query.trim().toLocaleLowerCase('tr');
    return !needle || `${module.name} ${module.description} ${module.category} ${module.settings.map(setting => `${setting.label} ${setting.description ?? ''} ${setting.section ?? ''}`).join(' ')}`.toLocaleLowerCase('tr').includes(needle);
  }), [status, favoritesOnly, favorites, category, query, state]);
  const selected = filtered.find(module => module.id === selectedId) ?? filtered[0];
  const navigate = (item: string) => { clearFilters(); setNavigation(item); setDetailsOpen(false); if (item === 'Favoriler') setFavoritesOnly(true); else if (!['Genel bakış', 'Tüm modüller'].includes(item)) setCategory(item); };

  useEffect(() => {
    if (selected && selected.id !== selectedId) setSelectedId(selected.id);
  }, [selected, selectedId]);

  if (!state) return <div className="loading"><span className="loader" />WinForge hazırlanıyor</div>;

  const dark = state.appearance.tone !== 'Açık';
  const accent = ({ Mavi: dark ? '#8bb6ff' : '#1556c0', 'Yeşil': dark ? '#70dda2' : '#14743f', 'Kırmızı': dark ? '#ff9696' : '#b52e3e' } as Record<string, string>)[state.appearance.accent] ?? (dark ? '#8bb6ff' : '#1556c0');
  const updateAppearance = (value: Record<string, unknown>) => {
    if (!window.chrome?.webview) setState(current => current ? { ...current, appearance: { ...current.appearance, ...value } } : current);
    send('appearance.update', value);
  };

  return <div className={`app-shell dashboard ${dark ? 'dark-dashboard' : 'light-dashboard'} ${state.appearance.tone === 'Grafit' ? 'tone-grafit' : ''} ${state.appearance.glow ? 'glow' : ''}`} style={{ '--accent': accent } as React.CSSProperties}>
    <a className="skip-link" href="#dashboard-content">İçeriğe geç</a>
    <main className={`dashboard-main ${detailsOpen && selected ? 'details-open' : ''}`} id="dashboard-content" tabIndex={-1}>
      <header className="dashboard-topbar">
        <div className="dashboard-brand"><img className="app-logo" src="./favicon.svg" alt="" aria-hidden="true" /><div><strong>WinForge</strong><span>Daha iyi bir Windows.</span></div></div>
        <label className="dashboard-search"><Icon name="search" size={21} /><input id="module-search" aria-label="Araç veya ayar ara" value={query} onChange={event => { setQuery(event.target.value); setDetailsOpen(false); }} placeholder="Araç veya ayar ara..." /><kbd>Ctrl + F</kbd></label>
        <div className="dashboard-actions">
          <button className="secondary-button" onClick={() => updateAppearance({ tone: dark ? 'Açık' : 'Siyah' })} aria-label={dark ? 'Açık temaya geç' : 'Karanlık temaya geç'}><Icon name="monitor" />{dark ? 'Açık tema' : 'Karanlık tema'}</button>
          <button className="icon-button" onClick={() => setShowAppearance(true)} aria-label="Uygulama ayarları" title="Uygulama ayarları"><Icon name="settings" /></button>
        </div>
      </header>
      <nav className="dashboard-navigation" aria-label="Modül gezintisi">
        {['Genel bakış', 'Favoriler', 'Tüm modüller', 'Görünüm', 'Pencereler', 'Gezgin', 'Verimlilik', 'Sistem'].map((item, index) => <button key={item} className={`dashboard-nav-item ${navigation === item ? 'selected' : ''}`} aria-current={navigation === item ? 'page' : undefined} onClick={() => navigate(item)}><Icon name={['home', 'star', 'grid', 'monitor', 'window', 'folder', 'bolt', 'settings'][index]} size={18} /><span>{item}</span></button>)}
      </nav>
      <div className={`dashboard-workspace ${detailsOpen && selected ? 'with-details' : ''}`}>
      <section className="dashboard-catalog" aria-label="Modül kataloğu">
        <header className="dashboard-heading"><div><h1>{navigation === 'Genel bakış' || navigation === 'Tüm modüller' ? 'Araçların, tek bir yerde' : navigation}</h1><p>Windows deneyimini kendine göre düzenle.</p></div><span className="dashboard-active-count"><i />{state.activeCount} modül etkin</span></header>
        <section className="dashboard-grid" aria-label="Modüller">
          {filtered.map(module => <article className={`tool-card ${detailsOpen && selected?.id === module.id ? 'selected' : ''} ${module.error ? 'has-error' : ''}`} key={module.id}>
            <div className="tool-card-intro"><span className="tool-icon"><Icon name={moduleIcon(module)} size={40} /></span><div><h2>{module.name}</h2><p>{module.description}</p></div></div>
            <div className="tool-card-status"><span className={module.error ? 'error' : module.isActive ? 'enabled' : ''}>{module.error ? 'Hata' : module.isActive ? 'Etkin' : 'Kapalı'}</span><Toggle checked={module.isActive} label={`${module.name} modülünü etkinleştir`} onChange={enabled => send('module.toggle', { id: module.id, enabled })} /></div>
            <div className="tool-card-footer"><button className="tool-settings-button" aria-label={`${module.name}: Ayarları aç`} onClick={() => { setSelectedId(module.id); setDetailsOpen(true); }}><Icon name="settings" size={22} />Ayarları aç</button><button className={`tool-favorite ${favorites.includes(module.id) ? 'selected' : ''}`} aria-label={`${module.name}: ${favorites.includes(module.id) ? 'favorilerden çıkar' : 'favorilere ekle'}`} aria-pressed={favorites.includes(module.id)} onClick={() => setFavorites(current => current.includes(module.id) ? current.filter(id => id !== module.id) : [...current, module.id])}><Icon name="star" size={24} /></button></div>
          </article>)}
          {filtered.length === 0 && <div className="dashboard-empty"><Icon name="search" size={32} /><h2>{favoritesOnly ? 'Henüz favori araç yok' : 'Sonuç bulunamadı'}</h2><p>{favoritesOnly ? 'Kartlardaki yıldızla favori araçlarını buraya ekle.' : 'Başka bir arama veya kategori dene.'}</p><button className="tool-settings-button" onClick={clearFilters}>Filtreleri temizle</button></div>}
        </section>
      </section>
      {detailsOpen && selected && <aside className="dashboard-settings" aria-label="Seçili modülün ayarları">
        <button className="dashboard-back" onClick={() => setDetailsOpen(false)}><Icon name="back" />Modüllere dön</button>
        <div id="module-details"><ModuleDetails key={selected.id} module={selected} onToggle={enabled => send('module.toggle', { id: selected.id, enabled })} onUpdate={(key, value) => send('setting.update', { moduleId: selected.id, key, value })} onExecute={key => send('setting.execute', { moduleId: selected.id, key })} /></div>
      </aside>}
      </div>
    </main>

    {showAppearance && <AppearanceDialog dataDirectory={state.dataDirectory} onOpenData={() => send('system.openData')} appearance={state.appearance} onClose={() => setShowAppearance(false)} onChange={updateAppearance} onOpenLogs={() => send('system.openLogs')} />}
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
  if (setting.kind === 'text' && setting.options.length > 0) return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><select className="white-control" value={setting.options.includes(text) ? text : '__custom'} onChange={event => { if (event.target.value !== '__custom') setText(event.target.value, true); }} aria-label={`${setting.label} hazır seçenekler`}><option value="__custom">Özel (elle düzenle)</option>{setting.options.map(option => <option value={option} key={option}>{setting.optionLabels[option] ?? option}</option>)}</select><input className="white-control" value={text} onChange={event => setText(event.target.value)} onBlur={saveText} onKeyDown={event => { if (event.key === 'Enter') { saveText(); event.currentTarget.blur(); } }} aria-label={setting.label} placeholder="Windows varsayılanı" /></div>;
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
    <div className="appearance-section"><h3>Tema</h3><div className="tone-options">{appearance.tones.map(tone => <button key={tone} className={`tone-option ${appearance.tone === tone ? 'selected' : ''}`} onClick={() => onChange({ tone })}><span className={`tone-preview ${tone.toLowerCase()}`}><i /><i /><i /></span><span>{tone === 'Siyah' ? 'Karanlık' : tone}</span>{appearance.tone === tone && <Icon name="check" size={15} />}</button>)}</div></div>
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
