import React, { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import type { AutomationState } from './AutomationPanel';
import type { AppState, Module } from './types';
import { Icon } from './Icon';
import { Toggle } from './Toggle';
import { SettingControl } from './SettingControl';
import { ErrorBoundary } from './ErrorBoundary';
import { ModulePreview } from './ModulePreview';
import { ActionMatrix } from './ActionMatrix';
import { DesignedSetting } from './DesignedSetting';
import { resolveDesign, copy, controlDesign } from './moduleDesigns';
import { inactiveDependencies } from './settingDependencies';
import { moduleIcon } from './modulePresentation';
import { send } from './bridge';
import { t, getLanguage } from './localization';

const AutomationPanel = React.lazy(() => import('./AutomationPanel').then(m => ({ default: m.AutomationPanel })));
const ContextMenuPanel = React.lazy(() => import('./ContextMenuPanel').then(m => ({ default: m.ContextMenuPanel })));
const ShortcutActionsPanel = React.lazy(() => import('./ShortcutActionsPanel').then(m => ({ default: m.ShortcutActionsPanel })));
const DefaultPathsPanel = React.lazy(() => import('./features/DefaultSettingsPanels').then(m => ({ default: m.DefaultPathsPanel })));
const DefaultAppsPanel = React.lazy(() => import('./features/DefaultSettingsPanels').then(m => ({ default: m.DefaultAppsPanel })));

export function SaveStatus({ persistence }: { persistence?: AppState['persistence'] }) {
  return <span className={`save-status ${persistence?.error ? 'has-error' : ''}`} role="status" aria-live="polite">
    <Icon name={persistence?.pending ? 'layers' : persistence?.error ? 'close' : 'check'} size={15} />
    {persistence?.error ? t('details.saveError', undefined, { error: persistence.error })
      : persistence?.pending ? t('details.savePending') : persistence ? t('details.saved') : t('details.autoSave')}
  </span>;
}

function ManagementPanel({ module,filterQuery='',onClearFilter }: { module: Module;filterQuery?:string;onClearFilter?:()=>void }) {
  const command = (action: string, data: Record<string, unknown> = {}) => send('module.command', { moduleId: module.id, action, data });
  const panel = module.panel;
  if (module.panelError) return <div className="error-box" role="alert"><strong>{t('details.panelError')}</strong><p>{module.panelError}</p>
    <button className="secondary-button" onClick={() => send('module.inspect', { moduleId: module.id })}>{t('details.reloadSettings')}</button></div>;
  if (!module.panelLoaded && !panel) return <div className="panel-loading" role="status"><span className="loader" />{t('details.loadingSettings')}</div>;
  return <ErrorBoundary key={module.id}><Suspense fallback={<p role="status">{t('details.loadingSettings')}</p>}>
    {module.id === 'automation' && panel && <AutomationPanel state={panel as AutomationState} active={module.isActive} command={command} filterQuery={filterQuery} onClearFilter={onClearFilter} />}
    {panel && 'kind' in panel && panel.kind === 'default-paths' && <DefaultPathsPanel inline state={panel.state} sendCommand={command} filterQuery={filterQuery} />}
    {panel && 'kind' in panel && panel.kind === 'default-apps' && <DefaultAppsPanel inline state={panel.state} sendCommand={command} filterQuery={filterQuery} />}
    {panel && 'kind' in panel && panel.kind === 'context-menu' && <ContextMenuPanel state={panel} command={command} filterQuery={filterQuery} />}
    {panel && 'kind' in panel && panel.kind === 'shortcut-actions' && <ShortcutActionsPanel state={panel} moduleId={module.id} active={module.isActive} command={command} filterQuery={filterQuery} onClearFilter={onClearFilter} />}
    {panel && 'kind' in panel && panel.kind === 'start-sections' && panel.buttons.map(button => <div className="setting-row" key={button.id}>
      <strong>{button.name}</strong><Toggle checked={button.visible} label={button.name} onChange={visible => command('place-visibility', { id: button.id, visible })} /></div>)}
    {panel && 'kind' in panel && panel.kind === 'start-sections' && !panel.buttons.length && <p className="management-empty">{copy(['Bu Windows sürümünde yönetilebilir alt klasör düğmesi bildirilmedi. Menü içeriğini diğer bölümlerden düzenleyebilirsin.','This Windows version reported no manageable folder buttons. You can customize menu content in the other sections.'])}</p>}
  </Suspense></ErrorBoundary>;
}

export function ModuleDetails({ module, persistence, onToggle, onUpdate, onExecute }: {
  module: Module;
  persistence?: AppState['persistence'];
  onToggle: (value: boolean) => void;
  onUpdate: (key: string, value: unknown) => void;
  onExecute: (key: string) => void;
}) {
  const [sectionKey, setSectionKey] = useState('');
  const [query, setQuery] = useState('');
  const scroll = useRef<HTMLDivElement>(null);
  const hasPanel = Boolean(module.panelAvailable || module.panel);
  const language = getLanguage();
  const sections = useMemo(() => {
    const resolved = resolveDesign(module);
    const pages = resolved.pages;
    if (hasPanel && !pages.some(page => page.management)) pages.unshift({ id:'management',title:[t('details.management'),t('details.management')],description:['',''],icon:'layers',groups:[],management:true });
    return { ...resolved, pages };
  }, [module, hasPanel, language]);
  const { design,pages }=sections;
  const activeSection = pages.some(page=>page.id===sectionKey) ? sectionKey : pages[0]?.id || '';
  const needle = query.trim().toLocaleLowerCase();
  const matches = pages.map(page=>({...page,groups:page.groups.map(group=>({...group,settings:group.settings.filter(setting=>!needle
    || `${setting.label} ${setting.description??''} ${setting.section??''} ${copy(page.title)} ${copy(group.title)}`.toLocaleLowerCase().includes(needle))}))}));
  const count=matches.reduce((sum,page)=>sum+page.groups.reduce((total,group)=>total+group.settings.length,0),0);
  useEffect(() => { if (scroll.current) scroll.current.scrollTop = 0; }, [activeSection, needle]);
  const selectSection = (key: string) => { setSectionKey(key); setQuery(''); };

  return <div className={`module-detail-page module-design-${module.id}`} data-module-design={design ? module.id : 'fallback'}>
    <header className="details-header">
      <div className="details-icon"><Icon name={moduleIcon(module)} size={34} /></div>
      <div className="details-title"><span className="details-category">{module.category}</span><h2>{module.name}</h2><p className="details-description">{module.description}</p></div>
      <div className="details-enable"><span>{module.isActive ? t('catalog.enabled') : t('catalog.disabled')}</span>
        <Toggle checked={module.isActive} label={t('card.enableModule', undefined, { name: module.name })} onChange={onToggle} /></div>
    </header>
    {module.warning && <div className="warning-box"><Icon name="layers" /><div><strong>{t('details.infoTitle')}</strong><p>{module.warning}</p></div></div>}
    {module.error && <div className="error-box" role="alert"><strong>{t('details.errorTitle')}</strong><p>{module.error}</p></div>}
    {!module.isActive && <p className="inactive-hint"><Icon name="power" size={16} />{t('details.inactiveHint')}</p>}
    <div className="details-toolbar"><label className="settings-search"><Icon name="search" size={18} />
      <input type="search" value={query} aria-label={t('details.searchLabel')} placeholder={t('details.search')} onChange={e => setQuery(e.target.value)} />
    </label><SaveStatus persistence={persistence} /></div>
    <div className={`details-body ${pages.length<=1 ? 'single-design-page':''}`}>
      {pages.length>1 &&
      <nav className="settings-navigation" aria-label={t('details.settingsJumps')}>
        {pages.map(page => <button key={page.id} aria-current={!needle && activeSection === page.id ? 'location' : undefined}
          onClick={() => selectSection(page.id)}><Icon name={page.icon}/><span>{copy(page.title)}</span><small>{page.groups.reduce((n,g)=>n+g.settings.length,0)||''}</small></button>)}
      </nav>}
      <div className="details-scroll" ref={scroll}>
        {needle && <div className="settings-results" role="status">{hasPanel?copy(['Yönetim paneli ve ayarlarda arama','Searching management entries and settings']):t('details.searchResults', undefined, { count })}
          <button className="text-button" onClick={() => setQuery('')}>{t('details.clearSearch')}</button></div>}
        {!needle && design && <details className="module-usage"><summary><Icon name="bolt" size={16}/>{copy(['Nasıl kullanılır?','How to use'])}</summary><p>{copy(design.purpose)}</p><ol>{design.steps.map((step,index)=><li key={index}>{copy(step)}</li>)}</ol></details>}
        {!needle && design?.preview && <details className="module-preview-disclosure" key={activeSection} open={activeSection===pages[0]?.id}><summary>{copy(['Görünüm önizlemesi','Appearance preview'])}</summary><ModulePreview module={module} /></details>}
        {matches.map(page=><div className={`design-page page-${page.id}`} key={page.id} hidden={needle ? !page.management&&!page.groups.some(g=>g.settings.length) : activeSection!==page.id}>
          <header className="design-page-heading"><h3>{copy(page.title)}</h3><p>{copy(page.description)}</p></header>
          {page.management && <section className="settings-section management-section" aria-label={copy(page.title)}><ManagementPanel module={module} filterQuery={query} onClearFilter={()=>setQuery('')}/></section>}
          <div className="design-groups">{page.groups.map(group=><section className={`settings-section design-group group-${group.id} group-layout-${group.layout??'rows'}`} key={group.id} data-design-group={group.id}
            id={`settings-${module.id}-${group.id}`} hidden={!group.settings.length} aria-labelledby={`heading-${module.id}-${group.id}`}>
            <div className="settings-section-heading"><div><h3 id={`heading-${module.id}-${group.id}`}>{copy(group.title)}</h3>{group.description[1]!=='Adjust your preferences. Changes are saved automatically.'&&<p>{copy(group.description)}</p>}</div><span>{t('details.settingCount',undefined,{count:group.settings.length})}</span></div>
            {group.layout==='matrix' && !needle ? <ActionMatrix settings={group.settings} onUpdate={onUpdate}/> : <div className="design-group-controls">{group.settings.map(setting=>{
              const presentation=controlDesign(module.id,setting);
              const dependencies=inactiveDependencies(module,setting.key);
              return <div className="designed-setting-slot" key={setting.key} {...(presentation==='default'?{'data-setting-key':setting.key}:{})}>
                {presentation==='default' ? <SettingControl setting={setting} onUpdate={onUpdate} onExecute={onExecute}/>
                  : <DesignedSetting moduleId={module.id} setting={setting} design={presentation} onUpdate={onUpdate}/>}
                {dependencies.length>0&&<p className="setting-dependency"><Icon name="power" size={13}/>{copy(['Uygulanması için aç: ','Enable to apply: '])}{dependencies.join(' · ')}</p>}
              </div>;
            })}</div>}
          </section>)}</div>
        </div>)}
        {needle && count === 0 && !hasPanel && <div className="dashboard-empty"><Icon name="search" size={30} /><h3>{t('empty.noResultsTitle')}</h3><p>{t('empty.noResultsDesc')}</p></div>}
        {!hasPanel && !pages.length && <section className="module-operation-card"><div className={`operation-mark ${module.isActive?'enabled':''}`}><Icon name={module.id==='startup'?'power':'folder'} size={42}/></div><h3>{design ? copy(design.purpose) : module.name}</h3><p>{module.isActive ? t('details.stateEnabled') : t('details.stateDisabled')}</p>{design && <ol>{design.steps.map((step,index)=><li key={index}>{copy(step)}</li>)}</ol>}</section>}
      </div>
    </div>
  </div>;
}
