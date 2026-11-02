export function Toggle({ checked, label, onChange, describedBy }: {
  checked: boolean;
  label: string;
  onChange: (value: boolean) => void;
  describedBy?: string;
}) {
  return <button type="button" role="switch" aria-checked={checked} aria-label={label}
    aria-describedby={describedBy} className={`toggle ${checked ? 'on' : ''}`} onClick={() => onChange(!checked)}><span /></button>;
}
