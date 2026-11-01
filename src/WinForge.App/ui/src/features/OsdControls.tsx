import { useEffect, useRef, useState } from 'react';

type Props = {
  kind: 'volume' | 'brightness';
  percent: number;
  isMuted?: boolean;
  send: (command: string, payload?: Record<string, unknown>) => void;
};

export function OsdControls({ kind, percent, isMuted, send }: Props) {
  const [value, setValue] = useState(percent);
  const dragging = useRef(false);
  useEffect(() => { if (!dragging.current) setValue(percent); }, [percent, kind]);
  const endDrag = () => {
    if (!dragging.current) return;
    dragging.current = false;
    send(`${kind}.drag`, { value: false });
  };

  return <div className="feature-root feature-root-osd">
    <div className="osd-card osd-volume" onPointerEnter={() => send(`${kind}.hover`, { value: true })} onPointerLeave={() => send(`${kind}.hover`, { value: false })} onKeyDown={event => { if (event.key === 'Escape') { endDrag(); send(`${kind}.dismiss`); } }}>
      {kind === 'volume' ? <button type="button" className="osd-volume-button" onClick={() => send('volume.toggle-mute')} aria-label={isMuted ? 'Sesi aç' : 'Sessize al'} aria-pressed={!!isMuted} title={isMuted ? 'Sesi aç' : 'Sessize al'}>
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M4 9v6h4l5 4V5L8 9H4Z" />
          {isMuted ? <path d="m17 9 4 6m0-6-4 6" /> : <path d="M16 9.5a4 4 0 0 1 0 5" />}
        </svg>
      </button> : <span className="osd-volume-button osd-brightness-icon" aria-hidden="true">
        <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round">
          <circle cx="12" cy="12" r="4" /><path d="M12 2v2m0 16v2M2 12h2m16 0h2M4.93 4.93l1.42 1.42m11.3 11.3 1.42 1.42m0-14.14-1.42 1.42M6.35 17.65l-1.42 1.42" />
        </svg>
      </span>}
      <input className="osd-volume-range" type="range" min="0" max="100" step="1" value={value}
        onPointerDown={event => {
          if (event.button !== 0) return;
          dragging.current = true;
          event.currentTarget.setPointerCapture(event.pointerId);
          send(`${kind}.drag`, { value: true });
        }}
        onPointerUp={endDrag} onPointerCancel={endDrag} onLostPointerCapture={endDrag}
        onChange={event => {
          const next = Number(event.target.value);
          setValue(next);
          send(`${kind}.set`, { percent: next });
        }} aria-label={kind === 'volume' ? 'Ses düzeyi' : 'Ekran parlaklığı'} />
      <strong className="osd-volume-value">{value}%</strong>
    </div>
  </div>;
}
