import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { AppState } from './types';
import { DashboardStateBridge } from './dashboardStateBridge';
import { isShortcutModule } from './moduleGroups';
import { moduleGroup, moduleIcon } from './modulePresentation';
import { send } from './bridge';
import { Icon } from './Icon';
import { Toggle } from './Toggle';
import { ModuleDetails, SaveStatus } from './ModuleDetails';
import { AppearanceDialog } from './AppearanceDialog';
import { previewState } from './previewState';
import { t, setLanguage } from './localization';

export function App() {
  const [stateBridge] = useState(() => new DashboardStateBridge<AppState>());
  const panelRequests = useRef(new Set<string>());
  const [state, setState] = useState<AppState | null>(null);
  const [category, setCategory] = useState('all');
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
  const [navigation, setNavigation] = useState('overview');
  const [error, setError] = useState('');
  const [connectionDelayed, setConnectionDelayed] = useState(false);
  const detailsOpener = useRef<HTMLButtonElement | null>(null);
  const closeDetails = () => { setDetailsOpen(false); window.requestAnimationFrame(() => detailsOpener.current?.focus()); };
  useEffect(() => { if (!state) { const timer = setTimeout(() => setConnectionDelayed(true), 10000); return () => clearTimeout(timer); } }, [state]);
  useEffect(() => { if (detailsOpen) document.querySelector<HTMLElement>('.dashboard-back')?.focus(); }, [detailsOpen]);
  const clearFilters = () => { setQuery(''); setCategory('all'); setStatus('all'); setFavoritesOnly(false); };

  useEffect(() => {
    try { localStorage.setItem('winforge.favorites', JSON.stringify(favorites)); } catch { /* Session favorites remain available. */ }
  }, [favorites]);

  const receive = useCallback((event: MessageEvent) => {
    const result = stateBridge.accept(event.data);
    if (result.resync) send('ui.resync');
    if (result.accepted && result.state) {
      if (event.data.type === 'state' || event.data.type === 'error') panelRequests.current.clear();
      if (result.state.appearance?.language) {
        setLanguage(result.state.appearance.language);
      }
      setState(result.state);
      setSelectedId(current => current || result.state!.modules[0]?.id || '');
      if (result.requestId && !result.error) setError('');
    }
    if (result.error) setError(result.error);
  }, [stateBridge]);

  useEffect(() => {
    const bridge = window.chrome?.webview;
    if (!bridge) {
      if (import.meta.env.DEV) {
        const language = new URLSearchParams(window.location.search).get('lang') === 'tr' ? 'tr' : 'en';
        setLanguage(language);
        setState({ ...previewState, appearance: { ...previewState.appearance, language } });
      }
    }
    bridge?.addEventListener('message', receive);
    if (bridge) send('ui.ready', { deferredPanels: true });
    const focusSearch = () => (document.querySelector<HTMLInputElement>('#module-details input[type="search"]') ?? document.getElementById('module-search'))?.focus();
    window.addEventListener('winforge-search', focusSearch);
    window.addEventListener('keydown', onGlobalSearch);
    function onGlobalSearch(event: KeyboardEvent) {
      if (event.defaultPrevented) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'f') {
        event.preventDefault();
        focusSearch();
      }
      if (event.key === 'Escape') {
        if ((event.target as HTMLElement)?.closest('input, textarea, select, [role=dialog]')) return;
        setShowAppearance(false); closeDetails();
      }
    }
    return () => {
      bridge?.removeEventListener('message', receive);
      window.removeEventListener('winforge-search', focusSearch);
      window.removeEventListener('keydown', onGlobalSearch);
    };
  }, [receive]);

  const filtered = useMemo(() => (state?.modules ?? []).filter(module => {
    if (category === 'shortcuts') {
      if (!isShortcutModule(module)) return false;
    } else if (category !== 'all' && category !== 'overview' && isShortcutModule(module)) {
      return false;
    } else if (category === 'windows') {
      const matchCat = ['Pencereler', 'Windows'].includes(module.category) || moduleIcon(module) === 'windows';
      if (!matchCat) return false;
    } else if (category === 'appearance') {
      if (!['Görünüm', 'Appearance', 'Görev Çubuğu', 'Taskbar', 'Başlat', 'Start Menu'].includes(module.category) && moduleGroup(module) !== 'Appearance') return false;
    } else if (category === 'explorer') {
      if (!['Gezgin', 'Explorer'].includes(module.category)) return false;
    } else if (category === 'productivity') {
      if (!['Verimlilik', 'Productivity', 'Fare', 'Mouse', 'Arama', 'Search'].includes(module.category) && moduleGroup(module) !== 'Productivity') return false;
    } else if (category === 'system') {
      if (!['Sistem', 'System'].includes(module.category)) return false;
    } else if (category !== 'all' && category !== 'overview') {
      if (moduleGroup(module).toLowerCase() !== category.toLowerCase() && module.category.toLowerCase() !== category.toLowerCase()) return false;
    }
    if (status === 'active' && !module.isActive) return false;
    if (status === 'inactive' && module.isActive) return false;
    if (status === 'error' && !module.error) return false;
    if (favoritesOnly && !favorites.includes(module.id)) return false;
    const needle = query.trim().toLowerCase();
    return !needle || `${module.name} ${module.description} ${module.category} ${module.settings.map(setting => `${setting.label} ${setting.description ?? ''} ${setting.section ?? ''}`).join(' ')}`.toLowerCase().includes(needle);
  }), [status, favoritesOnly, favorites, category, query, state?.modules]);

  const selected = detailsOpen ? state?.modules.find(module => module.id === selectedId) : filtered.find(module => module.id === selectedId) ?? filtered[0];
  const navigate = (key: string) => {
    clearFilters();
    setNavigation(key);
    setDetailsOpen(false);
    if (key === 'favorites') setFavoritesOnly(true);
    else if (!['overview', 'all'].includes(key)) setCategory(key);
  };

  useEffect(() => {
    if (selected && selected.id !== selectedId) setSelectedId(selected.id);
  }, [selected, selectedId]);

  useEffect(() => {
    if (!detailsOpen || !selected?.panelAvailable || selected.panelLoaded || selected.panelError || panelRequests.current.has(selected.id)) return;
    panelRequests.current.add(selected.id);
    send('module.inspect', { moduleId: selected.id });
  }, [detailsOpen, selected]);

  if (!state) return <div className="loading" role="status"><span className="loader" />{t('loading.app')}{connectionDelayed && <div><p>{t('loading.connection')}</p><button className="secondary-button" onClick={() => send('ui.resync')}>{t('loading.retry')}</button></div>}</div>;

  const currentLang = state.appearance.language ?? 'en';
  setLanguage(currentLang);

  const dark = state.appearance.tone !== 'Açık';
  const accent = ({ Mavi: dark ? '#8bb6ff' : '#1556c0', 'Yeşil': dark ? '#70dda2' : '#14743f', 'Kırmızı': dark ? '#ff9696' : '#b52e3e' } as Record<string, string>)[state.appearance.accent] ?? (dark ? '#8bb6ff' : '#1556c0');
  const updateAppearance = (value: Record<string, unknown>) => {
    if (value.language && typeof value.language === 'string') {
      setLanguage(value.language);
    }
    if (!window.chrome?.webview) setState(current => current ? { ...current, appearance: { ...current.appearance, ...value } } : current);
    send('appearance.update', value);
  };

  const navItems = [
    { key: 'overview', icon: 'home', label: t('nav.overview') },
    { key: 'favorites', icon: 'star', label: t('nav.favorites') },
    { key: 'all', icon: 'grid', label: t('nav.allModules') },
    { key: 'appearance', icon: 'monitor', label: t('nav.appearance') },
    { key: 'windows', icon: 'window', label: t('nav.windows') },
    { key: 'explorer', icon: 'folder', label: t('nav.explorer') },
    { key: 'productivity', icon: 'bolt', label: t('nav.productivity') },
    { key: 'shortcuts', icon: 'keyboard', label: t('nav.shortcuts') },
    { key: 'system', icon: 'settings', label: t('nav.system') },
  ];

  const pageTitle = navItems.find(item => item.key === navigation)?.label ?? t('nav.allModules');

  return <div className={`app-shell dashboard ${dark ? 'dark-dashboard' : 'light-dashboard'} ${state.appearance.tone === 'Grafit' ? 'tone-grafit' : ''} ${state.appearance.glow ? 'glow' : ''}`} style={{ '--accent': accent } as React.CSSProperties}>
    <a className="skip-link" href="#dashboard-content">{t('skip.link')}</a>
    <aside className="dashboard-sidebar">
      <div className="dashboard-brand"><img className="app-logo" src="./favicon.svg" alt="" aria-hidden="true" /><strong>WinForge</strong></div>
      <nav className="dashboard-navigation" aria-label={t('nav.label')}>
        {navItems.map(item => <button key={item.key} className={`dashboard-nav-item ${navigation === item.key ? 'selected' : ''}`} aria-label={item.label} title={item.label} aria-current={navigation === item.key ? 'page' : undefined} onClick={() => navigate(item.key)}><Icon name={item.icon} size={20} /><span>{item.label}</span></button>)}
      </nav>
      <div className="dashboard-sidebar-footer"><button className="dashboard-nav-item" aria-label={t('app.settings')} title={t('app.settings')} onClick={() => setShowAppearance(true)}><Icon name="settings" size={20} /><span>{t('app.settings')}</span></button><small>WinForge <span>{state.version}</span></small></div>
    </aside>
    <main className={`dashboard-main ${detailsOpen && selected ? 'details-open' : ''}`} id="dashboard-content" tabIndex={-1}>
      <header className="dashboard-topbar">
        {detailsOpen && selected ? <button className="dashboard-back" onClick={closeDetails}><Icon name="back" /><span>{t('details.back')}</span></button> : <div className="topbar-title">{pageTitle}</div>}
        <label className="dashboard-search"><Icon name="search" size={21} /><input id="module-search" aria-label={t('search.aria')} value={query} onChange={event => { setQuery(event.target.value); setDetailsOpen(false); }} placeholder={t('search.placeholder')} /><kbd>{t('search.kbd')}</kbd></label>
        <div className="dashboard-actions">
          <button className="secondary-button" onClick={() => updateAppearance({ tone: dark ? 'Açık' : 'Siyah' })} aria-label={dark ? t('theme.switchToLight') : t('theme.switchToDark')}><Icon name="monitor" />{dark ? t('theme.light') : t('theme.dark')}</button>
          <button className="icon-button" onClick={() => setShowAppearance(true)} aria-label={t('app.settings')} title={t('app.settings')}><Icon name="settings" /></button>
        </div>
      </header>
      {(state.persistence?.error || state.persistence?.recovery) && <div role="alert" className="persistence-banner">
        {state.persistence.error ? t('details.saveError', undefined, { error: state.persistence.error }) : state.persistence.recovery}
      </div>}
      <div className={`dashboard-workspace ${detailsOpen && selected ? 'with-details' : ''}`}>
      <section className="dashboard-catalog" aria-label={t('nav.allModules')} hidden={detailsOpen && Boolean(selected)}>
        <div className="dashboard-heading"><div><span className="eyebrow">WinForge</span><h1>{pageTitle}</h1><p>{t('catalog.description')}</p></div><span className="dashboard-active-count"><i />{t('catalog.activeCount', undefined, { count: state.activeCount })}</span></div>
        <div className="catalog-toolbar"><span>{t('catalog.modules', undefined, { count: filtered.length })}</span><label>{t('catalog.status')}<select value={status} onChange={e => setStatus(e.target.value)}><option value="all">{t('catalog.allStatus')}</option><option value="active">{t('catalog.enabled')}</option><option value="inactive">{t('catalog.disabled')}</option><option value="error">{t('catalog.errors')}</option></select></label><SaveStatus persistence={state.persistence} /></div>
        <section className="dashboard-grid" aria-label="Modules">
          {filtered.map(module => <article className={`tool-card ${module.isActive ? 'is-enabled' : 'is-disabled'} ${detailsOpen && selected?.id === module.id ? 'selected' : ''} ${module.error ? 'has-error' : ''}`} key={module.id}>
            <button className="tool-card-open" aria-label={`${module.name}: ${t('card.openSettings')}`} aria-expanded={detailsOpen && selected?.id === module.id} aria-controls="module-details" title={t('card.openSettings')} onClick={event => { detailsOpener.current = event.currentTarget; setSelectedId(module.id); setDetailsOpen(true); }}></button>
            <div className="tool-card-intro"><span className="tool-icon"><Icon name={moduleIcon(module)} size={40} /></span><div><h2>{module.name}</h2><p>{module.description}</p></div></div>
            {module.error && <p className="tool-card-error">{t('card.error')}</p>}
            <div className="tool-card-footer"><button className={`tool-favorite ${favorites.includes(module.id) ? 'selected' : ''}`} aria-label={`${module.name}: ${favorites.includes(module.id) ? t('card.removeFavorite') : t('card.addFavorite')}`} title={favorites.includes(module.id) ? t('card.removeFavorite') : t('card.addFavorite')} aria-pressed={favorites.includes(module.id)} onClick={() => setFavorites(current => current.includes(module.id) ? current.filter(id => id !== module.id) : [...current, module.id])}><Icon name="star" size={24} /></button><Toggle checked={module.isActive} label={t('card.enableModule', undefined, { name: module.name })} onChange={enabled => send('module.toggle', { id: module.id, enabled })} /></div>
          </article>)}
          {filtered.length === 0 && <div className="dashboard-empty"><Icon name="search" size={32} /><h2>{favoritesOnly ? t('empty.noFavoritesTitle') : t('empty.noResultsTitle')}</h2><p>{favoritesOnly ? t('empty.noFavoritesDesc') : t('empty.noResultsDesc')}</p><button className="tool-settings-button" onClick={clearFilters}>{t('empty.clearFilters')}</button></div>}
        </section>
      </section>
      {detailsOpen && selected && <section className="dashboard-settings" aria-label={selected.name}>
        <div id="module-details"><ModuleDetails key={selected.id} module={selected} persistence={state.persistence} onToggle={enabled => send('module.toggle', { id: selected.id, enabled })} onUpdate={(key, value) => send('setting.update', { moduleId: selected.id, key, value })} onExecute={key => send('setting.execute', { moduleId: selected.id, key })} /></div>
      </section>}
      </div>
    </main>

    {showAppearance && <AppearanceDialog dataDirectory={state.dataDirectory} onOpenData={() => send('system.openData')} appearance={state.appearance} onClose={() => setShowAppearance(false)} onChange={updateAppearance} onOpenLogs={() => send('system.openLogs')} />}
    {error && <div className="toast-error" role="alert"><span>{error}</span><button onClick={() => setError('')} aria-label={t('modal.close')}><Icon name="close" size={15} /></button></div>}
  </div>;
}

