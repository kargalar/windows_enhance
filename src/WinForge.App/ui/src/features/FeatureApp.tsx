import { useEffect, useRef, useState } from 'react';
import './features.css';

type NetSpeedState = {
  download: string;
  upload: string;
  fontSize: number;
  showDownload: boolean;
  showUpload: boolean;
};

type SpectrumState = {
  levels: number[];
  visible: boolean;
  showBaseline: boolean;
};

type OsdState = {
  kind: 'toast' | 'brightness' | 'volume';
  message?: string;
  deviceName?: string;
  percent?: number;
  isMuted?: boolean;
  icon?: string;
};

type MediaFlyoutState = {
  title: string;
  artist: string;
  artwork?: string;
  sourceName: string;
  sourceType: string;
  sourceBadge: string;
  upNext?: string;
  sessions: { id: string; label: string }[];
  selectedSessionId: string;
  showSessionSelector: boolean;
  position: number;
  duration: number;
  currentTime: string;
  totalTime: string;
  isPlaying: boolean;
  canPlay: boolean;
  canPrevious: boolean;
  canNext: boolean;
  canShuffle: boolean;
  canRepeat: boolean;
  shuffleEnabled: boolean;
  repeatMode: string;
  pinned: boolean;
};

type LockKeysState = { key: string; enabled: boolean };
type TaskbarWidgetState = { title: string; player: string; isPlaying: boolean; canPrevious: boolean; canNext: boolean; sessions: { id: string; label: string }[]; selectedSessionId: string; showSessionSelector: boolean; pinnedSessionId: string; scrollMode: string };
type LauncherState = { query: string; selectedIndex: number; focusGeneration: number; results: { name: string; path: string; isCalculation: boolean; isRecent: boolean }[] };
type CaptureToolsState = { title: string };
type CaptureHistoryState = { title: string; kind: string; search: string; app: string; tag: string; from: string; until: string; offset: number; status: string; selectedId?: number; selectedText: string; selectedTags: string; preview?: string; mediaType: string; confirmDelete: boolean; confirmDeleteRange: boolean; confirmDeleteAll: boolean; rows: { id: number; label: string; app: string; title: string; created: string; icon?: string; hasMedia: boolean; mediaType: string; tags: string }[] };
type UsageState = { from: string; until: string; status: string; summary: string; applications: { app: string; seconds: number; duration: string; estimated: boolean }[]; days: { date: string; active: number; idle: number }[]; dayApplications: { date: string; app: string; duration: string; estimated: boolean }[]; contexts: { date: string; type: string; app: string; context: string; duration: string }[]; tags: { tag: string; seconds: number; duration: string; applications: number }[] };
type ImportState = { title: string; kind: string; root: string; status: string; progress: number; busy: boolean; selectedDatabase: string; sources: { database: string; imagesDirectory: string }[]; preview?: { records: number; existingRecords: number; bytes: number; missingFiles: number; destination: string } };
type ModelsState = { directory: string; status: string; models: { name: string; installed: boolean }[] };
type AdvancedPasteState = { options: { name: string; action: string }[]; selectedIndex: number; jpegQuality: number; jpegVisible: boolean; sourceText: string; sourceLength: number; hasImage: boolean; imageWidth: number; imageHeight: number; preview: string; status: string; canCopy: boolean; canPaste: boolean; pasteLabel: string };
type SuggestionsState = { suggestions: string[] };
type ShortcutGuideState = { query: string; entries: { category: string; keys: string; description: string }[] };
type EnvironmentVariablesState = { title: string; description: string; status: string; rows: { name: string; value: string; expandable: boolean }[] };
type EnvironmentProfilesState = { profiles: { id: string; name: string }[]; selectedId: string; profileName: string; newProfileName: string; enabled: boolean; enabledAllowed: boolean; status: string; confirmDelete: boolean; variables: { name: string; value: string; expandable: boolean }[] };
type FileLocksmithState = { path: string; status: string; selectedProcessId?: number; canScan: boolean; canTerminate: boolean; confirmTerminate: boolean; processes: { processId: number; processName: string; applicationName: string; status: string; canTerminate: boolean }[] };
type MouseJumpState = { selected: number; monitors: { index: number; name: string; isPrimary: boolean; width: number; height: number; preview?: string }[] };
type PowerRenameState = { find: string; replace: string; regex: boolean; caseSensitive: boolean; status: string; canApply: boolean; canUndo: boolean; rows: { originalName: string; newName: string; status: string }[] };
type ImageResizerState = { preset: string; customSize: string; format: string; quality: string; preserveMetadata: boolean; processing: boolean; progress: number; progressMaximum: number; canResize: boolean; status: string; rows: { index: number; fileName: string; sourceSize: string; targetName: string; targetSize: string; status: string }[] };
type WorkspacesState = { name: string; selectedId: string; status: string; busy: boolean; presets: { id: string; name: string; savedAt: string; windowCount: number; applications: string }[] };

const previewNetSpeed: NetSpeedState = {
  download: 'â†“ 12.4 MB/s',
  upload: 'â†‘ 860 KB/s',
  fontSize: 11,
  showDownload: true,
  showUpload: true,
};

const previewSpectrum: SpectrumState = {
  levels: Array.from({ length: 24 }, (_, index) => 0.18 + Math.abs(Math.sin(index * 0.63)) * 0.75),
  visible: true,
  showBaseline: true,
};

const previewOsd: OsdState = {
  kind: 'volume',
  percent: 64,
  isMuted: false,
  icon: 'medium',
};

const previewMedia: MediaFlyoutState = {
  title: 'Geceye DoÄŸru',
  artist: 'Ã–rnek SanatÃ§Ä±',
  sourceName: 'Spotify',
  sourceType: 'Spotify',
  sourceBadge: 'UygulamayÄ± aÃ§',
  sessions: [],
  selectedSessionId: '',
  showSessionSelector: false,
  position: 92,
  duration: 218,
  currentTime: '1:32',
  totalTime: '3:38',
  isPlaying: true,
  canPlay: true,
  canPrevious: true,
  canNext: true,
  canShuffle: true,
  canRepeat: true,
  shuffleEnabled: false,
  repeatMode: 'off',
  pinned: false,
};

export function FeatureApp({ view }: { view: string }) {
  const [state, setState] = useState<Record<string, unknown> | null>(null);
  const [seekPosition, setSeekPosition] = useState<number | null>(null);
  const [widgetMenuOpen, setWidgetMenuOpen] = useState(false);
  const [historyDraft, setHistoryDraft] = useState<{ search: string; app: string; tag: string; from: string; until: string } | null>(null);
  const [historyTagsDraft, setHistoryTagsDraft] = useState<string | null>(null);
  const [historyPreviewOpen, setHistoryPreviewOpen] = useState(false);
  const [statisticsTab, setStatisticsTab] = useState('applications');
  const launcherInput = useRef<HTMLInputElement>(null);
  const launcherState = state as unknown as LauncherState | null;

  useEffect(() => {
    if (view === 'launcher' && launcherState) {
      launcherInput.current?.focus();
      launcherInput.current?.select();
    }
  }, [view, launcherState?.focusGeneration]);

  useEffect(() => {
    const bridge = window.chrome?.webview;
    if (!bridge) {
      if (import.meta.env.DEV) {
        const preview = view === 'net-speed-overlay'
          ? previewNetSpeed
          : view === 'taskbar-audio-visualizer'
            ? previewSpectrum
            : view === 'media-flyout'
              ? previewMedia
            : previewOsd;
        setState(preview);
      }
      return;
    }

    const receive = (event: MessageEvent) => {
      const message = event.data as { type: string; payload?: Record<string, unknown> };
      if (message.type === 'feature.state' && message.payload) setState(message.payload);
    };

    bridge.addEventListener('message', receive);
    bridge.postMessage({ command: 'feature.ready', payload: {} });
    return () => bridge.removeEventListener('message', receive);
  }, []);

  const send = (command: string, payload: Record<string, unknown> = {}) =>
    window.chrome?.webview?.postMessage({ command, payload });

  if (view === 'autocorrect-suggestions') {
    const suggestionState = state as unknown as SuggestionsState | null;
    return <div className="feature-root feature-root-transparent">
      <div className="autocorrect-suggestions">{suggestionState?.suggestions.map((suggestion, index) => <button key={`${suggestion}-${index}`} onClick={() => send('autocorrect.accept', { suggestion })}>{suggestion}</button>)}</div>
    </div>;
  }

  if (view === 'shortcut-guide') {
    const guide = state as unknown as ShortcutGuideState | null;
    return <div className="feature-root feature-root-shortcuts">
      {guide && <section className="shortcut-guide-panel"><header><div><strong>Kısayol rehberi</strong><small>Windows ve WinForge kısayollarını arayın</small></div><button onClick={() => send('shortcut-guide.close')} aria-label="Kapat">×</button></header>
        <input value={guide.query} onChange={event => send('shortcut-guide.search', { query: event.target.value })} onKeyDown={event => { if (event.key === 'Escape') send('shortcut-guide.close'); }} placeholder="Kısayol, kategori veya işlev ara…" aria-label="Kısayol ara" />
        <div className="shortcut-guide-list">{guide.entries.map((entry, index) => <article key={`${entry.keys}-${index}`}><span>{entry.category}</span><kbd>{entry.keys}</kbd><p>{entry.description}</p></article>)}{guide.entries.length === 0 && <div className="history-empty">Eşleşen kısayol bulunamadı.</div>}</div>
      </section>}
    </div>;
  }

  if (view === 'environment-variables') {
    const environment = state as unknown as EnvironmentVariablesState | null;
    return <div className="feature-root feature-root-environment">
      {environment && <section className="environment-panel"><header><div><strong>{environment.title}</strong><small>{environment.description}</small></div><button onClick={() => send('environment.close')} aria-label="Kapat">×</button></header>
        <div className="environment-table"><div className="environment-table-heading"><span>Değişken</span><span>Değer</span><span>%…% genişlet</span><span /></div>
          {environment.rows.map((row, index) => <div className="environment-row" key={index}><input aria-label="Değişken adı" value={row.name} onChange={event => send('environment.update', { index, name: event.target.value })} /><input aria-label="Değişken değeri" value={row.value} onChange={event => send('environment.update', { index, value: event.target.value })} /><input aria-label="Genişletilebilir değer" type="checkbox" checked={row.expandable} onChange={event => send('environment.update', { index, expandable: event.target.checked })} /><button onClick={() => send('environment.remove', { index })} aria-label="Satırı sil">Sil</button></div>)}
          {environment.rows.length === 0 && <div className="history-empty">Ortam değişkeni bulunamadı.</div>}
        </div>
        <footer><span>{environment.status}</span><button onClick={() => send('environment.add')}>Değişken ekle</button><button onClick={() => send('environment.reload')}>Yenile</button><button className="primary" onClick={() => send('environment.save')}>Kaydet</button></footer>
      </section>}
    </div>;
  }

  if (view === 'environment-profiles') {
    const profiles = state as unknown as EnvironmentProfilesState | null;
    return <div className="feature-root feature-root-environment">
      {profiles && <section className="environment-panel profile-panel"><header><div><strong>Ortam değişkeni profilleri</strong><small>Profiller kullanıcı değişkenlerini birlikte uygular. Aynı anda bir profil etkin olabilir; devre dışı bırakıldığında önceki değerler geri yüklenir.</small></div><button onClick={() => send('profiles.close')} aria-label="Kapat">×</button></header>
        <div className="profile-toolbar"><select value={profiles.selectedId} onChange={event => send('profiles.select', { id: event.target.value })}><option value="">Profil seçin…</option>{profiles.profiles.map(profile => <option key={profile.id} value={profile.id}>{profile.name}</option>)}</select><input value={profiles.profileName} onChange={event => send('profiles.name', { name: event.target.value })} placeholder="Seçili profilin adı" /><input value={profiles.newProfileName} onChange={event => send('profiles.new-name', { name: event.target.value })} placeholder="Yeni profil adı" /><button onClick={() => send('profiles.new', { name: profiles.newProfileName })}>Yeni</button><button disabled={!profiles.selectedId} onClick={() => send('profiles.delete')}>Profili sil</button></div>
        <label className="profile-enabled"><input type="checkbox" checked={profiles.enabled} disabled={!profiles.enabledAllowed} onChange={event => send('profiles.toggle', { enabled: event.target.checked })} /> Bu profili kullanıcı ortam değişkenlerine uygula</label>
        <div className="environment-table"><div className="environment-table-heading"><span>Değişken</span><span>Değer</span><span>%…% genişlet</span><span /></div>{profiles.variables.map((variable, index) => <div className="environment-row" key={index}><input value={variable.name} onChange={event => send('profiles.variable-update', { index, name: event.target.value })} aria-label="Değişken adı" /><input value={variable.value} onChange={event => send('profiles.variable-update', { index, value: event.target.value })} aria-label="Değişken değeri" /><input type="checkbox" checked={variable.expandable} onChange={event => send('profiles.variable-update', { index, expandable: event.target.checked })} aria-label="Genişletilebilir" /><button onClick={() => send('profiles.variable-remove', { index })}>Sil</button></div>)}{profiles.variables.length === 0 && <div className="history-empty">Bu profilde değişken yok.</div>}</div>
        <footer><span>{profiles.status}</span><button onClick={() => send('profiles.variable-add')}>Değişken ekle</button><button className="primary" disabled={!profiles.selectedId} onClick={() => send('profiles.save')}>Kaydet</button></footer>
        {profiles.confirmDelete && <div className="history-confirm-backdrop"><section className="history-confirm"><strong>Profili sil?</strong><p>Etkin profildeki değişkenler önceki değerlerine döndürülür.</p><div><button onClick={() => send('profiles.delete-confirm', { confirm: false })}>Vazgeç</button><button className="danger" onClick={() => send('profiles.delete-confirm', { confirm: true })}>Sil</button></div></section></div>}
      </section>}
    </div>;
  }

  if (view === 'file-locksmith') {
    const locksmith = state as unknown as FileLocksmithState | null;
    return <div className="feature-root feature-root-locksmith"><section className="locksmith-panel">
      {locksmith && <><header><div><strong>Dosya kilidi bulucu</strong><small>Dosyayı kullanan uygulama ve hizmetleri salt okunur olarak tara</small></div><button onClick={() => send('file-locksmith.close')} aria-label="Kapat">×</button></header>
        <div className="locksmith-path"><code title={locksmith.path}>{locksmith.path || 'Bir dosya veya klasör seçin'}</code><button onClick={() => send('file-locksmith.choose')}>Dosya seç</button><button disabled={!locksmith.canScan} onClick={() => send('file-locksmith.scan')}>Yeniden tara</button></div>
        <div className="locksmith-list"><div className="locksmith-list-heading"><span>İşlem</span><span>Uygulama / hizmet</span><span>PID</span><span>Durum</span></div>{locksmith.processes.map(process => <button key={process.processId} className={`locksmith-row ${process.processId === locksmith.selectedProcessId ? 'selected' : ''}`} onClick={() => send('file-locksmith.select', { processId: process.processId })}><span>{process.processName}</span><span>{process.applicationName}</span><span>{process.processId}</span><span>{process.status}</span></button>)}{locksmith.processes.length === 0 && <div className="history-empty">Tarama sonucu henüz yok.</div>}</div>
        <footer><span>{locksmith.status}</span><button disabled={!locksmith.canTerminate} className="danger" onClick={() => send('file-locksmith.terminate')}>Uygulamayı sonlandır…</button></footer>
        {locksmith.confirmTerminate && <div className="history-confirm-backdrop"><section className="history-confirm"><strong>Uygulama sonlandırılsın mı?</strong><p>Kaydedilmemiş çalışmalar kaybolabilir. Yalnızca seçilen uygulama işlemi sonlandırılır.</p><div><button onClick={() => send('file-locksmith.terminate', { confirm: false })}>Vazgeç</button><button className="danger" onClick={() => send('file-locksmith.terminate', { confirm: true })}>Sonlandır</button></div></section></div>}
      </>}
    </section></div>;
  }

  if (view === 'mouse-jump') {
    const mouseJump = state as unknown as MouseJumpState | null;
    const chooseCenter = (index: number) => send('mouse-jump.center', { index });
    const handleMouseJumpKey = (event: React.KeyboardEvent) => {
      if (event.key === 'Escape') send('mouse-jump.cancel');
      else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') send('mouse-jump.move', { delta: -1 });
      else if (event.key === 'ArrowRight' || event.key === 'ArrowDown') send('mouse-jump.move', { delta: 1 });
      else if (event.key === 'Home' && mouseJump) send('mouse-jump.select', { index: 0 });
      else if (event.key === 'End' && mouseJump) send('mouse-jump.select', { index: mouseJump.monitors.length - 1 });
      else if (event.key === 'Enter' && mouseJump) chooseCenter(mouseJump.selected);
      else if (event.key.toLowerCase() === 'p' && mouseJump) chooseCenter(mouseJump.monitors.find(monitor => monitor.isPrimary)?.index ?? 0);
      else if (/^[1-9]$/.test(event.key) && mouseJump) chooseCenter(Number(event.key) - 1);
      else return;
      event.preventDefault();
    };
    return <div className="feature-root feature-root-mouse-jump" tabIndex={0} autoFocus onKeyDown={handleMouseJumpKey}>
      {mouseJump && <section className="mouse-jump-panel"><header><div><strong>Ekranlar arası fare taşıma</strong><small>Noktaya tıklayarak imleci o konuma taşıyın</small></div><button onClick={() => send('mouse-jump.cancel')} aria-label="Kapat">×</button></header>
        <div className="mouse-jump-cards">{mouseJump.monitors.map(monitor => <article key={monitor.index} className={`mouse-jump-card ${mouseJump.selected === monitor.index ? 'selected' : ''}`} onMouseEnter={() => send('mouse-jump.select', { index: monitor.index })}>
          <div className="mouse-jump-card-title"><strong>{monitor.name}</strong><small>{monitor.width} × {monitor.height}{monitor.isPrimary ? ' · Birincil' : ''}</small></div>
          <div className="mouse-jump-preview" onClick={event => {
            if (!monitor.preview) { chooseCenter(monitor.index); return; }
            const image = event.currentTarget.querySelector('img');
            if (!image) return;
            const bounds = event.currentTarget.getBoundingClientRect();
            const scale = Math.min(1, bounds.width / image.naturalWidth, bounds.height / image.naturalHeight);
            const displayedWidth = image.naturalWidth * scale;
            const displayedHeight = image.naturalHeight * scale;
            const x = Math.max(0, Math.min(1, (event.clientX - bounds.left - (bounds.width - displayedWidth) / 2) / displayedWidth));
            const y = Math.max(0, Math.min(1, (event.clientY - bounds.top - (bounds.height - displayedHeight) / 2) / displayedHeight));
            send('mouse-jump.choose', { index: monitor.index, x, y });
          }} title="Bu noktaya taşı">
            {monitor.preview ? <img src={monitor.preview} alt={`${monitor.name} önizlemesi`} draggable={false} /> : <span>Önizleme yok<br />Ekran merkezine taşı</span>}
            <span className="mouse-jump-crosshair" aria-hidden="true">+</span>
          </div>
          <button onClick={() => chooseCenter(monitor.index)}>Merkeze taşı</button>
        </article>)}</div>
        <footer>← → ekran seç · Enter merkez · 1–9 ekran · P birincil · Esc kapat</footer>
      </section>}
    </div>;
  }

  if (view === 'power-rename') {
    const rename = state as unknown as PowerRenameState | null;
    return <div className="feature-root feature-root-power-rename">{rename && <section className="power-rename-panel">
      <header><div><strong>Toplu yeniden adlandırma</strong><small>Dosya adlarını önizleyin, çakışmaları giderin ve işlemi geri alın.</small></div><button onClick={() => send('power-rename.close')} aria-label="Kapat">×</button></header>
      <div className="power-rename-toolbar"><button onClick={() => send('power-rename.choose')}>Dosyaları seç</button><label>Ara<input value={rename.find} onChange={event => send('power-rename.update', { find: event.target.value })} /></label><label>Değiştir<input value={rename.replace} onChange={event => send('power-rename.update', { replace: event.target.value })} /></label></div>
      <div className="power-rename-options"><label><input type="checkbox" checked={rename.regex} onChange={event => send('power-rename.update', { regex: event.target.checked })} /> Düzenli ifade (regex)</label><label><input type="checkbox" checked={rename.caseSensitive} onChange={event => send('power-rename.update', { caseSensitive: event.target.checked })} /> Büyük/küçük harfe duyarlı</label></div>
      <div className="power-rename-table"><div className="power-rename-table-heading"><span>Mevcut ad</span><span>Yeni ad</span><span>Durum</span></div>{rename.rows.map((row, index) => <div className="power-rename-row" key={`${row.originalName}-${index}`}><span title={row.originalName}>{row.originalName}</span><span title={row.newName}>{row.newName}</span><span className={row.status.startsWith('Çakışma') || row.status.startsWith('Geçersiz') ? 'error' : ''}>{row.status}</span></div>)}{rename.rows.length === 0 && <div className="history-empty">Dosya seçerek ad değiştirme önizlemesini oluşturun.</div>}</div>
      <footer><span role="status">{rename.status}</span><div><button disabled={!rename.canUndo} onClick={() => send('power-rename.undo')}>Geri al</button><button className="primary" disabled={!rename.canApply} onClick={() => send('power-rename.apply')}>Yeniden adlandır</button></div></footer>
    </section>}</div>;
  }

  if (view === 'image-resizer') {
    const resizer = state as unknown as ImageResizerState | null;
    return <div className="feature-root feature-root-image-resizer">{resizer && <section className="image-resizer-panel">
      <header><div><strong>Görsel boyutlandırıcı</strong><small>Toplu yeniden boyutlandırma · özgün görseller korunur</small></div><button onClick={() => send('image-resizer.close')} aria-label="Kapat">×</button></header>
      <div className="image-resizer-toolbar"><button disabled={resizer.processing} onClick={() => send('image-resizer.choose')}>Görselleri seç</button><label>Uzun kenar<select disabled={resizer.processing} value={resizer.preset} onChange={event => send('image-resizer.options', { preset: event.target.value })}><option value="small">Küçük · 854 px</option><option value="medium">Orta · 1280 px</option><option value="large">Büyük · 1920 px</option><option value="custom">Özel</option><option value="source">Kaynağı koru</option></select></label>{resizer.preset === 'custom' && <label>Boyut (px)<input disabled={resizer.processing} inputMode="numeric" value={resizer.customSize} onChange={event => send('image-resizer.options', { customSize: event.target.value })} /></label>}<label>Biçim<select disabled={resizer.processing} value={resizer.format} onChange={event => send('image-resizer.options', { format: event.target.value })}><option value="source">Kaynak biçimi</option><option value="png">PNG</option><option value="jpeg">JPEG</option></select></label><label>JPEG kalitesi<input disabled={resizer.processing} inputMode="numeric" value={resizer.quality} onChange={event => send('image-resizer.options', { quality: event.target.value })} /></label></div>
      <label className="image-resizer-metadata"><input type="checkbox" disabled={resizer.processing} checked={resizer.preserveMetadata} onChange={event => send('image-resizer.options', { preserveMetadata: event.target.checked })} /> EXIF/IPTC metadatasını koru</label>
      <div className="image-resizer-table"><div className="image-resizer-table-heading"><span>Dosya</span><span>Özgün boyut</span><span>Çıktı</span><span>Boyut</span><span>Durum</span><span /></div>{resizer.rows.map(row => <div className="image-resizer-row" key={`${row.fileName}-${row.index}`}><span title={row.fileName}>{row.fileName}</span><span>{row.sourceSize}</span><span title={row.targetName}>{row.targetName}</span><span>{row.targetSize}</span><span>{row.status}</span><button disabled={resizer.processing} onClick={() => send('image-resizer.remove', { index: row.index })} aria-label={`${row.fileName} listesinden kaldır`}>×</button></div>)}{resizer.rows.length === 0 && <div className="history-empty">Boyutlandırmak için görsel ekleyin.</div>}</div>
      <footer><div><div className="image-resizer-progress"><span style={{ width: `${resizer.progressMaximum ? Math.min(100, 100 * resizer.progress / resizer.progressMaximum) : 0}%` }} /></div><span role="status">{resizer.status}</span></div><div>{resizer.processing && <button onClick={() => send('image-resizer.cancel')}>İptal et</button>}<button className="primary" disabled={!resizer.canResize} onClick={() => send('image-resizer.run')}>Boyutlandır</button></div></footer>
    </section>}</div>;
  }

  if (view === 'workspaces') {
    const workspaces = state as unknown as WorkspacesState | null;
    return <div className="feature-root feature-root-workspaces">{workspaces && <section className="workspaces-panel">
      <header><div><strong>Çalışma alanları</strong><small>Açık masaüstü uygulamalarını ve pencere konumlarını kaydedip birlikte geri yükleyin.</small></div><button onClick={() => send('workspaces.close')} aria-label="Kapat">×</button></header>
      <section className="workspaces-capture"><label>Çalışma alanı adı<input maxLength={64} value={workspaces.name} onChange={event => send('workspaces.name', { name: event.target.value })} placeholder="Örn. Kodlama, Toplantı" /></label><button className="primary" disabled={workspaces.busy} onClick={() => send('workspaces.capture')}>Açık pencereleri kaydet</button></section>
      <div className="workspaces-list">{workspaces.presets.map(preset => <button key={preset.id} className={`workspace-card ${workspaces.selectedId === preset.id ? 'selected' : ''}`} onClick={() => send('workspaces.select', { id: preset.id })}><span className="workspace-card-icon">▦</span><span><strong>{preset.name}</strong><small>{preset.windowCount} pencere · {preset.applications || 'Uygulama bilgisi yok'}</small><small>{preset.savedAt}</small></span></button>)}{workspaces.presets.length === 0 && <div className="history-empty">Henüz kaydedilmiş çalışma alanı yok. Açık uygulama pencerelerini kaydedin.</div>}</div>
      <footer><span role="status">{workspaces.status}</span><div>{workspaces.busy && <button onClick={() => send('workspaces.cancel')}>Durdur</button>}<button disabled={workspaces.busy || !workspaces.selectedId} onClick={() => send('workspaces.delete')}>Sil</button><button className="primary" disabled={workspaces.busy || !workspaces.selectedId} onClick={() => send('workspaces.restore')}>Çalışma alanını aç</button></div></footer>
    </section>}</div>;
  }

  if (view === 'capture-tools') {
    const tools = [
      ['region', 'Bölge yakala', 'Belirli bir alanı seç'], ['window', 'Pencere yakala', 'Tek bir pencereyi seç'],
      ['monitor', 'Monitör yakala', 'Ekran seç'], ['desktop', 'Tüm ekranlar', 'Bağlı ekranları birlikte al'],
      ['last', 'Son bölge', 'En son seçilen alanı kullan'], ['scroll', 'Kayan ekran', 'Uzun sayfayı kaydırarak al'],
      ['video', 'Video kaydı', 'Ekranı video olarak kaydet'], ['gif', 'GIF kaydı', 'Ekranı animasyonlu GIF kaydet'],
      ['annotate-text', 'Ekrana yazı yaz', 'Ekran üzerine metin ekle'], ['annotate-shape', 'Şekil çiz', 'Ekran üzerine şekil ekle'],
      ['annotate-freehand', 'Serbest çiz', 'Serbest çizim yap'], ['multi-region', 'Çoklu bölge yakala', 'Birden fazla alan seç']
    ];
    return <div className="feature-root feature-root-capture">
      <section className="capture-tools-panel">
        <header><span className="capture-mark">⌗</span><div><strong>{(state as unknown as CaptureToolsState | null)?.title ?? 'Ekran Yakalama'}</strong><small>Bir yakalama türü seçin</small></div><button onClick={() => send('capture-tools.close')} aria-label="Kapat">×</button></header>
        <div className="capture-tool-grid">{tools.map(([mode, title, description], index) => <button key={mode} className="capture-tool" onClick={() => send('capture-tools.run', { mode })}>
          <span>{['▣', '▱', '▤', '▦', '↺', '↕', '◉', '▧', 'T', '◇', '✎', '▥'][index]}</span><strong>{title}</strong><small>{description}</small>
        </button>)}</div>
        <footer><span>Win + Alt + S bölge yakala</span><span>Gelişmiş seçenekler modül ayarlarında</span></footer>
      </section>
    </div>;
  }

  if (view === 'capture-history') {
    const history = state as unknown as CaptureHistoryState | null;
    const filters = historyDraft ?? { search: history?.search ?? '', app: history?.app ?? '', tag: history?.tag ?? '', from: history?.from ?? '', until: history?.until ?? '' };
    return <div className="feature-root feature-root-history">
      {history && <section className="history-panel">
        <header className="history-header"><div><strong>{history.title}</strong><small>{history.kind === 'recall' ? 'Kullanım ve etkinlik geçmişi' : 'Yakalamalarınız bu cihazda saklanır'}</small></div><span>{history.status}</span></header>
        <form className="history-filters" onSubmit={event => { event.preventDefault(); send('history.filter', filters); }}>
          <input placeholder="Metin / OCR" value={filters.search} onChange={event => setHistoryDraft({ ...filters, search: event.target.value })} />
          <input placeholder="Uygulama" value={filters.app} onChange={event => setHistoryDraft({ ...filters, app: event.target.value })} />
          <input placeholder="Etiket" value={filters.tag} onChange={event => setHistoryDraft({ ...filters, tag: event.target.value })} />
          <input placeholder="Başlangıç gg.aa.yyyy" value={filters.from} onChange={event => setHistoryDraft({ ...filters, from: event.target.value })} />
          <input placeholder="Bitiş gg.aa.yyyy" value={filters.until} onChange={event => setHistoryDraft({ ...filters, until: event.target.value })} />
          <button type="submit">Ara</button>
        </form>
        <div className="history-content">
          <div className="history-list">{history.rows.map(row => <button key={row.id} className={`history-row ${row.id === history.selectedId ? 'selected' : ''}`} onClick={() => { setHistoryTagsDraft(null); send('history.select', { id: row.id }); }}>
            {row.icon ? <img src={row.icon} alt="" /> : <span className="history-icon">▧</span>}<span><strong>{row.title || row.label.split('\n')[1] || row.app}</strong><small>{row.created} · {row.app}</small><small>{row.tags}</small></span>
          </button>)}{history.rows.length === 0 && <div className="history-empty">Bu filtrelerle kayıt bulunamadı.</div>}</div>
          <article className="history-detail">
            {history.preview ? <><button className="history-preview-button" onClick={() => setHistoryPreviewOpen(true)} title="Büyük önizleme"><img className="history-preview" src={history.preview} alt="Seçili kayıt önizlemesi" /><span>Büyüt</span></button>{history.kind === 'recall' && <input className="history-selected-tags" value={historyTagsDraft ?? history.selectedTags} onChange={event => setHistoryTagsDraft(event.target.value)} placeholder="Seçili uygulama etiketleri" />}</> : <div className="history-preview-empty">{history.mediaType ? `${history.mediaType.replace('.', '').toUpperCase()} kaydı` : 'Bir kayıt seçin'}</div>}
            <textarea readOnly value={history.selectedText} aria-label="Kayıt metni" />
            {history.kind === 'recall' && <div className="history-tags"><button onClick={() => { send('history.save-tags', { tags: historyTagsDraft ?? history.selectedTags }); setHistoryTagsDraft(null); }}>Etiketleri kaydet</button></div>}
          </article>
        </div>
        <footer className="history-actions">
          <button onClick={() => send('history.page', { delta: -200 })}>Önceki</button><button onClick={() => send('history.page', { delta: 200 })}>Sonraki</button>
          <button onClick={() => send('history.copy')}>Metni kopyala</button><button onClick={() => send('history.open-media')}>Medyayı aç</button>
          <button onClick={() => send('history.delete-selected')}>Seçiliyi sil</button><button onClick={() => send('history.delete-range')}>Tarih aralığını sil</button>
          <button onClick={() => send('history.delete-all')}>Tüm geçmişi sil</button>
          {history.kind === 'recall' && <><button onClick={() => send('history.usage')}>Kullanım istatistikleri</button><button onClick={() => send('history.retry-ocr')}>OCR yeniden dene</button></>}
        </footer>
        {(history.confirmDelete || history.confirmDeleteAll) && <div className="history-confirm-backdrop"><section className="history-confirm"><strong>Geçmiş kaydı silinsin mi?</strong><p>{history.confirmDeleteAll ? 'Bu modülün bütün geçmişi kalıcı olarak silinecek.' : history.confirmDeleteRange ? 'Seçilen tarih aralığındaki kayıtlar kalıcı olarak silinecek.' : 'Seçilen kayıt kalıcı olarak silinecek.'}</p><div><button onClick={() => send('history.delete-confirm', { confirm: false, range: history.confirmDeleteRange })}>Vazgeç</button><button className="danger" onClick={() => send(history.confirmDeleteAll ? 'history.delete-all' : 'history.delete-confirm', { confirm: true, range: history.confirmDeleteRange })}>Sil</button></div></section></div>}
        {historyPreviewOpen && history.preview && <div className="history-image-backdrop" onClick={() => setHistoryPreviewOpen(false)}><button aria-label="Önizlemeyi kapat">×</button><img src={history.preview} alt="Tam ekran yakalama önizlemesi" onClick={event => event.stopPropagation()} /></div>}
      </section>}
    </div>;
  }

  if (view === 'capture-usage-statistics') {
    const stats = state as unknown as UsageState | null;
    const maxAppSeconds = Math.max(1, ...(stats?.applications.map(row => row.seconds) ?? []));
    const maxTagSeconds = Math.max(1, ...(stats?.tags.map(row => row.seconds) ?? []));
    const maxDaySeconds = Math.max(1, ...(stats?.days.map(row => row.active + row.idle) ?? []));
    return <div className="feature-root feature-root-statistics">
      {stats && <section className="statistics-panel">
        <header><div><strong>Kullanım istatistikleri</strong><small>Yerel etkinlik kayıtlarından hesaplanır</small></div><button onClick={() => send('statistics.close')} aria-label="Kapat">×</button></header>
        <form className="statistics-date-filter" onSubmit={event => { event.preventDefault(); const form = new FormData(event.currentTarget); send('statistics.refresh', { from: form.get('from'), until: form.get('until') }); }}>
          <label>Başlangıç<input name="from" defaultValue={stats.from} placeholder="gg.aa.yyyy" /></label><label>Bitiş<input name="until" defaultValue={stats.until} placeholder="gg.aa.yyyy" /></label><button>Yenile</button>
        </form>
        <nav className="statistics-tabs">{[['applications', 'Uygulamalar'], ['days', 'Günlük kullanım'], ['contexts', 'Web ve klasörler'], ['tags', 'Etiketler']].map(([id, label]) => <button key={id} className={statisticsTab === id ? 'active' : ''} onClick={() => setStatisticsTab(id)}>{label}</button>)}</nav>
        {statisticsTab === 'applications' && <div className="statistics-view"><div className="statistics-summary">{stats.summary}</div><div className="statistics-bars">{stats.applications.map(row => <div className="statistics-bar-row" key={row.app}><span>{row.app}</span><div><i style={{ width: `${row.seconds / maxAppSeconds * 100}%` }} /></div><small>{row.duration}</small></div>)}</div><div className="statistics-table">{stats.applications.map(row => <div key={row.app}><span>{row.app}</span><span>{row.duration}</span><span>{row.estimated ? 'Tahmini' : 'Kesin'}</span></div>)}</div></div>}
        {statisticsTab === 'days' && <div className="statistics-view"><div className="statistics-day-chart">{stats.days.map(row => <div key={row.date} title={`${row.date}: etkin ${Math.round(row.active / 60)} dk, boşta ${Math.round(row.idle / 60)} dk`}><span style={{ height: `${row.active / maxDaySeconds * 100}%` }} /><i style={{ height: `${row.idle / maxDaySeconds * 100}%` }} /><small>{row.date.slice(0, 5)}</small></div>)}</div><div className="statistics-table">{stats.dayApplications.map((row, index) => <div key={`${row.date}-${row.app}-${index}`}><span>{row.date}</span><span>{row.app}</span><span>{row.duration}</span></div>)}</div></div>}
        {statisticsTab === 'contexts' && <div className="statistics-view"><div className="statistics-table">{stats.contexts.map((row, index) => <div key={`${row.date}-${row.app}-${row.context}-${index}`}><span>{row.date}</span><span>{row.type === 'website' ? 'Web sitesi' : 'Klasör'}</span><span>{row.app}</span><span>{row.context}</span><span>{row.duration}</span></div>)}</div></div>}
        {statisticsTab === 'tags' && <div className="statistics-view"><div className="statistics-bars">{stats.tags.map(row => <div className="statistics-bar-row" key={row.tag}><span>{row.tag}</span><div><i style={{ width: `${row.seconds / maxTagSeconds * 100}%` }} /></div><small>{row.duration}</small></div>)}</div><div className="statistics-table">{stats.tags.map(row => <div key={row.tag}><span>{row.tag}</span><span>{row.duration}</span><span>{row.applications} uygulama</span></div>)}</div></div>}
        <footer>{stats.status}</footer>
      </section>}
    </div>;
  }

  if (view === 'capture-import') {
    const importer = state as unknown as ImportState | null;
    const formatBytes = (bytes: number) => bytes >= 1024 ** 3 ? `${(bytes / 1024 ** 3).toFixed(2)} GB` : `${(bytes / 1024 ** 2).toFixed(1)} MB`;
    return <div className="feature-root feature-root-import">
      {importer && <section className="import-panel">
        <header><div><strong>{importer.title}</strong><small>Kaynak veriler silinmez veya değiştirilmez</small></div><button onClick={() => send('import.close')} aria-label="Kapat">×</button></header>
        <form className="import-source-path" onSubmit={event => { event.preventDefault(); const form = new FormData(event.currentTarget); send('import.discover', { root: form.get('root') }); }}>
          <label>Kaynak uygulama klasörü<input name="root" defaultValue={importer.root} /></label><button>Kaynakları tara</button>
        </form>
        <label className="import-source-select">Geçmiş veritabanı<select value={importer.selectedDatabase} onChange={event => send('import.select', { database: event.target.value })}>
          <option value="">Kaynak seçin…</option>{importer.sources.map(source => <option key={source.database} value={source.database}>{source.database}{source.imagesDirectory ? ` · ${source.imagesDirectory}` : ''}</option>)}
        </select></label>
        {importer.preview && <div className="import-preview-grid"><div><small>Kayıt</small><strong>{importer.preview.records.toLocaleString('tr-TR')}</strong></div><div><small>Zaten aktarılmış</small><strong>{importer.preview.existingRecords.toLocaleString('tr-TR')}</strong></div><div><small>Ek alan</small><strong>{formatBytes(importer.preview.bytes)}</strong></div><div><small>Eksik dosya</small><strong>{importer.preview.missingFiles.toLocaleString('tr-TR')}</strong></div><p>Hedef: {importer.preview.destination}</p></div>}
        <div className="import-status"><span>{importer.status}</span><div><i style={{ width: `${Math.max(0, Math.min(100, importer.progress * 100))}%` }} /></div></div>
        <footer><button disabled={importer.busy || !importer.preview} onClick={() => send('import.start')}>Önizlenen kayıtları aktar</button><button disabled={!importer.busy} onClick={() => send('import.cancel')}>İptal</button></footer>
      </section>}
    </div>;
  }

  if (view === 'capture-models') {
    const models = state as unknown as ModelsState | null;
    return <div className="feature-root feature-root-models">
      {models && <section className="models-panel"><header><div><strong>Whisper modelleri</strong><small>Modeller cihaz üzerinde çalışır ve yerel olarak saklanır</small></div><button onClick={() => send('models.close')} aria-label="Kapat">×</button></header>
        <p className="models-path">Model klasörü: {models.directory}</p>
        <div className="models-list">{models.models.map(model => <article key={model.name}><span className={model.installed ? 'installed' : ''}>{model.installed ? '✓' : '↓'}</span><div><strong>{model.name}</strong><small>{model.installed ? 'Hazır' : 'Yüklü değil'}</small></div><button onClick={() => send('models.add', { name: model.name })}>{model.installed ? 'Değiştir' : 'Model ekle'}</button></article>)}</div>
        <footer>{models.status || 'GGML model dosyalarını ekleyerek yerel dikteyi kullanın.'}</footer>
      </section>}
    </div>;
  }

  if (view === 'advanced-paste') {
    const paste = state as unknown as AdvancedPasteState | null;
    return <div className="feature-root feature-root-paste">
      {paste && <section className="paste-panel">
        <header><span className="paste-mark">⇧</span><div><strong>Gelişmiş yapıştır</strong><small>Pano içeriğini dönüştür ve gönder</small></div><button onClick={() => send('paste.cancel')} aria-label="Kapat">×</button></header>
        <div className="paste-source"><small>GİRDİ ÖNİZLEMESİ · {paste.sourceLength.toLocaleString('tr-TR')} KARAKTER</small><p>{paste.sourceText || (paste.hasImage ? `Panoda görsel · ${paste.imageWidth} × ${paste.imageHeight} piksel` : 'Pano metni boş')}</p></div>
        <label className="paste-format">Biçim<select value={paste.selectedIndex} onChange={event => send('paste.select', { index: Number(event.target.value) })}>{paste.options.map((option, index) => <option key={option.action} value={index}>{option.name}</option>)}</select></label>
        {paste.jpegVisible && <label className="paste-quality">JPEG kalite · {paste.jpegQuality}%<input type="range" min="10" max="100" step="5" value={paste.jpegQuality} onChange={event => send('paste.quality', { quality: Number(event.target.value) })} /></label>}
        <textarea className="paste-preview" readOnly value={paste.preview} placeholder="Dönüştürülmüş önizleme" />
        <footer><span>{paste.status}</span><button disabled={!paste.canCopy} onClick={() => send('paste.copy')}>Panoya kopyala</button><button disabled={!paste.canPaste} onClick={() => send('paste.paste')}>{paste.pasteLabel}</button></footer>
      </section>}
    </div>;
  }

  if (view === 'launcher') {
    const launcher = launcherState;
    const handleLauncherKey = (event: React.KeyboardEvent) => {
      if (event.key === 'Escape') { send('launcher.dismiss'); event.preventDefault(); }
      else if (event.key === 'Enter') { send('launcher.activate', { index: launcher?.selectedIndex ?? 0 }); event.preventDefault(); }
      else if (event.key === 'ArrowDown') { send('launcher.move', { delta: 1 }); event.preventDefault(); }
      else if (event.key === 'ArrowUp') { send('launcher.move', { delta: -1 }); event.preventDefault(); }
    };
    return <div className="feature-root feature-root-launcher">
      <section className="launcher-panel">
        <div className="launcher-heading"><span className="launcher-mark">W</span><span>WinForge <small>HIZLI BAŞLATICI</small></span><kbd>ESC</kbd></div>
        <input ref={launcherInput} className="launcher-search" value={launcher?.query ?? ''} onChange={event => send('launcher.search', { query: event.target.value })} onKeyDown={handleLauncherKey} placeholder="Uygulama, dosya veya komut ara…" aria-label="Arama" />
        <div className="launcher-results" role="listbox" aria-label="Arama sonuçları">
          {(launcher?.results ?? []).map((result, index) => <button key={`${result.path}-${index}`} className={`launcher-result ${launcher?.selectedIndex === index ? 'selected' : ''}`} onMouseEnter={() => send('launcher.select', { index })} onClick={() => send('launcher.activate', { index })} onKeyDown={handleLauncherKey} role="option" aria-selected={launcher?.selectedIndex === index}>
            <span className="launcher-result-icon">{result.isCalculation ? '=' : result.isRecent ? '◷' : result.path.startsWith('ms-settings:') ? '⚙' : '◈'}</span><span><strong>{result.name}</strong><small>{result.path}</small></span><kbd>↵</kbd>
          </button>)}
          {launcher && launcher.results.length === 0 && <div className="launcher-empty">Sonuç bulunamadı</div>}
        </div>
        <footer className="launcher-footer"><span>↑ ↓ seç</span><span>Enter aç veya kopyala</span><span>= hesapla</span></footer>
      </section>
    </div>;
  }

  if (view === 'taskbar-media-widget') {
    const widget = state as unknown as TaskbarWidgetState | null;
    return <div className="feature-root feature-root-transparent">
      {widget && <div className="taskbar-media-widget" onDoubleClick={() => send('widget.open-flyout')}>
        <button className="widget-track" onClick={() => send('widget.open-flyout')} title={widget.title}><span className="widget-player-mark">♫</span><span><small>{widget.player}</small><strong>{widget.title}</strong></span></button>
        <div className="widget-controls">
          {widget.showSessionSelector && <button onClick={() => send('widget.switch-session')} title="Sonraki medya oturumu">⇄</button>}
          <button disabled={!widget.canPrevious} onClick={() => send('widget.previous')} aria-label="Önceki">‹</button>
          <button className="widget-play" onClick={() => send('widget.toggle')} aria-label={widget.isPlaying ? 'Duraklat' : 'Oynat'}>{widget.isPlaying ? 'Ⅱ' : '▶'}</button>
          <button disabled={!widget.canNext} onClick={() => send('widget.next')} aria-label="Sonraki">›</button>
          <button onClick={() => setWidgetMenuOpen(open => !open)} aria-label="Diğer seçenekler">···</button>
        </div>
        {widgetMenuOpen && <div className="widget-menu">
          {widget.sessions.map(session => <button key={session.id} className={session.id === widget.pinnedSessionId ? 'selected' : ''} onClick={() => { send('widget.select-session', { id: session.id }); setWidgetMenuOpen(false); }}>{session.label}</button>)}
          <button onClick={() => { send('widget.select-session', { id: '' }); setWidgetMenuOpen(false); }}>Otomatik oturum seç</button>
          <button onClick={() => send('widget.open-flyout')}>Medya denetimlerini aç</button>
          <button onClick={() => send('widget.open-sound-settings')}>Windows ses ayarları</button>
          <button onClick={() => send('widget.open-player')}>Oynatıcıya geç</button>
        </div>}
      </div>}
    </div>;
  }

  if (view === 'lock-keys-status') {
    const lockKeys = state as unknown as LockKeysState | null;
    return <div className="feature-root feature-root-transparent">
      {lockKeys && <div className="lock-keys-card"><span className={`lock-keys-indicator ${lockKeys.enabled ? 'enabled' : ''}`}>{lockKeys.enabled ? '●' : '○'}</span><div><strong>{lockKeys.key}</strong><span>{lockKeys.enabled ? 'Açık' : 'Kapalı'}</span></div></div>}
    </div>;
  }

  if (view === 'net-speed-overlay') {
    const network = state as unknown as NetSpeedState | null;
    return <div className={`feature-root feature-root-transparent ${import.meta.env.DEV ? 'feature-root-preview' : ''}`}>
      {network && (network.showDownload || network.showUpload) && <div className="network-speed-pill" style={{ fontSize: network.fontSize }}>
        {network.showDownload && <span className="network-speed-value download-value">{network.download}</span>}
        {network.showDownload && network.showUpload && <span className="network-speed-divider" />}
        {network.showUpload && <span className="network-speed-value upload-value">{network.upload}</span>}
      </div>}
    </div>;
  }

  if (view === 'taskbar-audio-visualizer') {
    const spectrum = state as unknown as SpectrumState | null;
    return <div className={`feature-root feature-root-transparent ${import.meta.env.DEV ? 'feature-root-preview' : ''}`}>
      {spectrum && <div className={`spectrum-visualizer ${spectrum.visible ? '' : 'is-hidden'}`}>
        {spectrum.showBaseline && <div className="spectrum-baseline" />}
        <div className="spectrum-bars">{spectrum.levels.map((level, index) => <span key={index} style={{ height: `${Math.max(2, Math.min(22, level * 20))}px` }} />)}</div>
      </div>}
    </div>;
  }

  if (view === 'system-osd') {
    const osd = state as unknown as OsdState | null;
    if (!osd) return <div className="feature-root feature-root-transparent" />;
    if (osd.kind === 'toast') return <div className="feature-root feature-root-osd">
      <div className="osd-card osd-toast"><span className="osd-status-dot" /><span>{osd.message}</span></div>
    </div>;
    if (osd.kind === 'brightness') return <div className="feature-root feature-root-osd">
      <div className="osd-card osd-brightness">
        <div className="osd-heading"><span>Ekran parlaklÄ±ÄŸÄ±</span><strong>{osd.percent}%</strong></div>
        {osd.deviceName && <span className="osd-device">{osd.deviceName}</span>}
        <div className="osd-progress"><span style={{ width: `${osd.percent}%` }} /></div>
      </div>
    </div>;
    if (osd.kind === 'volume') return <div className="feature-root feature-root-osd">
      <div className="osd-card osd-volume" onMouseEnter={() => send('volume.hover', { value: true })} onMouseLeave={() => send('volume.hover', { value: false })}>
        <button className="osd-volume-button" onClick={() => send('volume.toggle-mute')} aria-label={osd.isMuted ? 'Sesi aÃ§' : 'Sessize al'}>
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M4 9v6h4l5 4V5L8 9H4Z" />
            {osd.isMuted ? <path d="m17 9 4 6m0-6-4 6" /> : <path d="M16 9.5a4 4 0 0 1 0 5" />}
          </svg>
        </button>
        <input className="osd-volume-range" type="range" min="0" max="100" value={osd.percent} onChange={event => send('volume.set', { percent: Number(event.target.value) })} onKeyDown={event => { if (event.key === 'Escape') send('volume.dismiss'); }} aria-label="Ses dÃ¼zeyi" />
        <strong className="osd-volume-value">{osd.percent}%</strong>
      </div>
    </div>;
  }

  if (view === 'media-flyout') {
    const media = state as unknown as MediaFlyoutState | null;
    if (!media) return <div className="feature-root feature-root-transparent" />;
    return <div className="feature-root feature-root-media">
      <article className="media-flyout-card" onMouseEnter={() => send('media.hover', { value: true })} onMouseLeave={() => send('media.hover', { value: false })}>
        {media.upNext && <div className="media-up-next"><span>SONRAKÄ°</span>{media.upNext}</div>}
        <header className="media-source-row">
          <button className={`media-source ${media.sourceType.toLowerCase()}`} onClick={() => send('media.open-source')}>
            <span className="media-source-icon">{media.sourceType === 'Spotify' ? 'S' : media.sourceType === 'YouTube' ? 'â–¶' : 'â™«'}</span>
            <span>{media.sourceName || 'Medya'}</span><small>{media.sourceBadge}</small>
          </button>
          <button className={`media-pin ${media.pinned ? 'active' : ''}`} onClick={() => send('media.pin', { value: !media.pinned })} aria-label={media.pinned ? 'Panel sabitlemesini kaldÄ±r' : 'Paneli sabitle'} title={media.pinned ? 'Sabitlemeyi kaldÄ±r' : 'Sabitle'}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m16 3 5 5-4 1-3 4 2 2-1 1-4-4-6 8 1-10-4-4 1-1 2 2 4-3 1-4Z" /></svg>
          </button>
        </header>
        <div className="media-track-row">
          <div className="media-artwork">{media.artwork ? <img src={media.artwork} alt="AlbÃ¼m kapaÄŸÄ±" /> : <span>â™«</span>}</div>
          <div className="media-track-copy"><strong title={media.title}>{media.title}</strong><span title={media.artist}>{media.artist || 'Bilinmeyen sanatÃ§Ä±'}</span></div>
        </div>
        {media.showSessionSelector && <select className="media-session-select" value={media.selectedSessionId} onChange={event => send('media.select-session', { id: event.target.value })} aria-label="Medya oturumu seÃ§">
          {media.sessions.map(session => <option key={session.id} value={session.id}>{session.label}</option>)}
        </select>}
        <div className="media-timeline">
          <input type="range" min="0" max={Math.max(1, media.duration)} value={Math.min(seekPosition ?? media.position, media.duration || 1)} onChange={event => setSeekPosition(Number(event.target.value))} onPointerUp={() => { if (seekPosition !== null) send('media.seek', { position: seekPosition }); setSeekPosition(null); }} onKeyUp={event => { if (event.key.startsWith('Arrow') && seekPosition !== null) { send('media.seek', { position: seekPosition }); setSeekPosition(null); } }} aria-label="ParÃ§ada ilerle" />
          <div><span>{media.currentTime}</span><span>{media.totalTime}</span></div>
        </div>
        <footer className="media-controls">
          <button className={`media-option ${media.shuffleEnabled ? 'active' : ''}`} disabled={!media.canShuffle} onClick={() => send('media.shuffle')} aria-label="KarÄ±ÅŸtÄ±r" title="KarÄ±ÅŸtÄ±r">â¤¨</button>
          <button className="media-skip" disabled={!media.canPrevious} onClick={() => send('media.previous')} aria-label="Ã–nceki parÃ§a" title="Ã–nceki parÃ§a"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 5h2v14H6zm3 7 10-7v14z" /></svg></button>
          <button className="media-play" disabled={!media.canPlay} onClick={() => send('media.toggle')} aria-label={media.isPlaying ? 'Duraklat' : 'Oynat'} title={media.isPlaying ? 'Duraklat' : 'Oynat'}>
            {media.isPlaying ? <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5h4v14H7zm6 0h4v14h-4z" /></svg> : <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m7 4 13 8-13 8z" /></svg>}
          </button>
          <button className="media-skip" disabled={!media.canNext} onClick={() => send('media.next')} aria-label="Sonraki parÃ§a" title="Sonraki parÃ§a"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 5h2v14h-2zm-1 7L5 5v14z" /></svg></button>
          <button className={`media-option ${media.repeatMode !== 'off' ? 'active' : ''}`} disabled={!media.canRepeat} onClick={() => send('media.repeat')} aria-label={`Tekrar ${media.repeatMode}`} title={`Tekrar ${media.repeatMode}`}>{media.repeatMode === 'track' ? 'â†»Â¹' : 'â†»'}</button>
        </footer>
      </article>
    </div>;
  }

  return <div className="feature-root feature-root-error">GÃ¶rÃ¼nÃ¼m bulunamadÄ±</div>;
}
