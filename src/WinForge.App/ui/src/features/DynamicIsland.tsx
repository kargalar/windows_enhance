import { useRef, useState } from 'react';
import './dynamic-island.css';

export type IslandState = {
  expanded: boolean; pinned: boolean; theme: string; reducedMotion: boolean; scale: number;
  time: string; date: string; year: number; month: number; day: number;
  title: string; artist: string; artwork: string; playing: boolean; position: number; duration: number;
  canPlay: boolean; canPrevious: boolean; canNext: boolean;
  battery: number; charging: boolean; volume: number; muted: boolean; volumeEnabled: boolean;
  clipboard: string; clipboardEnabled: boolean; alert: string; alertKind: string; focusRemaining: number; focusMinutes: number;
  files: { path: string; name: string }[]; downloads: { path: string; name: string }[]; downloadsEnabled: boolean;
};

export const previewIsland: IslandState = {
  expanded: true, pinned: false, theme: 'obsidian', reducedMotion: false, scale: 1,
  time: '10:42', date: 'Pazar, 1 Kasım', year: 2026, month: 11, day: 1,
  title: '', artist: '', artwork: '', playing: false, position: 0, duration: 0,
  canPlay: false, canPrevious: false, canNext: false, battery: 78, charging: true,
  volume: 42, muted: false, volumeEnabled: true, clipboard: '', clipboardEnabled: false,
  alert: '', alertKind: '', focusRemaining: 0, focusMinutes: 25,
  files: [], downloads: [], downloadsEnabled: true,
};

type Tab = 'media' | 'calendar' | 'focus' | 'clipboard' | 'files' | 'downloads' | 'system';
const tabs: { id: Tab; label: string; icon: string }[] = [
  { id: 'media', label: 'Medya', icon: '♫' }, { id: 'calendar', label: 'Takvim', icon: '▦' },
  { id: 'focus', label: 'Odak', icon: '◴' }, { id: 'clipboard', label: 'Pano', icon: '▤' },
  { id: 'files', label: 'Dosya', icon: '▱' }, { id: 'downloads', label: 'İndir', icon: '↓' }, { id: 'system', label: 'Sistem', icon: '◈' },
];
const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
const durationText = (seconds: number) => `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;

export function IslandGlyph({ name }: { name: 'play' | 'pause' | 'next' | 'previous' | 'pin' | 'close' }) {
  return <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
    {name === 'play' && <path d="m9 5 10 7-10 7Z" fill="currentColor" stroke="none" />}
    {name === 'pause' && <><path d="M8 5v14M16 5v14" strokeWidth="4" /></>}
    {name === 'next' && <><path d="m5 5 10 7-10 7Z" fill="currentColor" stroke="none" /><path d="M18 5v14" /></>}
    {name === 'previous' && <><path d="m19 5-10 7 10 7Z" fill="currentColor" stroke="none" /><path d="M6 5v14" /></>}
    {name === 'pin' && <><path d="m9 3 6 0-1 6 4 4H6l4-4-1-6ZM12 13v8" /></>}
    {name === 'close' && <path d="m7 7 10 10M17 7 7 17" />}
  </svg>;
}

function Calendar({ state }: { state: IslandState }) {
  const firstDay = (new Date(state.year, state.month - 1, 1).getDay() + 6) % 7;
  const days = new Date(state.year, state.month, 0).getDate();
  const label = new Date(state.year, state.month - 1).toLocaleDateString('tr-TR', { month: 'long', year: 'numeric' });
  return <div className="island-calendar"><strong>{label}</strong><div className="island-calendar-grid">
    {['Pt', 'Sa', 'Ça', 'Pe', 'Cu', 'Ct', 'Pa'].map(day => <small key={day}>{day}</small>)}
    {Array.from({ length: firstDay }, (_, index) => <span key={`blank-${index}`} />)}
    {Array.from({ length: days }, (_, index) => <span key={index} className={index + 1 === state.day ? 'today' : ''} aria-current={index + 1 === state.day ? 'date' : undefined}>{index + 1}</span>)}
  </div></div>;
}

export function DynamicIsland({ state }: { state: IslandState | null }) {
  const [tab, setTab] = useState<Tab>('media');
  const [seek, setSeek] = useState<number | null>(null);
  const wheelTime = useRef(0);
  const native = Boolean(window.chrome?.webview);
  const [previewExpanded, setPreviewExpanded] = useState(true);
  const [previewPinned, setPreviewPinned] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  if (!state) return <div className="feature-root feature-root-island" />;
  const expanded = native ? state.expanded : previewExpanded;
  const pinned = native ? state.pinned : previewPinned;
  const expand = () => native ? send('island.expand') : setPreviewExpanded(value => !value);
  const setPanel = (index: number) => setTab(tabs[(index + tabs.length) % tabs.length].id);
  return <div className={`feature-root feature-root-island ${state.reducedMotion ? 'island-no-motion' : ''}`}>
    <div ref={root} tabIndex={0} role="region" aria-label="Dynamic Island" className={`dynamic-island island-${state.theme} ${expanded ? 'expanded' : 'collapsed'}`}
      style={{ '--island-scale': state.scale } as React.CSSProperties}
      onMouseEnter={() => send('island.hover', { value: true })} onMouseLeave={() => send('island.hover', { value: false })}
      onKeyDown={event => { if ((event.target as Element).closest('input,textarea,select')) return; if (event.key === 'Escape' && expanded) { event.preventDefault(); expand(); } if (event.key === 'ArrowRight') setPanel(tabs.findIndex(item => item.id === tab) + 1); if (event.key === 'ArrowLeft') setPanel(tabs.findIndex(item => item.id === tab) - 1); }}
      onWheel={event => { if (!expanded || Math.abs(event.deltaY) < 10 || event.timeStamp - wheelTime.current < 250 || (event.target as Element).closest('input,.island-clipboard')) return; wheelTime.current = event.timeStamp; setPanel(tabs.findIndex(item => item.id === tab) + Math.sign(event.deltaY)); }}>
      <div className="island-summary">
        <button className="island-summary-main" aria-label={expanded ? 'Adayı küçült' : 'Adayı genişlet'} aria-expanded={expanded} onClick={expand}>
          <span className={`island-orb ${state.playing ? 'playing' : ''}`}>{state.artwork ? <img src={state.artwork} alt="" /> : <span className="island-orbit" />}</span>
          <span className="island-summary-copy"><strong>{state.alert || (state.focusRemaining > 0 ? `Odak · ${durationText(state.focusRemaining)}` : state.title || state.time)}</strong>
            {expanded && <small>{state.alert ? state.alertKind === 'media' ? state.artist : 'Şimdi' : state.title ? state.artist : state.date}</small>}</span>
        </button>
        {!expanded && <span className={`island-wave ${state.playing ? 'playing' : ''}`} aria-hidden="true">{[0, 1, 2, 3, 4].map(index => <i key={index} style={{ '--bar': index } as React.CSSProperties} />)}</span>}
        {expanded && <div className="island-window-actions"><button aria-label={pinned ? 'Sabitlemeyi kaldır' : 'Adayı açık tut'} aria-pressed={pinned} className={pinned ? 'selected' : ''} onClick={() => native ? send('island.pin') : setPreviewPinned(value => !value)}><IslandGlyph name="pin" /></button><button aria-label="Adayı gizle (Ctrl+Alt+D ile geri aç)" onClick={() => send('island.hide')}><IslandGlyph name="close" /></button></div>}
      </div>
      {state.alertKind === 'volume' && <div className="island-volume-alert" role="meter" aria-label="Ses düzeyi" aria-valuenow={state.muted ? 0 : state.volume} aria-valuemin={0} aria-valuemax={100}><i style={{ width: `${state.muted ? 0 : state.volume}%` }} /></div>}
      {expanded && <div className="island-expanded-body">
        <nav className="island-tabs" aria-label="Ada panelleri">{tabs.map(item => <button key={item.id} aria-current={tab === item.id ? 'page' : undefined} className={tab === item.id ? 'selected' : ''} onClick={() => setTab(item.id)}><span aria-hidden="true">{item.icon}</span>{item.label}</button>)}</nav>
        <div className="island-panel" key={tab}>
          {tab === 'media' && (state.title ? <div className="island-media"><div className="island-track"><div className="island-cover">{state.artwork ? <img src={state.artwork} alt="Albüm kapağı" /> : '♫'}</div><div><strong>{state.title}</strong><p>{state.artist || 'Bilinmeyen sanatçı'}</p><small>{state.playing ? 'Şimdi çalıyor' : 'Duraklatıldı'}</small></div></div>
            <label className="island-seek"><span className="sr-only">Parça konumu</span><input type="range" min={0} max={Math.max(1, state.duration)} step={1} value={seek ?? state.position} disabled={state.duration <= 0} onChange={event => setSeek(Number(event.target.value))} onPointerUp={event => { send('island.seek', { seconds: Number(event.currentTarget.value) }); setSeek(null); }} onKeyUp={event => { if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) { send('island.seek', { seconds: Number(event.currentTarget.value) }); setSeek(null); } }} onPointerCancel={() => setSeek(null)} /><span>{durationText(seek ?? state.position)}</span><span>{durationText(state.duration)}</span></label>
            <div className="island-playback"><button aria-label="Önceki parça" disabled={!state.canPrevious} onClick={() => send('island.previous')}><IslandGlyph name="previous" /></button><button className="island-play" aria-label={state.playing ? 'Duraklat' : 'Oynat'} disabled={!state.canPlay} onClick={() => send('island.play')}><IslandGlyph name={state.playing ? 'pause' : 'play'} /></button><button aria-label="Sonraki parça" disabled={!state.canNext} onClick={() => send('island.next')}><IslandGlyph name="next" /></button></div>
          </div> : <div className="island-empty"><span className="island-empty-icon">♫</span><strong>Biraz müzik iyi gelir.</strong><p>Spotify, tarayıcı veya bir medya uygulamasında oynatmayı başlat.</p></div>)}
          {tab === 'calendar' && <Calendar state={state} />}
          {tab === 'focus' && <div className="island-focus"><div className="island-focus-ring" style={{ '--progress': `${state.focusRemaining > 0 ? (1 - state.focusRemaining / (state.focusMinutes * 60)) * 360 : 0}deg` } as React.CSSProperties}><strong>{durationText(state.focusRemaining || state.focusMinutes * 60)}</strong><small>{state.focusRemaining ? 'Kendine alan aç.' : 'Tek bir şeye odaklan.'}</small></div><button className="island-primary" onClick={() => send('island.focus')}>{state.focusRemaining ? 'Sayacı durdur' : 'Odaklanmaya başla'}</button></div>}
          {tab === 'clipboard' && <div className="island-clipboard">{state.clipboard ? <><p>{state.clipboard}</p><button onClick={() => send('island.clipboard.clear')}>Ada önizlemesini temizle</button></> : <div className="island-empty"><span className="island-empty-icon">▤</span><strong>{state.clipboardEnabled ? 'Panon burada görünecek.' : 'Pano önizlemesi kapalı.'}</strong><p>{state.clipboardEnabled ? 'Yeni kopyaladığın metnin son 500 karaktere kadar önizlemesi.' : 'Modül ayarlarından isteğe bağlı olarak açabilirsin.'}</p></div>}</div>}
          {(tab === 'files' || tab === 'downloads') && <div className="island-files">
            {(tab === 'files' ? state.files : state.downloads).length > 0 ? (tab === 'files' ? state.files : state.downloads).map(file => <button key={file.path} title={`${file.path} · Gezgin’de göster`} onClick={() => send('island.files.reveal', { path: file.path })}><span aria-hidden="true">▱</span><strong>{file.name}</strong><span aria-hidden="true">↗</span></button>) : <div className="island-empty"><span className="island-empty-icon">{tab === 'files' ? '▱' : '↓'}</span><strong>{tab === 'files' ? 'Dosyaların elinin altında.' : state.downloadsEnabled ? 'Yeni dosyalar burada.' : 'İndirme izleme kapalı.'}</strong><p>{tab === 'files' ? 'Dosyalarını bu oturum için tepsiye ekle; tek tıkla Gezgin’de bul.' : 'İndirilenler klasörüne gelen yeni dosyalar gösterilir.'}</p></div>}
            {tab === 'files' && <div className="island-file-actions"><button className="island-primary" onClick={() => send('island.files.add')}>Dosya ekle</button>{state.files.length > 0 && <button onClick={() => send('island.files.clear')}>Tepsiyi temizle</button>}</div>}
          </div>}
          {tab === 'system' && <div className="island-system"><div className="island-system-clock"><strong>{state.time}</strong><span>{state.date}</span></div><div className="island-system-metrics"><div><small>PİL</small><strong>{state.battery >= 0 ? `%${state.battery}` : 'AC'}</strong><span>{state.battery < 0 ? 'Güç bağlantısı' : state.charging ? 'Şarj oluyor' : 'Pil kullanılıyor'}</span></div><div><small>SES</small><strong>{state.volumeEnabled ? state.muted ? 'Kapalı' : `%${state.volume}` : '—'}</strong><span>{state.volumeEnabled ? 'Sistem çıkışı' : 'İzleme kapalı'}</span></div></div>{state.volumeEnabled && <div className="island-volume-controls"><button aria-label="Sesi azalt" onClick={() => send('island.volume', { step: -5 })}>−</button><button onClick={() => send('island.mute')}>{state.muted ? 'Sesi aç' : 'Sessize al'}</button><button aria-label="Sesi artır" onClick={() => send('island.volume', { step: 5 })}>+</button></div>}</div>}
        </div>
        <footer className="island-footer"><span className="island-status-dot" /><span>{state.focusRemaining > 0 ? `${durationText(state.focusRemaining)} · odak sürüyor` : 'WinForge Island'}</span><span>{pinned ? 'Sabitlendi' : 'Tekerlekle keşfet'}</span></footer>
      </div>}
      <div className="sr-only" role="status" aria-live="polite">{state.alert}</div>
    </div>
  </div>;
}
