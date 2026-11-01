import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { FeatureApp } from './features/FeatureApp';
import './styles.css';
import './island-design.css';

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
  const [activeOnly, setActiveOnly] = useState(false);
  const [selectedId, setSelectedId] = useState('');
  const [showAppearance, setShowAppearance] = useState(false);
  const [error, setError] = useState('');

  const receive = useCallback((event: MessageEvent) => {
    const message = event.data as { type: string; payload?: AppState; message?: string };
    if (message.type === 'state' && message.payload) {
      setState(message.payload);
      setSelectedId(current => current || message.payload!.modules[0]?.id || '');
    }
    if (message.type === 'error') setError(message.message ?? 'İşlem tamamlanamadı.');
  }, []);

  useEffect(() => {
    const bridge = window.chrome?.webview;
    if (!bridge) {
      if (import.meta.env.DEV) setState(previewState);
      return;
    }
    bridge?.addEventListener('message', receive);
    bridge?.postMessage({ command: 'ui.ready', payload: {} });
    const focusSearch = () => document.getElementById('module-search')?.focus();
    window.addEventListener('winforge-search', focusSearch);
    window.addEventListener('keydown', onGlobalSearch);
    function onGlobalSearch(event: KeyboardEvent) {
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
    if (activeOnly && !module.isActive) return false;
    const needle = query.trim().toLocaleLowerCase('tr');
    return !needle || `${module.name} ${module.description} ${module.category}`.toLocaleLowerCase('tr').includes(needle);
  }), [activeOnly, category, query, state]);
  const selected = filtered.find(module => module.id === selectedId) ?? filtered[0];
  const categories = ['Tümü', ...(state?.categories ?? [])];

  useEffect(() => {
    if (selected && selected.id !== selectedId) setSelectedId(selected.id);
  }, [selected, selectedId]);

  if (!state) return <div className="loading"><span className="loader" />WinForge hazırlanıyor</div>;

  const accentHex = state.appearance.accents.find(accent => accent.name === state.appearance.accent)?.hex ?? '#75A7FF';
  const island = state.modules.find(module => module.id === 'dynamic-island');
  return <div className={`app-shell tone-${state.appearance.tone.toLowerCase()} ${state.appearance.glow ? 'glow' : ''}`} style={{ '--accent': accentHex } as React.CSSProperties}>
    <aside className="sidebar">
      <div className="brand"><div className="brand-mark"><Icon name="layers" size={21} /></div><div><strong>WinForge</strong><span>WINDOWS ARAÇ KUTUSU</span></div></div>
      <div className="nav-label">KATEGORİLER</div>
      <nav className="category-nav" aria-label="Modül kategorileri">
        {categories.map((item, index) => <button key={item} className={`nav-item ${category === item ? 'selected' : ''}`} onClick={() => setCategory(item)}>
          <span className="nav-icon">{index === 0 ? <Icon name="grid" /> : <Icon name="layers" />}</span><span>{item}</span>
          {item === 'Tümü' && <span className="nav-count">{state.totalCount}</span>}
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
        <div className="page-intro"><div className="eyebrow">SENİN ALANIN. SENİN AKIŞIN.</div><h1>{category === 'Tümü' ? 'Her şey, bir arada.' : category}</h1><p>Windows deneyimini kendi ritmine göre düzenle.</p></div>
        <div className="top-actions">
          <label className="search-box"><Icon name="search" /><input id="module-search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Modül ara" /><kbd>Ctrl F</kbd></label>
          <button className="icon-button" title="Görünüm ayarları" aria-label="Görünüm ayarları" onClick={() => setShowAppearance(true)}><Icon name="settings" /></button>
          <button className="icon-button" title="WinForge'dan çık" aria-label="WinForge'dan çık" onClick={() => send('app.exit')}><Icon name="power" /></button>
        </div>
      </header>

      {island && <button className={`island-entry ${island.isActive ? 'is-live' : ''}`} onClick={() => { setCategory('Tümü'); setQuery(''); setActiveOnly(false); setSelectedId(island.id); }}>
        <span className="island-entry-preview" aria-hidden="true"><span className="island-entry-orbit" /><span className="island-entry-line" /><span className="island-entry-bars"><i /><i /><i /><i /></span></span>
        <span className="island-entry-copy"><strong>Dynamic Island</strong><small>{island.isActive ? 'Masaüstünde, akışınla birlikte.' : 'Küçük bir ada. Daha akıcı bir masaüstü.'}</small></span>
        <span className={`island-entry-status ${island.isActive ? 'enabled' : ''}`}><span />{island.isActive ? 'Etkin' : 'Keşfet'}<Icon name="arrow" size={14} /></span>
      </button>}

      <section className="content-toolbar">
        <div className="result-count"><strong>{filtered.length}</strong><span>modül</span><span className="toolbar-divider" />
          <button className={`filter-chip ${activeOnly ? 'on' : ''}`} onClick={() => setActiveOnly(value => !value)}><span className="filter-check">{activeOnly && <Icon name="check" size={12} />}</span>Etkin olanlar</button>
        </div>
        <div className="autosave"><span className="save-dot" />Değişiklikler otomatik kaydedilir</div>
      </section>

      <section className="workspace">
        <div className="module-column">
          <div className="section-heading"><div><h2>{category === 'Tümü' ? 'Tüm modüller' : `${category} modülleri`}</h2><p>Bir aracı seçerek ayarlarını görüntüle.</p></div><span className="count-badge">{filtered.length}</span></div>
          <div className="module-list">
            {filtered.map(module => <button key={module.id} className={`module-card ${selected?.id === module.id ? 'active' : ''}`} onClick={() => setSelectedId(module.id)}>
              <span className={`module-icon ${module.isActive ? 'enabled' : ''}`}><Icon name={module.category === 'Fare' ? 'grid' : module.category === 'Sistem' ? 'settings' : 'layers'} size={18} /></span>
              <span className="module-copy"><strong>{module.name}</strong><small>{module.description}</small><span className="module-category">{module.category}</span></span>
              <span className={`status-mark ${module.isActive ? 'enabled' : ''}`} title={module.isActive ? 'Etkin' : 'Kapalı'} />
            </button>)}
            {filtered.length === 0 && <div className="empty-state"><div className="empty-icon"><Icon name="search" size={22} /></div><strong>Sonuç bulunamadı</strong><p>Arama veya kategori filtresini değiştirmeyi dene.</p><button className="secondary-button" onClick={() => { setQuery(''); setCategory('Tümü'); setActiveOnly(false); }}>Filtreleri temizle</button></div>}
          </div>
        </div>
        <div className="details-column">
          {selected ? <ModuleDetails key={selected.id} module={selected} onToggle={enabled => send('module.toggle', { id: selected.id, enabled })} onUpdate={(key, value) => send('setting.update', { moduleId: selected.id, key, value })} onExecute={key => send('setting.execute', { moduleId: selected.id, key })} /> : <div className="details-empty">Görüntülenecek modül yok.</div>}
        </div>
      </section>
    </main>

    {showAppearance && <AppearanceDialog appearance={state.appearance} onClose={() => setShowAppearance(false)} onChange={value => send('appearance.update', value)} onOpenLogs={() => send('system.openLogs')} />}
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
    {sections.map(([section, settings]) => <section className="settings-section" key={section || 'general'}>
      {section && <h3>{section}</h3>}
      {settings.map(setting => <SettingControl key={setting.key} setting={setting} onUpdate={onUpdate} onExecute={onExecute} />)}
    </section>)}
  </div>;
}

function Toggle({ checked, label, onChange }: { checked: boolean; label: string; onChange: (value: boolean) => void }) {
  return <button role="switch" aria-checked={checked} aria-label={label} className={`toggle ${checked ? 'on' : ''}`} onClick={() => onChange(!checked)}><span /></button>;
}

function SettingControl({ setting, onUpdate, onExecute }: { setting: Setting; onUpdate: (key: string, value: unknown) => void; onExecute: (key: string) => void }) {
  const [text, setText] = useState(String(setting.value ?? ''));
  const [number, setNumber] = useState(Number(setting.value ?? setting.defaultValue ?? 0));
  const [capture, setCapture] = useState(false);
  const [captured, setCaptured] = useState('');
  const hotkeyInput = useRef<HTMLInputElement>(null);
  useEffect(() => { setText(String(setting.value ?? '')); }, [setting.value]);
  useEffect(() => { setNumber(Number(setting.value ?? setting.defaultValue ?? 0)); }, [setting.value, setting.defaultValue]);
  useEffect(() => {
    if (setting.kind !== 'number' || number === Number(setting.value ?? setting.defaultValue ?? 0)) return;
    const timer = window.setTimeout(() => onUpdate(setting.key, number), 180);
    return () => window.clearTimeout(timer);
  }, [number, onUpdate, setting.defaultValue, setting.key, setting.kind, setting.value]);
  const isHotkey = setting.kind === 'text' && /hotkey|shortcut|kısayol/i.test(setting.key + setting.label);
  const saveText = () => onUpdate(setting.key, text.trim());

  if (setting.kind === 'action') return <div className="setting-row action-row"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><button className="primary-button" onClick={() => onExecute(setting.key)}>{setting.label}<Icon name="arrow" size={15} /></button></div>;
  if (setting.kind === 'toggle') return <div className="setting-row"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><Toggle checked={Boolean(setting.value)} label={setting.label} onChange={value => onUpdate(setting.key, value)} /></div>;
  if (setting.kind === 'number') return <div className="setting-row vertical"><div className="setting-copy"><div className="setting-heading"><strong>{setting.label}</strong><span className="value-pill">{number}</span></div>{setting.description && <p>{setting.description}</p>}</div><input className="range-input" type="range" min={setting.min} max={setting.max} step="1" value={number} onChange={event => setNumber(Number(event.target.value))} aria-label={setting.label} /></div>;
  if (setting.kind === 'choice') return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><select className="white-control" value={String(setting.value ?? setting.defaultValue ?? '')} onChange={event => onUpdate(setting.key, event.target.value)} aria-label={setting.label}>{setting.options.map(option => <option value={option} key={option}>{setting.optionLabels[option] ?? option}</option>)}</select></div>;
  if (setting.kind === 'palette') return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="palette-options">{setting.options.map(option => <button className={`palette-option ${setting.value === option ? 'selected' : ''}`} key={option} title={setting.optionLabels[option] ?? option} style={{ backgroundColor: option }} onClick={() => onUpdate(setting.key, option)} aria-label={setting.optionLabels[option] ?? option} />)}</div></div>;
  if (setting.kind === 'text' && /^#[0-9a-f]{6}$/i.test(String(setting.defaultValue))) return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="input-action-row"><input type="color" value={/^#[0-9a-f]{6}$/i.test(text) ? text : String(setting.defaultValue)} onChange={event => { setText(event.target.value); onUpdate(setting.key, event.target.value); }} aria-label={`${setting.label} seçici`} /><input className="white-control" value={text} maxLength={7} onChange={event => setText(event.target.value)} onBlur={() => { if (/^#[0-9a-f]{6}$/i.test(text)) saveText(); }} onKeyDown={event => { if (event.key === 'Enter' && /^#[0-9a-f]{6}$/i.test(text)) saveText(); }} aria-label={setting.label} aria-invalid={!/^#[0-9a-f]{6}$/i.test(text)} /></div></div>;
  if (isHotkey) return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="input-action-row"><input ref={hotkeyInput} className="white-control" value={capture ? captured || 'Tuşlara bas…' : text} readOnly={capture} onKeyDown={event => {
    if (!capture) return;
    event.preventDefault();
    if (event.key === 'Escape') { setCapture(false); setCaptured(''); return; }
    if (event.key === 'Enter') { if (captured) { onUpdate(setting.key, captured); setText(captured); } setCapture(false); return; }
    if (['Control', 'Alt', 'Shift', 'Meta'].includes(event.key)) return;
    const parts = [event.ctrlKey && 'Ctrl', event.altKey && 'Alt', event.shiftKey && 'Shift', event.metaKey && 'Win'].filter(Boolean);
    const key = event.key.startsWith('Arrow') ? event.key.slice(5) : event.key === ' ' ? 'Space' : event.key;
    parts.push(key.length === 1 ? key.toUpperCase() : key);
    setCaptured(parts.join('+'));
  }} onWheel={event => {
    if (!capture) return;
    event.preventDefault();
    const parts = [event.ctrlKey && 'Ctrl', event.altKey && 'Alt', event.shiftKey && 'Shift', event.metaKey && 'Win'].filter(Boolean);
    parts.push(event.deltaY < 0 ? 'MouseWheelUp' : 'MouseWheelDown');
    setCaptured(parts.join('+'));
  }} aria-label={setting.label} /><button className="secondary-button compact" onClick={() => {
    if (capture) { setCapture(false); setCaptured(''); return; }
    setCapture(true);
    setCaptured('');
    requestAnimationFrame(() => hotkeyInput.current?.focus());
  }}>{capture ? 'İptal' : 'Ata'}</button>{capture && <button className="primary-button compact" onClick={() => { if (captured) { onUpdate(setting.key, captured); setText(captured); } setCapture(false); }}>Kaydet</button>}</div></div>;
  return <div className="setting-row vertical"><div className="setting-copy"><strong>{setting.label}</strong>{setting.description && <p>{setting.description}</p>}</div><div className="input-action-row"><input className="white-control" value={text} onChange={event => setText(event.target.value)} onBlur={saveText} onKeyDown={event => { if (event.key === 'Enter') { saveText(); (event.currentTarget as HTMLInputElement).blur(); } }} aria-label={setting.label} /></div></div>;
}

function AppearanceDialog({ appearance, onClose, onChange, onOpenLogs }: { appearance: Appearance; onClose: () => void; onChange: (value: Record<string, unknown>) => void; onOpenLogs: () => void }) {
  return <div className="modal-backdrop" onMouseDown={event => { if (event.target === event.currentTarget) onClose(); }}><section className="appearance-dialog" role="dialog" aria-modal="true" aria-labelledby="appearance-title">
    <header><div><span className="eyebrow">KİŞİSELLEŞTİRME</span><h2 id="appearance-title">Görünüm</h2></div><button className="icon-button" onClick={onClose} aria-label="Kapat"><Icon name="close" /></button></header>
    <div className="appearance-section"><h3>Yüzey tonu</h3><div className="tone-options">{appearance.tones.map(tone => <button key={tone} className={`tone-option ${appearance.tone === tone ? 'selected' : ''}`} onClick={() => onChange({ tone })}><span className={`tone-preview ${tone.toLowerCase()}`}><i /><i /><i /></span><span>{tone}</span>{appearance.tone === tone && <Icon name="check" size={15} />}</button>)}</div></div>
    <div className="appearance-section"><h3>Vurgu rengi</h3><div className="accent-options">{appearance.accents.map(accent => <button key={accent.name} className={`accent-option ${appearance.accent === accent.name ? 'selected' : ''}`} title={accent.name} aria-label={accent.name} style={{ '--swatch': accent.hex } as React.CSSProperties} onClick={() => onChange({ accent: accent.name })}>{appearance.accent === accent.name && <Icon name="check" size={14} />}</button>)}</div></div>
    <div className="setting-row"><div className="setting-copy"><strong>Arka plan parlaması</strong><p>Başlık alanında hafif vurgu ışığı göster.</p></div><Toggle checked={appearance.glow} label="Arka plan parlaması" onChange={glow => onChange({ glow })} /></div>
    <footer><button className="secondary-button" onClick={onOpenLogs}>Uygulama günlükleri</button><button className="primary-button" onClick={onClose}>Tamam</button></footer>
  </section></div>;
}

const view = new URLSearchParams(window.location.search).get('view');
createRoot(document.getElementById('root')!).render(
  <React.StrictMode>{view ? <FeatureApp view={view} /> : <App />}</React.StrictMode>,
);
