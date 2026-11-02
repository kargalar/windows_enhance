import type { CSSProperties } from 'react';
import type { Module } from './types';
import { Icon } from './Icon';
import { t } from './localization';

// A schematic, not a screenshot: only values we can represent faithfully are used.
export function TaskbarPreview({ module }: { module: Module }) {
  const value = (key: string) => { const setting = module.settings.find(s => s.key === key); return setting?.value ?? setting?.defaultValue; };
  const custom = value('custom-background') === true;
  const color = String(value('background-color') ?? '#e7edf6');
  const background = custom && /^#[0-9a-f]{6}$/i.test(color) ? color : undefined;
  const opacity = custom ? Math.min(1, Math.max(0, Number(value('background-opacity') ?? 100) / 100)) : 1;
  const size = Math.min(32, Math.max(16, Number(value('app-icon-size')) || 24));
  const designEnabled = value('design-enabled') !== false;
  return <figure className="module-preview"><figcaption><strong>{t('details.preview')}</strong><span>{t('details.previewHint')}</span></figcaption>
    <div className="preview-desktop" aria-hidden="true"><div className="preview-wallpaper" />
      <div className="preview-taskbar"><div className="preview-taskbar-background" style={{ background, opacity } as CSSProperties} />
        <div className="preview-apps" style={{ '--preview-icon-size': `${size}px` } as CSSProperties}>
          {value('hide-start') !== true && <span className="preview-start"><Icon name="grid" size={size} /></span>}
          <Icon name="search" size={size} /><Icon name="folder" size={size} /><Icon name="window" size={size} />
        </div><div className="preview-tray"><Icon name="volume" size={17} /><span>12:00</span></div>
      </div>
    </div>{!designEnabled && <p>{t('details.previewUnavailable')}</p>}
  </figure>;
}
