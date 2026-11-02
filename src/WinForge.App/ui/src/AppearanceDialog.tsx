import { useEffect, useRef } from 'react';
import type { Appearance } from './types';
import { Toggle } from './Toggle';
import { Icon } from './Icon';
import { t } from './localization';

export function AppearanceDialog({ appearance, onClose, onChange, onOpenLogs, dataDirectory, onOpenData }: { dataDirectory?: string; onOpenData: () => void; appearance: Appearance; onClose: () => void; onChange: (value: Record<string, unknown>) => void; onOpenLogs: () => void }) {
  const dialogRef = useRef<HTMLElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    dialogRef.current?.querySelector<HTMLButtonElement>('button')?.focus();
    return () => previous?.focus();
  }, []);

  const currentLang = appearance.language ?? 'en';

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
    <header><div><span className="eyebrow">{t('modal.customization')}</span><h2 id="appearance-title">{t('modal.appearanceTitle')}</h2></div><button className="icon-button" onClick={onClose} aria-label={t('modal.close')}><Icon name="close" /></button></header>

    <div className="appearance-section">
      <h3>{t('modal.language')}</h3>
      <div className="tone-options lang-options">
        <button
          className={`lang-option ${currentLang === 'en' ? 'selected' : ''}`}
          onClick={() => onChange({ language: 'en' })}
        >
          <span>English (Default)</span>
          {currentLang === 'en' && <Icon name="check" size={15} />}
        </button>
        <button
          className={`lang-option ${currentLang === 'tr' ? 'selected' : ''}`}
          onClick={() => onChange({ language: 'tr' })}
        >
          <span>Türkçe</span>
          {currentLang === 'tr' && <Icon name="check" size={15} />}
        </button>
      </div>
    </div>

    <div className="appearance-section">
      <h3>{t('modal.theme')}</h3>
      <div className="tone-options">
        {appearance.tones.map(tone => {
          const toneLabel = tone === 'Açık' ? t('tone.light') : (tone === 'Siyah' ? t('tone.dark') : (tone === 'Grafit' ? t('tone.graphite') : tone));
          return <button key={tone} className={`tone-option ${appearance.tone === tone ? 'selected' : ''}`} onClick={() => onChange({ tone })}><span className={`tone-preview ${tone.toLowerCase()}`}><i /><i /><i /></span><span>{toneLabel}</span>{appearance.tone === tone && <Icon name="check" size={15} />}</button>;
        })}
      </div>
    </div>

    <div className="appearance-section">
      <h3>{t('modal.accent')}</h3>
      <div className="accent-options">
        {appearance.accents.map(accent => {
          const accentLabel = accent.name === 'Mavi' ? t('accent.blue') : (accent.name === 'Yeşil' ? t('accent.green') : (accent.name === 'Kırmızı' ? t('accent.red') : accent.name));
          return <button key={accent.name} className={`accent-option ${appearance.accent === accent.name ? 'selected' : ''}`} title={accentLabel} aria-label={accentLabel} style={{ '--swatch': accent.hex } as React.CSSProperties} onClick={() => onChange({ accent: accent.name })}>{appearance.accent === accent.name && <Icon name="check" size={14} />}</button>;
        })}
      </div>
    </div>

    <div className="setting-row">
      <div className="setting-copy">
        <strong>{t('modal.glow')}</strong>
        <p>{t('modal.glowDesc')}</p>
      </div>
      <Toggle checked={appearance.glow} label={t('modal.glow')} onChange={glow => onChange({ glow })} />
    </div>

    {dataDirectory && <div className="appearance-section">
      <h3>{t('modal.storage')}</h3>
      <p style={{ overflowWrap: 'anywhere' }}>{dataDirectory}</p>
      <button className="secondary-button" onClick={onOpenData}>{t('modal.openDataFolder')}</button>
    </div>}

    <footer>
      <button className="secondary-button" onClick={onOpenLogs}>{t('modal.appLogs')}</button>
      <button className="primary-button" onClick={onClose}>{t('modal.done')}</button>
    </footer>
  </section></div>;
}

