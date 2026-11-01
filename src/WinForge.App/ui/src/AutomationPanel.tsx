import { useAutoSaveDraft } from './useAutoSaveDraft';
import { ShortcutField as SharedShortcutField } from './ShortcutField';

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
  return <article className={`automation-card ${rule.enabled ? '' : 'is-off'}`}>
    <header><div><strong>{rule.name}</strong><small>{!rule.enabled ? 'Kapalı' : !active ? 'Modül kapalı' : paused ? 'Duraklatıldı' : rule.running ? 'Çalışıyor' : 'Kısayol bekleniyor'}</small></div><Switch checked={rule.enabled} label={`${rule.name} otomasyonunu etkinleştir`} change={enabled => persist({ ...draft, enabled })} /></header>
    <label>Otomasyon adı<input className="white-control" value={draft.name} maxLength={100} onChange={event => setDraft({ ...draft, name: event.target.value })} onBlur={() => { if (draft.name !== rule.name) persist(draft); }} /></label>
    <label>Başlat / durdur kısayolu<ShortcutField value={draft.shortcut} label={`${rule.name} başlat durdur kısayolu`} capturing={capturing} command={command} save={shortcut => persist({ ...draft, shortcut })} /></label>
    <label>Yalnızca bu uygulama odaktayken (isteğe bağlı)<input className="white-control" aria-label={`${rule.name} odaktaki exe`} value={draft.processName ?? ''} placeholder="oyun.exe · boşsa tüm uygulamalar" onChange={event => setDraft({ ...draft, processName: event.target.value })} /><small>İşlem adı veya tam exe yolu. Odak başka uygulamaya geçince çalışan otomasyon durur.</small></label>
    <label>Çalışma şekli<select className="white-control" value={draft.repeat ? 'toggle' : 'once'} onChange={event => persist({ ...draft, repeat: event.target.value === 'toggle' })}><option value="toggle">Aynı kısayolla aç / kapat · tekrarla</option><option value="once">Kısayola basınca bir kez çalıştır</option></select></label>
    <div className="automation-interval"><label>Tekrar aralığı (ms)<input className="white-control" type="number" min={10} max={3600000} step={10} value={draft.interval} aria-label={`${rule.name} tekrar aralığı ms`} onChange={event => setDraft({ ...draft, interval: Number(event.target.value) })} onBlur={() => { if (draft.interval !== rule.interval) persist(draft); }} onKeyDown={event => { if (event.key === 'Enter') persist(draft); }} /></label>
      <input className="range-input" type="range" min={10} max={Math.max(2000, draft.interval)} step={10} value={draft.interval} aria-label={`${rule.name} aralık kaydırıcısı`} onChange={event => setDraft({ ...draft, interval: Number(event.target.value) })} onPointerUp={() => persist(draft)} onKeyUp={() => persist(draft)} />
      <small>{draft.repeat ? 'Dizinin sonundan sonraki tekrarına kadar bekleme.' : 'Tek seferlik komutta tekrar aralığı kullanılmaz.'}</small>
    </div>
    <div className="automation-steps"><strong>Komut adımları</strong>{draft.steps.map((step, index) => <div className="automation-step" key={index}>
      <span className="automation-step-number">{index + 1}</span><div className="automation-step-fields"><select className="white-control" value={step.kind} aria-label={`${index + 1}. komut türü`} onChange={event => {
        const kind = event.target.value;
        const next = { ...draft, repeat: kind === 'search' ? false : draft.repeat, steps: draft.steps.map((item, position) => position === index ? { kind, value: ({ click: 'left', key: 'Enter', text: 'Merhaba', search: 'google', wait: '' } as Record<string, string>)[kind], delay: kind === 'wait' ? 500 : 0 } : item) };
        persist(next);
      }}><option value="click">Fare tıklaması</option><option value="key">Tuş gönder</option><option value="text">Metin yaz</option><option value="wait">Bekle</option><option value="search">Seçili metni Google’da ara</option></select>
      {step.kind === 'click' && <select className="white-control" aria-label={`${index + 1}. fare düğmesi`} value={step.value} onChange={event => stepChange(index, { value: event.target.value }, true)}><option value="left">Sol tık</option><option value="right">Sağ tık</option><option value="middle">Orta tık</option></select>}
      {step.kind === 'key' && <ShortcutField value={step.value} label={`${index + 1}. gönderilecek tuş`} command={command} capturing={capturing} save={value => stepChange(index, { value }, true)} />}
      {step.kind === 'text' && <input className="white-control" aria-label={`${index + 1}. gönderilecek metin`} placeholder="Yazılacak metin" value={step.value} onChange={event => stepChange(index, { value: event.target.value })} onBlur={() => persist(draft)} />}
      {step.kind === 'wait' && <label>Bekleme (ms)<input className="white-control" type="number" min={10} max={3600000} value={step.delay} aria-label={`${index + 1}. bekleme ms`} onChange={event => stepChange(index, { delay: Number(event.target.value) })} onBlur={() => persist(draft)} /></label>}
      </div><button type="button" className="secondary-button" disabled={draft.steps.length === 1} aria-label={`${index + 1}. adımı sil`} onClick={() => persist({ ...draft, steps: draft.steps.filter((_, position) => position !== index) })}>×</button>
    </div>)}</div>
    <footer><button className="secondary-button" disabled={draft.steps.length >= 100} onClick={() => persist({ ...draft, steps: [...draft.steps, { kind: 'click', value: 'left', delay: 0 }] })}>+ Adım ekle</button><button className="secondary-button" onClick={() => { cancelSave(); command('delete', { id: rule.id }); }}>Otomasyonu sil</button></footer>
  </article>;
}

export function AutomationPanel({ state, active, command }: { state: AutomationState; active: boolean; command: Command }) {
  return <section className="automation-panel">
    <div className="automation-master"><div className="setting-row"><div className="setting-copy"><strong>Otomasyon kısayolları</strong><p>{state.paused ? 'Tümü duraklatıldı. Devam etmek için anahtarı veya genel kısayolu kullanın.' : 'Etkin otomasyonlar kendi kısayollarını bekler.'}</p></div><Switch checked={!state.paused} label="Tüm otomasyonları etkinleştir" change={enabled => command('pause', { paused: !enabled })} /></div>
      <label>Tümünü duraklat / devam et kısayolu<ShortcutField value={state.masterShortcut} label="Genel otomasyon başlat durdur kısayolu" command={command} capturing={state.capturing} save={shortcut => command('master', { shortcut })} /></label>
      <button className="secondary-button" onClick={() => command('stop-all')}>Çalışan tüm komutları durdur</button>
    </div>
    <p className="automation-hint">Her otomasyonun kısayolu ve komutları birbirinden bağımsızdır. Değişiklikler otomatik kaydedilir ve uygulanır. Değişiklikler çalışan komutları durdurur. Devam etmek, komutları yeniden çalıştırmaz; kısayolu bekler.</p>
    {state.capturing && <p className="automation-hint" role="status">Tuş atama sırasında otomasyonlar geçici olarak durduruldu. İptal: Esc.</p>}
    {state.rules.map(rule => <AutomationCard key={rule.id} rule={rule} command={command} capturing={state.capturing} active={active} paused={state.paused} />)}
    <button className="primary-button automation-add" disabled={state.rules.length >= 64} onClick={() => command('add')}>+ Otomasyon ekle</button>
  </section>;
}
