import { useEffect } from 'react';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { ShortcutField } from './ShortcutField';

type Rule = { id: string; name: string; enabled: boolean; shortcut: string; action: string; value: string; arguments: string; hasSecret: boolean };
export type ShortcutActionsState = { kind: 'shortcut-actions'; capturing: boolean; saveErrors?: Record<string, string>; actions: { id: string; name: string }[]; rules: Rule[]; pendingDisplay: boolean };
type Command = (action: string, data?: Record<string, unknown>) => void;

function Card({ rule, state, moduleId, active, command }: { rule: Rule; state: ShortcutActionsState; moduleId: string; active: boolean; command: Command }) {
  const [draft, setDraft, cancelSave] = useAutoSaveDraft(rule, next => command('save', { rule: next }));
  return <article className={`automation-card ${rule.enabled ? '' : 'is-off'}`}>
    <header><strong>{rule.name}</strong><label><input type="checkbox" checked={draft.enabled} onChange={e => setDraft({ ...draft, enabled: e.target.checked }, true)} /> Etkin</label></header>
    {state.saveErrors?.[rule.id] && <div className="error-box" role="alert"><strong>Değişiklik kaydedilemedi</strong><p>{state.saveErrors[rule.id]}</p><p>Önceki kayıt korundu: <kbd>{rule.shortcut}</kbd>{rule.enabled && active ? ' hâlâ geçerli.' : ' (kapalı).'}</p></div>}
    <p className="automation-hint">{rule.action === 'text-expand' ? 'Kayıtlı kısaltma: ' : 'Kayıtlı kısayol: '}<kbd>{rule.shortcut}</kbd>{rule.action === 'text-expand' && ' + Boşluk'}</p>
    <p className="automation-hint" role="status">{!rule.enabled ? 'Bu kısayol kapalı. Etkin kutusuyla açabilirsiniz.' : !active ? 'Kart etkin; çalışması için modülü açın.' : state.capturing ? 'Kısayol girişi sırasında geçici olarak durduruldu.' : 'Kısayol çalışmaya hazır.'}</p>
    <label>Ad<input className="white-control" value={draft.name} maxLength={100} onChange={e => setDraft({ ...draft, name: e.target.value })} /></label>
    <label>İşlem<select className="white-control" value={draft.action} onChange={e => setDraft({ ...draft, action: e.target.value, shortcut: e.target.value === 'text-expand' ? '.gk' : draft.action === 'text-expand' ? 'Ctrl+P' : draft.shortcut })}>{state.actions.map(a => <option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
    {draft.action === 'text-expand' ? <label>Kısaltma<input className="white-control" aria-label="Kısaltma" value={draft.shortcut} maxLength={64} placeholder=".gk" autoComplete="off" spellCheck={false} onChange={e => setDraft({ ...draft, shortcut: e.target.value })} /><small>Kısaltmayı yazıp boşluğa basın. Örnek: .gk → e-posta adresiniz. Büyük/küçük harf eşleşmelidir.</small></label> : <label>Kısayol<ShortcutField value={draft.shortcut} label={`${rule.name} kısayolu`} allowMouse capturing={state.capturing}
      onChange={shortcut => setDraft(current => ({ ...current, shortcut }))}
      onCommit={shortcut => setDraft(current => ({ ...current, shortcut }), true)}
      onCapture={active => command(active ? 'capture-begin' : 'capture-end')} /></label>}
    {moduleId === 'combination-translator' && <label>Hedef kombinasyon<ShortcutField value={draft.value} label={`${rule.name} hedef kombinasyon`} capturing={state.capturing} onChange={value => setDraft(current => ({ ...current, value }))} onCommit={value => setDraft(current => ({ ...current, value }), true)} onCapture={active => command(active ? 'capture-begin' : 'capture-end')} /></label>}
    {moduleId === 'text-shortcuts' && <label>Yazılacak metin / şifre<textarea className="white-control shortcut-text" aria-label="Yazılacak metin veya şifre" value={draft.value} maxLength={8192} placeholder="Yazılacak metni girin" autoComplete="off" spellCheck={false} onChange={e => setDraft({ ...draft, value: e.target.value })} /><small>Kayıtlı metin burada açıkça gösterilir. Değişiklikler otomatik kaydedilir. Boş bırakmak kayıtlı metni korur.</small></label>}
    {moduleId === 'program-shortcuts' && <><label>Program, dosya veya klasör yolu<input className="white-control" value={draft.value} placeholder="C:\Program Files\Uygulama\uygulama.exe veya D:\Belgeler" onChange={e => setDraft({ ...draft, value: e.target.value })} /></label><label>Argümanlar (isteğe bağlı)<input className="white-control" value={draft.arguments} onChange={e => setDraft({ ...draft, arguments: e.target.value })} /></label></>}
    <footer>{moduleId === 'monitor-shortcuts' && <button className="secondary-button" onClick={() => command('snapshot', { id: rule.id })}>Mevcut düzenle güncelle</button>}<button className="secondary-button" onClick={() => { cancelSave(); command('delete', { id: rule.id }); }}>Sil</button></footer>
  </article>;
}

export function ShortcutActionsPanel({ state, moduleId, active, command }: { state: ShortcutActionsState; moduleId: string; active: boolean; command: Command }) {
  useEffect(() => () => command('capture-end'), []);
  return <section className="automation-panel">
    <p className="automation-hint">{active ? 'Etkin kartlar kısayollarını bekler.' : 'Kısayolların çalışması için modülü açın.'} Değişiklikler otomatik kaydedilir ve uygulanır. Klavye veya Ctrl / Alt / Shift / Win ile fare tuşu ve tekerlek kullanabilirsiniz.</p>
    <p className="automation-hint">Örnekler: Ctrl+Space, Alt+WheelUp, Ctrl+Alt+MouseLeft. Kısayollar tam eşleşir. Yeni kartlar kapalı eklenir.</p>
    {moduleId === 'text-shortcuts' && <p className="automation-hint">İşlem alanından kısayol veya kısaltma seçin. Kısaltmayı yazıp boşluğa bastığınızda kayıtlı metin ve sonuna bir boşluk eklenir. Örnek: .gk → e-posta adresiniz.</p>}
    {state.capturing && <p role="status">Kısayol girişi sırasında bu modüllerin kısayolları geçici olarak durduruldu.</p>}
    {moduleId === 'monitor-shortcuts' && <div className="automation-master"><p>Mevcut ekran düzeninizi profil olarak kaydedin, adlandırın ve kısayol atayın. Ekranların açık/kapalı durumu, ana ekran, konum, çözünürlük, yön ve yenileme hızı WinForge ayarlarında saklanır.</p><p>Başka bir düzen kaydetmek için Windows ekran ayarlarında istediğiniz düzeni oluşturup yeni profil ekleyin. Mevcut bir profili değiştirmek için “Mevcut düzenle güncelle” düğmesini kullanın. Kısayolla uygulanan düzen 20 saniye içinde onaylanmazsa önceki düzene dönülür.</p></div>}
    {state.pendingDisplay && <div className="automation-master" role="alert"><strong>Bu monitör düzeni korunsun mu?</strong><p>20 saniye içinde onaylanmazsa önceki düzene dönülür.</p><button className="primary-button" onClick={() => command('confirm-display')}>Düzeni koru</button><button className="secondary-button" onClick={() => command('revert-display')}>Geri al</button></div>}
    {state.rules.map(rule => <Card key={rule.id} rule={rule} state={state} moduleId={moduleId} active={active} command={command} />)}
    <button className="primary-button automation-add" disabled={state.rules.length >= 64} onClick={() => command('add')}>{moduleId === 'monitor-shortcuts' ? '+ Mevcut düzeni profil olarak kaydet' : '+ Kısayol ekle'}</button>
  </section>;
}
