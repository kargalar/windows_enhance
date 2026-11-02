import { useEffect, useRef, useState } from 'react';
import type { Setting } from './types';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { t } from './localization';

export function NumericSettingControl({ setting, describedBy, onUpdate }: {
  setting: Setting;
  describedBy?: string;
  onUpdate: (key: string, value: unknown) => void;
}) {
  const [number, setNumber] = useAutoSaveDraft(Number(setting.value ?? setting.defaultValue ?? 0), value => onUpdate(setting.key, value));
  const [exact, setExact] = useState(String(number));
  const editing = useRef(false);
  useEffect(() => { if (!editing.current) setExact(String(number)); }, [number]);
  const parsed = Number(exact);
  const valid = exact.trim() !== '' && Number.isSafeInteger(parsed) && parsed >= setting.min && parsed <= setting.max;
  const commitExact = () => {
    editing.current = false;
    const next = exact.trim() && Number.isFinite(parsed) ? Math.min(setting.max, Math.max(setting.min, Math.round(parsed))) : number;
    setExact(String(next));
    setNumber(next, true);
  };
  return <div className="numeric-control">
    <input className="range-input" type="range" min={setting.min} max={setting.max} step="1" value={number}
      onChange={event => setNumber(Number(event.target.value))} aria-label={setting.label} aria-describedby={describedBy} />
    <input className="white-control number-input" type="number" min={setting.min} max={setting.max} step="1" value={exact}
      aria-label={t('details.numberInput', undefined, { label: setting.label })} aria-describedby={describedBy} aria-invalid={!valid}
      title={t('details.valueRange', undefined, { min: setting.min, max: setting.max })}
      onFocus={() => { editing.current = true; }} onBlur={commitExact}
      onKeyDown={event => { if (event.key === 'Enter') event.currentTarget.blur(); }}
      onChange={event => {
        const text = event.target.value;
        setExact(text);
        const value = Number(text);
        if (text.trim() && Number.isSafeInteger(value) && value >= setting.min && value <= setting.max) setNumber(value);
      }} />
  </div>;
}
