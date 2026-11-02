import { useEffect } from 'react';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { ShortcutField } from './ShortcutField';
import { t, getLanguage } from './localization';
import { RuleWorkbench } from './RuleWorkbench';
import { copy } from './moduleDesigns';

type Rule = { id: string; name: string; enabled: boolean; shortcut: string; action: string; value: string; arguments: string; hasSecret: boolean };
export type ShortcutActionsState = { kind: 'shortcut-actions'; capturing: boolean; saveErrors?: Record<string, string>; actions: { id: string; name: string }[]; rules: Rule[]; pendingDisplay: boolean };
type Command = (action: string, data?: Record<string, unknown>) => void;

function Card({ rule, state, moduleId, active, command }: { rule: Rule; state: ShortcutActionsState; moduleId: string; active: boolean; command: Command }) {
  const isTr = getLanguage() === 'tr';
  const [draft, setDraft, cancelSave] = useAutoSaveDraft(rule, next => command('save', { rule: next }));
  const statusHint = !rule.enabled
    ? (isTr ? 'Bu kısayol kapalı. Etkin kutusuyla açabilirsiniz.' : 'This shortcut is off. Enable it with the checkbox.')
    : !active
    ? (isTr ? 'Kart etkin; çalışması için modülü açın.' : 'Card is enabled; turn on the module to use.')
    : state.capturing
    ? (isTr ? 'Kısayol girişi sırasında geçici olarak durduruldu.' : 'Temporarily paused during shortcut recording.')
    : (isTr ? 'Kısayol çalışmaya hazır.' : 'Shortcut ready to use.');

  return <article className={`automation-card ${rule.enabled ? '' : 'is-off'}`}>
    <header><strong>{rule.name}</strong><label><input type="checkbox" checked={draft.enabled} onChange={e => setDraft({ ...draft, enabled: e.target.checked }, true)} /> {t('shortcuts.enabled')}</label></header>
    {state.saveErrors?.[rule.id] && <div className="error-box" role="alert"><strong>{t('shortcuts.saveFailed')}</strong><p>{state.saveErrors[rule.id]}</p><p>{t('shortcuts.fallbackKept', undefined, { shortcut: rule.shortcut })}</p></div>}
    <p className="automation-hint">{rule.action === 'text-expand' ? t('shortcuts.savedAbbr') : t('shortcuts.savedShortcut')}<kbd>{rule.shortcut}</kbd>{rule.action === 'text-expand' && ' + Space'}</p>
    <p className="automation-hint" role="status">{statusHint}</p>
    <div className="rule-fields"><label>{t('shortcuts.name')}<input className="white-control" value={draft.name} maxLength={100} onChange={e => setDraft({ ...draft, name: e.target.value })} /></label>
    <label>{t('shortcuts.action')}<select className="white-control" value={draft.action} onChange={e => setDraft({ ...draft, action: e.target.value, shortcut: e.target.value === 'text-expand' ? '.gk' : draft.action === 'text-expand' ? 'Ctrl+P' : draft.shortcut })}>{state.actions.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
    {draft.action === 'text-expand' ? <label>{t('shortcuts.abbr')}<input className="white-control" aria-label={t('shortcuts.abbr')} value={draft.shortcut} maxLength={64} placeholder=".gk" autoComplete="off" spellCheck={false} onChange={e => setDraft({ ...draft, shortcut: e.target.value })} /><small>{t('shortcuts.abbrHint')}</small></label> : <label>{t('shortcuts.shortcut')}<ShortcutField value={draft.shortcut} label={`${rule.name} shortcut`} allowMouse capturing={state.capturing}
      onChange={shortcut => setDraft(current => ({ ...current, shortcut }))}
      onCommit={shortcut => setDraft(current => ({ ...current, shortcut }), true)}
      onCapture={capturing => command(capturing ? 'capture-begin' : 'capture-end')} /></label>}
    {moduleId === 'media-shortcuts' && ['volume-up', 'volume-down', 'volume-down-5'].includes(draft.action) && <label>{t('shortcuts.volumeStep')}<input className="white-control" type="number" min={1} max={100} value={draft.value || (draft.action === 'volume-down-5' ? '5' : '2')} onChange={event => setDraft({ ...draft, value: event.target.value })} /><small>{t('shortcuts.volumeStepHint')}</small></label>}
    {moduleId === 'combination-translator' && <label>{t('shortcuts.targetCombination')}<ShortcutField value={draft.value} label={`${rule.name} target combination`} capturing={state.capturing} onChange={value => setDraft(current => ({ ...current, value }))} onCommit={value => setDraft(current => ({ ...current, value }), true)} onCapture={capturing => command(capturing ? 'capture-begin' : 'capture-end')} /></label>}
    {moduleId === 'text-shortcuts' && <label>{t('shortcuts.textOrPassword')}<textarea className="white-control shortcut-text" aria-label={t('shortcuts.textOrPassword')} value={draft.value} maxLength={8192} placeholder={t('shortcuts.textPlaceholder')} autoComplete="off" spellCheck={false} onChange={e => setDraft({ ...draft, value: e.target.value })} /><small>{t('shortcuts.textHint')}</small></label>}
    {moduleId === 'program-shortcuts' && draft.action !== 'downloads' && <><label>{t('shortcuts.programPath')}<input className="white-control" value={draft.value} placeholder="C:\Program Files\App\app.exe" onChange={e => setDraft({ ...draft, value: e.target.value })} /></label><label>{t('shortcuts.arguments')}<input className="white-control" value={draft.arguments} onChange={e => setDraft({ ...draft, arguments: e.target.value })} /></label></>}
    </div><footer>{moduleId === 'monitor-shortcuts' && <button className="secondary-button" onClick={() => command('snapshot', { id: rule.id })}>{t('shortcuts.updateLayout')}</button>}<button className="secondary-button danger-action" onClick={() => { cancelSave(); command('delete', { id: rule.id }); }}>{t('shortcuts.delete')}</button></footer>
  </article>;
}

export function ShortcutActionsPanel({ state, moduleId, active, command, filterQuery='',onClearFilter }: { state: ShortcutActionsState; moduleId: string; active: boolean; command: Command; filterQuery?:string;onClearFilter?:()=>void }) {
  useEffect(() => () => command('capture-end'), []);
  return <section className={`automation-panel shortcut-workspace shortcut-workspace-${moduleId}`}>
    <details className="workspace-help"><summary>{copy(['Kısayol kullanımı ve örnekler','Shortcut usage and examples'])}</summary><p className="automation-hint">{t('shortcuts.hint')}</p>
    <p className="automation-hint">{t('shortcuts.examples')}</p></details>
    {moduleId === 'text-shortcuts' && <p className="automation-hint">{t('shortcuts.textDesc')}</p>}
    {moduleId === 'system-shortcuts' && <p className="automation-hint">{t('shortcuts.systemDesc')}</p>}
    {state.capturing && <p role="status">{t('shortcuts.capturing')}</p>}
    {moduleId === 'monitor-shortcuts' && <div className="automation-master"><p>{t('shortcuts.monitorDesc1')}</p><p>{t('shortcuts.monitorDesc2')}</p></div>}
    {state.pendingDisplay && <div className="automation-master" role="alert"><strong>{t('shortcuts.displayPromptTitle')}</strong><p>{t('shortcuts.displayPromptDesc')}</p><button className="primary-button" onClick={() => command('confirm-display')}>{t('shortcuts.keepLayout')}</button><button className="secondary-button" onClick={() => command('revert-display')}>{t('shortcuts.revertLayout')}</button></div>}
    <RuleWorkbench rules={state.rules} filterQuery={filterQuery} kind={moduleId==='monitor-shortcuts'?'profile':moduleId==='combination-translator'?'mapping':'shortcut'} render={rule=><Card key={rule.id} rule={rule} state={state} moduleId={moduleId} active={active} command={command}/>}/>
    <button className="primary-button automation-add" disabled={state.rules.length >= 64} onClick={() => {onClearFilter?.();command('add');}}>{moduleId === 'monitor-shortcuts' ? t('shortcuts.saveDisplayProfile') : t('shortcuts.addShortcut')}</button>
  </section>;
}
