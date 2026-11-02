import { useAutoSaveDraft } from './useAutoSaveDraft';
import { ShortcutField as SharedShortcutField } from './ShortcutField';
import { t } from './localization';

type Step = { kind: string; value: string; delay: number };
type Rule = { id: string; name: string; enabled: boolean; shortcut: string; repeat: boolean; interval: number; steps: Step[]; running: boolean; processName?: string };
export type AutomationState = { paused: boolean; capturing: boolean; masterShortcut: string; rules: Rule[] };
type Command = (action: string, data?: Record<string, unknown>) => void;

function Switch({ checked, label, change }: { checked: boolean; label: string; change: (checked: boolean) => void }) {
  return <button type="button" role="switch" aria-label={label} aria-checked={checked} className={`toggle ${checked ? 'on' : ''}`} onClick={() => change(!checked)}><span /></button>;
}

function ShortcutField({ value, label, command, capturing, save }: { value: string; label: string; command: Command; capturing: boolean; save: (value: string) => void }) {
  const [text, setText] = useAutoSaveDraft(value, save);
  return <SharedShortcutField value={text} label={label} capturing={capturing} onChange={setText}
    onCapture={active => command(active ? 'capture-begin' : 'capture-end')}
    onCommit={next => { if (next !== value) setText(next, true); }} />;
}

function AutomationCard({ rule, command, capturing, active, paused }: { rule: Rule; command: Command; capturing: boolean; active: boolean; paused: boolean }) {
  const [draft, setDraft, cancelSave] = useAutoSaveDraft(rule, next => command('save', { rule: next }));
  const persist = (next: Rule) => setDraft(next, true);
  const stepChange = (index: number, patch: Partial<Step>, immediate = false) => {
    const next = { ...draft, steps: draft.steps.map((step, position) => position === index ? { ...step, ...patch } : step) };
    if (immediate) persist(next); else setDraft(next);
  };
  const statusLabel = !rule.enabled
    ? t('auto.statusOff')
    : !active
    ? t('auto.statusModuleOff')
    : paused
    ? t('auto.statusPaused')
    : rule.running
    ? t('auto.statusRunning')
    : t('auto.statusWaiting');

  return <article className={`automation-card ${rule.enabled ? '' : 'is-off'}`}>
    <header><div><strong>{rule.name}</strong><small>{statusLabel}</small></div><Switch checked={rule.enabled} label={`${rule.name}: ${t('shortcuts.enabled')}`} change={enabled => persist({ ...draft, enabled })} /></header>
    <label>{t('auto.name')}<input className="white-control" value={draft.name} maxLength={100} onChange={event => setDraft({ ...draft, name: event.target.value })} onBlur={() => { if (draft.name !== rule.name) persist(draft); }} /></label>
    <label>{t('auto.shortcut')}<ShortcutField value={draft.shortcut} label={`${rule.name} ${t('auto.shortcut')}`} capturing={capturing} command={command} save={shortcut => persist({ ...draft, shortcut })} /></label>
    <label>{t('auto.process')}<input className="white-control" aria-label={`${rule.name} process`} value={draft.processName ?? ''} placeholder={t('auto.processPlaceholder')} onChange={event => setDraft({ ...draft, processName: event.target.value })} /><small>{t('auto.processHint')}</small></label>
    <label>{t('auto.mode')}<select className="white-control" value={draft.repeat ? 'toggle' : 'once'} onChange={event => persist({ ...draft, repeat: event.target.value === 'toggle' })}><option value="toggle">{t('auto.modeToggle')}</option><option value="once">{t('auto.modeOnce')}</option></select></label>
    <div className="automation-interval"><label>{t('auto.interval')}<input className="white-control" type="number" min={10} max={3600000} step={10} value={draft.interval} aria-label={`${rule.name} interval`} onChange={event => setDraft({ ...draft, interval: Number(event.target.value) })} onBlur={() => { if (draft.interval !== rule.interval) persist(draft); }} onKeyDown={event => { if (event.key === 'Enter') persist(draft); }} /></label>
      <input className="range-input" type="range" min={10} max={Math.max(2000, draft.interval)} step={10} value={draft.interval} aria-label={`${rule.name} interval slider`} onChange={event => setDraft({ ...draft, interval: Number(event.target.value) })} onPointerUp={() => persist(draft)} onKeyUp={() => persist(draft)} />
      <small>{draft.repeat ? t('auto.intervalRepeatHint') : t('auto.intervalOnceHint')}</small>
    </div>
    <div className="automation-steps"><strong>{t('auto.steps')}</strong>{draft.steps.map((step, index) => <div className="automation-step" key={index}>
      <span className="automation-step-number">{index + 1}</span><div className="automation-step-fields"><select className="white-control" value={step.kind} aria-label={`${index + 1}. step kind`} onChange={event => {
        const kind = event.target.value;
        const next = { ...draft, repeat: kind === 'search' ? false : draft.repeat, steps: draft.steps.map((item, position) => position === index ? { kind, value: ({ click: 'left', key: 'Enter', text: 'Hello', search: 'google', wait: '' } as Record<string, string>)[kind], delay: kind === 'wait' ? 500 : 0 } : item) };
        persist(next);
      }}><option value="click">{t('auto.stepClick')}</option><option value="key">{t('auto.stepKey')}</option><option value="text">{t('auto.stepText')}</option><option value="wait">{t('auto.stepWait')}</option><option value="search">{t('auto.stepSearch')}</option></select>
      {step.kind === 'click' && <select className="white-control" aria-label={`${index + 1}. mouse button`} value={step.value} onChange={event => stepChange(index, { value: event.target.value }, true)}><option value="left">{t('auto.clickLeft')}</option><option value="right">{t('auto.clickRight')}</option><option value="middle">{t('auto.clickMiddle')}</option></select>}
      {step.kind === 'key' && <ShortcutField value={step.value} label={`${index + 1}. key to send`} command={command} capturing={capturing} save={value => stepChange(index, { value }, true)} />}
      {step.kind === 'text' && <input className="white-control" aria-label={`${index + 1}. text to type`} placeholder={t('auto.textPlaceholder')} value={step.value} onChange={event => stepChange(index, { value: event.target.value })} onBlur={() => persist(draft)} />}
      {step.kind === 'wait' && <label>{t('auto.waitMs')}<input className="white-control" type="number" min={10} max={3600000} value={step.delay} aria-label={`${index + 1}. wait ms`} onChange={event => stepChange(index, { delay: Number(event.target.value) })} onBlur={() => persist(draft)} /></label>}
      </div><button type="button" className="secondary-button" disabled={draft.steps.length === 1} aria-label={`${index + 1}. step delete`} onClick={() => persist({ ...draft, steps: draft.steps.filter((_, position) => position !== index) })}>×</button>
    </div>)}</div>
    <footer><button className="secondary-button" disabled={draft.steps.length >= 100} onClick={() => persist({ ...draft, steps: [...draft.steps, { kind: 'click', value: 'left', delay: 0 }] })}>{t('auto.addStep')}</button><button className="secondary-button" onClick={() => { cancelSave(); command('delete', { id: rule.id }); }}>{t('auto.delete')}</button></footer>
  </article>;
}

export function AutomationPanel({ state, active, command }: { state: AutomationState; active: boolean; command: Command }) {
  return <section className="automation-panel">
    <div className="automation-master"><div className="setting-row"><div className="setting-copy"><strong>{t('auto.masterTitle')}</strong><p>{state.paused ? t('auto.masterPaused') : t('auto.masterActive')}</p></div><Switch checked={!state.paused} label={t('auto.enableAll')} change={enabled => command('pause', { paused: !enabled })} /></div>
      <label>{t('auto.masterShortcut')}<ShortcutField value={state.masterShortcut} label={t('auto.masterShortcut')} command={command} capturing={state.capturing} save={shortcut => command('master', { shortcut })} /></label>
      <button className="secondary-button" onClick={() => command('stop-all')}>{t('auto.stopAll')}</button>
    </div>
    <p className="automation-hint">{t('auto.hint')}</p>
    {state.capturing && <p className="automation-hint" role="status">{t('auto.capturing')}</p>}
    {state.rules.map(rule => <AutomationCard key={rule.id} rule={rule} command={command} capturing={state.capturing} active={active} paused={state.paused} />)}
    <button className="primary-button automation-add" disabled={state.rules.length >= 64} onClick={() => command('add')}>{t('auto.add')}</button>
  </section>;
}
