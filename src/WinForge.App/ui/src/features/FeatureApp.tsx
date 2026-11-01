import { useEffect, useRef, useState } from 'react';
import './features.css';
import { OsdControls } from './OsdControls';
import { CaptureEditorView, type CaptureEditorState } from './CaptureEditor';
import { DefaultAppsPanel, DefaultPathsPanel } from './DefaultSettingsPanels';
import { DynamicIsland, previewIsland, type IslandState } from './DynamicIsland';

const islandToolViews = new Set(['nashi', 'nashi-settings', 'nashi-setup', 'nashi-metadata-editor', 'nashi-pdf-organizer', 'clipboard-shelf', 'clipboard-settings', 'clipboard-prompt', 'clipboard-preview', 'nashi-tool-editor', 'nashi-progress', 'nashi-qr-results', 'clipboard-recording-trim']);

type NetSpeedState = {
  download: string;
  upload: string;
  fontSize: number;
  showDownload: boolean;
  showUpload: boolean;
  layout: string;
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
type AppDialogState = { title: string; message: string; confirmLabel: string; cancelLabel: string; isConfirmation: boolean };

type MediaFlyoutState = {
  title: string;
  artist: string;
  artwork?: string;
  sourceName: string;
  sourceType: string;
  sourceBadge: string;
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
type TaskbarWidgetState = { title: string; tooltip?: string; player: string; isPlaying: boolean; canPrevious: boolean; canNext: boolean; sessions: { id: string; label: string }[]; selectedSessionId: string; showSessionSelector: boolean; pinnedSessionId: string; scrollMode: string };
type LauncherState = { query: string; selectedIndex: number; focusGeneration: number; results: { name: string; path: string; isCalculation: boolean; isRecent: boolean }[] };
type CaptureToolsState = { title: string };
type CaptureRecordingState = { gif: boolean; busy: boolean; paused: boolean; status: string };
type CaptureHistoryState = { title: string; kind: string; search: string; app: string; tag: string; from: string; until: string; offset: number; status: string; selectedId?: number; selectedText: string; selectedTags: string; preview?: string; cursor?: { x: number; y: number; width: number; height: number }; mediaType: string; confirmDelete: boolean; confirmDeleteRange: boolean; confirmDeleteAll: boolean; rows: { id: number; label: string; app: string; title: string; created: string; icon?: string; hasMedia: boolean; mediaType: string; tags: string }[] };
type UsageState = { from: string; until: string; status: string; summary: string; applications: { app: string; seconds: number; duration: string; estimated: boolean }[]; days: { date: string; active: number; idle: number }[]; dayApplications: { date: string; app: string; duration: string; estimated: boolean }[]; contexts: { date: string; type: string; app: string; context: string; duration: string }[]; tags: { tag: string; seconds: number; duration: string; applications: number }[] };
type ImportState = { title: string; kind: string; root: string; status: string; progress: number; busy: boolean; selectedDatabase: string; sources: { database: string; imagesDirectory: string }[]; preview?: { records: number; existingRecords: number; bytes: number; missingFiles: number; destination: string } };
type ModelsState = { directory: string; status: string; models: { name: string; installed: boolean }[] };
type AdvancedPasteState = { options: { name: string; action: string }[]; selectedIndex: number; jpegQuality: number; jpegVisible: boolean; sourceText: string; sourceLength: number; hasImage: boolean; imageWidth: number; imageHeight: number; preview: string; status: string; canCopy: boolean; canPaste: boolean; pasteLabel: string };
type SuggestionsState = { suggestions: string[] };
type ShortcutGuideState = { query: string; entries: { category: string; keys: string; description: string }[] };
type EnvironmentVariablesState = { title: string; description: string; status: string; rows: { name: string; value: string; expandable: boolean }[] };
type EnvironmentProfilesState = { profiles: { id: string; name: string }[]; selectedId: string; profileName: string; newProfileName: string; enabled: boolean; enabledAllowed: boolean; status: string; confirmDelete: boolean; variables: { name: string; value: string; expandable: boolean }[] };
type FileLocksmithState = { path: string; status: string; selectedProcessId?: number; canScan: boolean; canTerminate: boolean; confirmTerminate: boolean; processes: { processId: number; processName: string; applicationName: string; status: string; canTerminate: boolean }[] };
type PowerRenameState = { find: string; replace: string; regex: boolean; caseSensitive: boolean; status: string; canApply: boolean; canUndo: boolean; rows: { originalName: string; newName: string; status: string }[] };
type ImageResizerState = { preset: string; customSize: string; format: string; quality: string; preserveMetadata: boolean; processing: boolean; progress: number; progressMaximum: number; canResize: boolean; status: string; rows: { index: number; fileName: string; sourceSize: string; targetName: string; targetSize: string; status: string }[] };
type WorkspacesState = { name: string; selectedId: string; status: string; busy: boolean; presets: { id: string; name: string; savedAt: string; windowCount: number; applications: string }[] };
type HostsState = { filter: string; status: string; dirty: boolean; selectedLine: number | null; canEdit: boolean; confirmDiscard: boolean; confirmRemove: boolean; entry: { address: string; hostNames: string; comment: string; isActive: boolean; lineIndex: number; error: string } | null; rows: { lineIndex: number; address: string; hostNames: string; comment: string; isActive: boolean }[] };
type CaptureImagePreviewState = { image: string; pinned: boolean };
type KeyboardEditorState = { keys: string; shortcuts: string; applications: string; text: string; actions: string; error: string; captureTarget: string | null; captureStatus: string };
type KeyboardEditorField = 'keys' | 'shortcuts' | 'applications' | 'text' | 'actions';
type DisplayControl = { code: number; label: string; type: 'slider' | 'select' | 'info'; value: number | string; options?: { value: number; label: string }[] };
type DisplayBrightnessState = { status: string; linkLevels: boolean; profileName: string; selectedProfileName: string; profiles: string[]; confirmColorPreset: { id: string; value: number; monitorName: string; label?: string } | null; monitors: { id: string; name: string; isInternalPanel: boolean; controls: DisplayControl[] }[] };
type NashiState = {
  page: 'home' | 'library' | 'download'; busy: boolean; status: string;
  files: { path: string; name: string; directory: string }[];
  choices: { id: string; title: string; symbol: string }[]; formats: string[];
  library: { id: string; name: string; action: string; created: string; paths: string[]; available: number; missing: boolean }[];
  recent: { id: string; name: string; action: string; created: string; paths: string[]; available: number; missing: boolean }[];
  query: string; recentQuery: string; downloadUrl: string; downloadKind: string; downloadKinds: string[]; quality: number;
  mediaReady: boolean; officeReady: boolean; setupLabel: string; appearance: string;
};
type NashiProfile = { quality: number; preserveMetadata: boolean; audioBitrate: number; sampleDepth: string; maxVideoHeight: number; gifFrameRate: number; gifWidth: number; compression: string; layout: string; epubLayout: string; dpi: number };
type NashiSetupState = { mediaReady: boolean; officeReady: boolean; ready: boolean; working: boolean; progress: number; indeterminate: boolean; status: string; missing: string[] };
type MetadataEditorState = { media: boolean; removeAll: boolean; fields: { key: string; label: string; value: string }[]; details: { label: string; value: string }[]; canSave: boolean };
type PdfOrganizerState = { uniformWidth: boolean; pages: { id: string; number: number; rotation: number; image: string }[]; selected: string[] };
type FilePreviewState = { file: string; index: number; count: number; kind: 'message' | 'image' | 'text' | 'pdf' | 'video' | 'audio'; message: string; image: string; text: string; mediaUrl: string; pdfPage: number; pdfPageCount: number; canPrevious: boolean; canNext: boolean };
type FloatShelfState = { items: { path: string; name: string; folder: boolean; extension: string; image: string | null }[]; selected: string[]; expanded: boolean; icons: boolean; edge: string | null; notice: string | null; count: number };
type WheelState = { externalDrag: boolean; finish: string; selected: number; center: string; subtitle: string; startAngle: number; choices: { id: string; title: string; symbol: string }[] };
type DictationStatusState = { message: string; result?: string; audioLevel: number; recording: boolean };
type ZonePreviewState = { width: number; height: number; showNumbers: boolean; activeColor: string; inactiveColor: string; borderThickness: number; zones: { index: number; x: number; y: number; width: number; height: number; active: boolean }[]; merged: { x: number; y: number; width: number; height: number } | null };
type CropThumbnailState = { title: string; subtitle: string };
type CropSelectionState = { hint: string };
type TextSelectionState = { hint: string };
type ScreenRulerState = { unit: string };
type ToolEditorOption = { type: 'help' | 'text' | 'password' | 'select' | 'checkbox' | 'button' | 'list' | 'range'; id: string; label: string; value?: string | number | boolean; options?: string[]; items?: string[]; reorderable?: boolean; minimum?: number; maximum?: number };
type ToolEditorState = { title: string; file: string; controls: ToolEditorOption[]; media: boolean; playing: boolean; currentTime: number; duration: number; canPlayRange: boolean; showComparison: boolean; comparisonLabel: string; processingPreview: boolean; playbackStatus: string };
type ProgressState = { label: string; message: string; fraction: number | null; complete: boolean; cancelled: boolean; canReveal: boolean };
type QrResultsState = { results: { index: number; file: string; value: string; canOpen: boolean }[]; status: string };
type ClipboardCard = { id: string; kind: string; title: string; description: string; created: string; tags: string[]; pinned: boolean; saved: boolean; preview: string };
type ClipboardRow = { index: number; title: string; visible: boolean; count: number; page: number; pageCount: number; items: ClipboardCard[] };
type ClipboardAction = { id: number; name: string };
type ClipboardShelfState = {
  libraryMode: boolean; status: string; query: string; searchMode: number; pinnedOnly: boolean; trash: boolean; selectMode: boolean;
  type: number; date: number; domain: string; tagsFilter: string; sort: number; scope: string; selected: string[];
  title: string; content: string; notes: string; itemTags: string; inspectorEnabled: boolean; inspectorContentReadOnly: boolean;
  folders: { id: string; name: string; depth: number; selected: boolean }[]; tagList: { name: string; selected: boolean }[];
  actions: ClipboardAction[]; rows: ClipboardRow[]; items: ClipboardCard[]; page: number; pageCount: number;
  listView: boolean; cardSize: string; showImages: boolean; showTexts: boolean; showLinks: boolean; count: number; selectionCount: number; searchFocusGeneration: number; actionsOpenGeneration: number; folderPickerGeneration: number; folderPickerPin: boolean; deleteConfirmGeneration: number; permanentDeleteCount: number;
};
type ClipboardPreviewState = { file: string; index: number; count: number; kind: 'image' | 'link' | 'recording' | 'text' | 'message'; message: string; image: string; mediaUrl: string; text: string; author: string; zoom: number; editingText: boolean; canPrevious: boolean; canNext: boolean; canEditImage: boolean; canEditText: boolean; canOpenLink: boolean; canTrim: boolean };
type ClipboardSettingsState = {
  captureText: boolean; captureImages: boolean; captureLinks: boolean; fetchLinkMetadata: boolean; indexImages: boolean;
  showImages: boolean; showTexts: boolean; showLinks: boolean; listView: boolean; sounds: boolean;
  imageRetentionDays: number; textRetentionDays: number; linkRetentionDays: number; cardSize: string;
  watchFolders: string[]; shortcuts: string; status: string;
};
type ClipboardPromptState = { title: string; value: string };
type RecordingTrimState = { file: string; mediaUrl: string; duration: number; start: string; end: string; progress: number; status: string; working: boolean; ready: boolean };
type NashiSettingsState = {
  section: string; language: string; appearance: string; enabled: boolean; feedback: boolean; animations: boolean; keepInTray: boolean; revealFinished: boolean; windowManagement: boolean; wheelFinish: string; wheelScale: number;
  toolsModifier: string; convertModifier: string; actionsModifier: string; floatBehavior: string; outputMode: string; outputFolder: string; downloadFolder: string; automaticUpdateChecks: boolean;
  formats: string[]; compatible: Record<string, string[]>; profiles: Record<string, NashiProfile>; selectedFormat: string; about: string;
};

function NashiSettingsView({ state }: { state: NashiSettingsState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const [format, setFormat] = useState(state.selectedFormat);
  const profile = state.profiles[format] ?? { quality: .85, preserveMetadata: true, audioBitrate: 256, sampleDepth: 'matchSource', maxVideoHeight: 0, gifFrameRate: 15, gifWidth: 960, compression: 'balanced', layout: 'editable', epubLayout: 'reflowable', dpi: 144 };
  const [draft, setDraft] = useState<NashiProfile>(profile);
  useEffect(() => { setFormat(state.selectedFormat); }, [state.selectedFormat]);
  useEffect(() => { setDraft(profile); }, [format, state.profiles]);
  const toggle = (key: string, value: boolean) => send('settings.toggle', { key, value });
  const pick = (key: string, value: string) => send('settings.pick', { key, value });
  const checkbox = (label: string, key: string, value: boolean, hint?: string) => <label className="nashi-setting-toggle"><span><strong>{label}</strong>{hint && <small>{hint}</small>}</span><input type="checkbox" checked={value} onChange={event => toggle(key, event.target.checked)} /></label>;
  const choice = (label: string, key: string, value: string, values: string[], titles = values) => <label className="nashi-setting-field"><span>{label}</span><select value={value} onChange={event => pick(key, event.target.value)}>{values.map((item, index) => <option key={item} value={item}>{titles[index]}</option>)}</select></label>;
  const profileChoice = (label: string, key: keyof NashiProfile, value: string, values: string[], titles = values) => <label className="nashi-setting-field"><span>{label}</span><select value={value} onChange={event => updateProfile({ [key]: event.target.value })}>{values.map((item, index) => <option key={item} value={item}>{titles[index]}</option>)}</select></label>;
  const updateProfile = (update: Partial<NashiProfile>) => { const next = { ...draft, ...update }; setDraft(next); send('settings.profile.save', { format, profile: next }); };
  const numberField = (label: string, key: keyof NashiProfile, min: number, max: number, step = 1, value = draft[key] as number) => <label className="nashi-setting-field"><span>{label}</span><input type="number" min={min} max={max} step={step} value={value} onChange={event => updateProfile({ [key]: key === 'quality' ? Number(event.target.value) / 100 : Number(event.target.value) })} /></label>;
  const settingsSections = ['General', 'Formats', 'Compatibility', 'Folders', 'Updates', 'About'];
  return <div className="feature-root feature-root-nashi-settings"><aside><strong>⚙ Nashi ayarları</strong><nav>{settingsSections.map(section => <button key={section} className={state.section === section ? 'active' : ''} onClick={() => send('settings.section', { section })}>{section === 'General' ? 'Genel' : section === 'Formats' ? 'Biçimler' : section === 'Compatibility' ? 'Uyumluluk' : section === 'Folders' ? 'Klasörler' : section === 'Updates' ? 'Güncellemeler' : 'Hakkında'}</button>)}</nav><button className="nashi-settings-close" onClick={() => send('settings.close')}>Ayarları kapat</button></aside><main><header><div><span className="nashi-eyebrow">TERCİHLER</span><h1>{state.section === 'General' ? 'Akışını özelleştir.' : state.section === 'Formats' ? 'Dışa aktarma varsayılanları.' : state.section === 'Compatibility' ? 'Uyumlu dönüşümler.' : state.section === 'Folders' ? 'Çıktı konumları.' : state.section === 'Updates' ? 'Güncel kal.' : 'Nashi hakkında.'}</h1></div><button onClick={() => send('settings.close')} aria-label="Kapat">×</button></header><section className="nashi-settings-content">
    {state.section === 'General' && <><p className="nashi-settings-intro">Küçük tercihler, daha rahat bir günlük kullanım.</p><div className="nashi-settings-grid">
      {choice('Dil', 'Language', state.language === 'tr' ? 'Türkçe' : 'English', ['English', 'Türkçe'])}{choice('Görünüm', 'Appearance', state.appearance, ['system', 'light', 'dark'], ['Sistem', 'Açık', 'Koyu'])}
      {checkbox('Sürükleme kısayolları', 'Enabled', state.enabled, 'Dosyaları sürüklerken araç çarkını kullan.')}{checkbox('Pencere yönetimi', 'WindowManagement', state.windowManagement, 'Başlık çubuğunu sürükleyip Shift veya Alt kullan.')}
      {checkbox('Sesli geri bildirim', 'Feedback', state.feedback)}{checkbox('Hafif animasyonlar', 'Animations', state.animations)}{checkbox('Sistem tepsisinde çalış', 'KeepInTray', state.keepInTray)}{checkbox('İşlem bitince Gezgin’de göster', 'RevealFinished', state.revealFinished)}
      {choice('Araç çarkı görünümü', 'WheelFinish', state.wheelFinish, ['glass', 'solid'], ['Cam', 'Düz'])}<label className="nashi-setting-field"><span>Çark boyutu <b>{Math.round(state.wheelScale * 100)}%</b></span><input type="range" min=".85" max="1.4" step=".05" value={state.wheelScale} onChange={event => send('settings.scale', { value: Number(event.target.value) })} /></label>
      {choice('Sürüklerken · Araçlar', 'ToolsModifier', state.toolsModifier, ['Ctrl', 'Shift', 'Alt', 'Ctrl+Shift', 'Ctrl+Alt', 'Shift+Alt'])}{choice('Sürüklerken · Dönüştür', 'ConvertModifier', state.convertModifier, ['Ctrl', 'Shift', 'Alt', 'Ctrl+Shift', 'Ctrl+Alt', 'Shift+Alt'])}{choice('Sürüklerken · Dosya işlemleri', 'ActionsModifier', state.actionsModifier, ['Ctrl', 'Shift', 'Alt', 'Ctrl+Shift', 'Ctrl+Alt', 'Shift+Alt'])}{choice('Yüzen tepsi davranışı', 'FloatBehavior', state.floatBehavior, ['createNew', 'addToExisting'], ['Yeni tepsi oluştur', 'Açık tepsiye ekle'])}
    </div><button className="nashi-settings-action" onClick={() => send('settings.feedback')}>Sesi dene</button></>}
      {state.section === 'Formats' && <><p className="nashi-settings-intro">Bu varsayılanlar, çıktı biçimini seçtiğinde otomatik uygulanır.</p><label className="nashi-setting-field nashi-format-select"><span>Çıktı biçimi</span><select value={format} onChange={event => { setFormat(event.target.value); send('settings.profile.select', { format: event.target.value }); }}>{state.formats.map(item => <option key={item} value={item}>{item.toUpperCase()}</option>)}</select></label><div className="nashi-settings-grid">
      {(state.compatible.mp4?.includes(format) || ['jpg', 'heic', 'webp', 'avif'].includes(format)) && numberField('Kalite (%)', 'quality', 10, 100, 1, Math.round(draft.quality * 100))}
      {(state.compatible.mp4?.includes(format) || (state.compatible.mp3?.includes(format) && !['wav', 'aiff', 'flac'].includes(format))) && numberField('Ses bit hızı (kbps)', 'audioBitrate', 8, 512)}
      {['wav', 'aiff'].includes(format) && profileChoice('Ses örnekleme derinliği', 'sampleDepth', draft.sampleDepth, ['matchSource', 'pcm16', 'pcm24', 'pcm32', 'float32'])}
      {state.compatible.mp4?.includes(format) && numberField('Maksimum video yüksekliği (0 = kaynak)', 'maxVideoHeight', 0, 8192)}
      {format === 'gif' && <>{numberField('Kare hızı', 'gifFrameRate', 1, 60)}{numberField('Genişlik', 'gifWidth', 16, 8192)}</>}
      {['png', 'jpg', 'epub'].includes(format) && numberField('PDF işleme DPI', 'dpi', 36, 600)}
      {['docx', 'pages'].includes(format) && profileChoice('PDF düzeni', 'layout', draft.layout, ['editable', 'preserve'], ['Düzenlenebilir', 'Görünümü koru'])}
      {format === 'epub' && profileChoice('EPUB düzeni', 'epubLayout', draft.epubLayout, ['reflowable', 'fixed'], ['Akışkan', 'Sabit'])}
      <label className="nashi-setting-toggle"><span><strong>Üst verileri koru</strong><small>Biçim destekliyorsa kaynak dosya bilgilerini taşı.</small></span><input type="checkbox" checked={draft.preserveMetadata} onChange={event => updateProfile({ preserveMetadata: event.target.checked })} /></label>
    </div><div className="nashi-settings-actions"><span>Değişiklikler otomatik kaydedilir.</span><button onClick={() => send('settings.profile.reset', { format })}>Varsayılana dön</button></div></>}
    {state.section === 'Compatibility' && <><p className="nashi-settings-intro">Desteklenen çıktı biçimleri kaynak türüne göre listelenir.</p><div className="nashi-compatibility-list">{Object.entries(state.compatible).sort(([a], [b]) => a.localeCompare(b)).map(([source, targets]) => <article key={source}><strong>{source.toUpperCase()}</strong><span>→</span><p>{targets.map(format => format.toUpperCase()).join('  ·  ')}</p></article>)}</div></>}
    {state.section === 'Folders' && <><p className="nashi-settings-intro">Özgün dosyalar korunur. Yeni kopyaların nereye kaydedileceğini seç.</p><div className="nashi-settings-grid">{choice('Yeni kopyaları kaydet', 'OutputMode', state.outputMode, ['beside', 'ask', 'folder'], ['Kaynağın yanına', 'Her seferinde sor', 'Seçilen klasöre'])}<div className="nashi-folder-picker"><span>Çıktı klasörü</span><p>{state.outputFolder || 'Henüz seçilmedi'}</p><button onClick={() => send('settings.folder', { folder: 'output' })}>Klasör seç</button></div><div className="nashi-folder-picker"><span>İndirilenler klasörü</span><p>{state.downloadFolder}</p><button onClick={() => send('settings.folder', { folder: 'download' })}>Klasör seç</button></div></div></>}
    {state.section === 'Updates' && <><p className="nashi-settings-intro">Güncellemeler herkese açık Nashi deposundan doğrulanarak indirilir.</p>{checkbox('Güncellemeleri otomatik kontrol et', 'AutomaticUpdateChecks', state.automaticUpdateChecks)}<button className="nashi-settings-action" onClick={() => send('settings.updates')}>Güncellemeleri kontrol et</button></>}
    {state.section === 'About' && <><p className="nashi-settings-intro">{state.about}</p><button className="nashi-settings-action" onClick={() => send('settings.source')}>Kaynak kodu ve üçüncü taraf bildirimleri</button></>}
    </section></main></div>;
}

function NashiSetupView({ state }: { state: NashiSetupState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const group = (id: 'media' | 'office', title: string, details: string, ready: boolean) => <article className="nashi-setup-group"><div className="nashi-setup-icon">{id === 'media' ? '♫' : '▤'}</div><div><header><strong>{title}</strong><span className={ready ? 'ready' : ''}>{ready ? 'Hazır' : 'Kurulmalı'}</span></header><p>{details}</p>{!ready && <button disabled={state.working} onClick={() => send('setup.install', { group: id })}>{id === 'media' ? 'Medya araçlarını indir' : 'Office araçlarını indir'}</button>}</div></article>;
  return <div className="feature-root feature-root-nashi-setup"><main><header><div><span className="nashi-eyebrow">İLK KURULUM</span><h1>Birkaç araç. Daha çok imkân.</h1><p>İndirmeler araç gruplarına ayrıdır. Hesap veya GitHub tokenı gerekmez.</p></div><button onClick={() => send('setup.close')} aria-label="Kapat">×</button></header><section className="nashi-setup-groups">{group('media', 'Medya araçları', 'FFmpeg, ffprobe, yt-dlp ve Deno medya dönüşümü, önizleme ve web indirmelerini sağlar. Yaklaşık 180 MB. FFmpeg ve yt-dlp GPL lisanslıdır; Deno MIT lisanslıdır.', state.mediaReady)}{group('office', 'Office araçları', 'LibreOffice 26.8.1 Office belge dönüşümlerini sağlar. Yaklaşık 360 MB. MPL 2.0 ve ilgili üçüncü taraf lisansları.', state.officeReady)}</section><button className="nashi-setup-source" onClick={() => send('setup.source')}>Sürümler, lisanslar ve kaynak bağlantıları ↗</button><footer><div role="status">{state.status || (state.ready ? 'Hazır. Dosyalarına dönebilirsin.' : 'Eksik araçları indirerek dönüşüm, önizleme ve web indirmelerini etkinleştir.')}</div>{state.working && <div className={`nashi-setup-progress ${state.indeterminate ? 'indeterminate' : ''}`}><i style={state.indeterminate ? undefined : { width: `${Math.round(state.progress * 100)}%` }} /></div>}<div className="nashi-setup-actions"><button disabled={!state.working} onClick={() => send('setup.cancel')}>Kurulumu iptal et</button><button className="nashi-primary" disabled={state.working} onClick={() => send('setup.continue')}>{state.ready ? 'Nashi’ye dön' : 'Eksik araçları indir'}</button></div></footer></main></div>;
}

function MetadataEditorView({ state }: { state: MetadataEditorState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  return <div className="feature-root feature-root-metadata-editor"><main><header><div><span className="nashi-eyebrow">DOSYA BİLGİLERİ</span><h1>Üst verileri düzenle</h1><p>{state.media ? 'Ses ve video kodlaması korunur; yalnızca seçili açıklayıcı alanlar değişir.' : 'Görsel pikselleri korunur; yalnızca seçili açıklayıcı alanlar değişir.'}</p></div><button onClick={() => send('metadata.cancel')} aria-label="Kapat">×</button></header><label className="metadata-remove-all"><span><strong>Tüm üst verileri kaldır</strong><small>Özgün dosya korunur ve yeni bir kopya oluşturulur.</small></span><input type="checkbox" checked={state.removeAll} onChange={event => send('metadata.remove-all', { value: event.target.checked })} /></label><section className="metadata-fields">{state.fields.map(field => <label key={field.key}><span>{field.label}</span><textarea rows={field.value.includes('\n') ? 3 : 1} disabled={state.removeAll} value={field.value} onChange={event => send('metadata.update', { key: field.key, value: event.target.value })} /></label>)}{state.fields.length === 0 && <p>Bu dosyada düzenlenebilir üst veri alanı bulunamadı.</p>}</section><details className="metadata-all"><summary>Tüm üst verileri görüntüle ({state.details.length})</summary><div>{state.details.map((item, index) => <p key={`${item.label}-${index}`}><strong>{item.label}</strong><span>{item.value || '—'}</span></p>)}</div></details><footer><span>Özgün dosya korunur.</span><div><button onClick={() => send('metadata.cancel')}>Vazgeç</button><button className="nashi-primary" disabled={!state.canSave} onClick={() => send('metadata.save')}>Yeni kopyayı kaydet ↗</button></div></footer></main></div>;
}

function PdfOrganizerView({ state }: { state: PdfOrganizerState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const [selected, setSelected] = useState<string[]>(state.selected);
  useEffect(() => { setSelected(state.selected); }, [state.pages, state.selected.join('|')]);
  return <div className="feature-root feature-root-pdf-organizer"><main><header><div><span className="nashi-eyebrow">BELGE DÜZENLEME</span><h1>PDF sayfalarını düzenle</h1><p>Sayfaları yeniden sırala, döndür, çoğalt veya kaldır.</p></div><button onClick={() => send('pdf-organizer.cancel')} aria-label="Kapat">×</button></header><div className="pdf-organizer-toolbar"><button disabled={!selected.length} onClick={() => send('pdf-organizer.move', { delta: -1 })}>← Sola taşı</button><button disabled={!selected.length} onClick={() => send('pdf-organizer.move', { delta: 1 })}>Sağa taşı →</button><button disabled={!selected.length} onClick={() => send('pdf-organizer.rotate')}>Döndür ↻</button><button disabled={!selected.length} onClick={() => send('pdf-organizer.duplicate')}>Çoğalt</button><button className="danger" disabled={!selected.length || state.pages.length - selected.length < 1} onClick={() => send('pdf-organizer.remove')}>Kaldır</button><label><input type="checkbox" checked={state.uniformWidth} onChange={event => send('pdf-organizer.uniform', { value: event.target.checked })} /> Sayfa genişliklerini eşitle</label><span>{state.pages.length} sayfa</span></div><section className="pdf-organizer-pages" onKeyDown={event => { if (event.key === 'Delete' && selected.length && state.pages.length - selected.length > 0) send('pdf-organizer.remove'); if (event.key === 'Escape') send('pdf-organizer.cancel'); }} tabIndex={0}>{state.pages.map((page, index) => <button key={page.id} className={`pdf-organizer-page ${selected.includes(page.id) ? 'selected' : ''}`} onClick={event => { const next = event.ctrlKey || event.metaKey ? selected.includes(page.id) ? selected.filter(id => id !== page.id) : [...selected, page.id] : [page.id]; setSelected(next); send('pdf-organizer.select', { ids: next }); }}><img src={page.image} alt={`Sayfa ${page.number}`} style={{ transform: `rotate(${page.rotation}deg)` }} /><span>Sayfa {page.number}{state.pages.filter(item => item.number === page.number).length > 1 ? ` · Kopya ${index + 1}` : ''}</span></button>)}</section><footer><span>Birden fazla sayfa için Ctrl tuşuyla seç.</span><div><button onClick={() => send('pdf-organizer.cancel')}>Vazgeç</button><button className="nashi-primary" disabled={!state.pages.length} onClick={() => send('pdf-organizer.save')}>Düzenlenmiş kopyayı kaydet ↗</button></div></footer></main></div>;
}

function NashiFilePreviewView({ state }: { state: FilePreviewState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const keyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape') send('preview.close');
    else if (event.altKey && event.key === 'ArrowLeft') send('preview.navigate', { delta: -1 });
    else if (event.altKey && event.key === 'ArrowRight') send('preview.navigate', { delta: 1 });
    else if (state.kind === 'pdf' && event.key === 'ArrowLeft') send('preview.pdf-page', { delta: -1 });
    else if (state.kind === 'pdf' && event.key === 'ArrowRight') send('preview.pdf-page', { delta: 1 });
  };
  return <div className="feature-root feature-root-file-preview" onKeyDown={keyDown} tabIndex={0}><main><header><div className="file-preview-navigation"><button disabled={!state.canPrevious} onClick={() => send('preview.navigate', { delta: -1 })} aria-label="Önceki dosya">←</button><div><strong title={state.file}>{state.file}</strong><small>{state.index} / {state.count}</small></div><button disabled={!state.canNext} onClick={() => send('preview.navigate', { delta: 1 })} aria-label="Sonraki dosya">→</button></div><div className="file-preview-actions"><button onClick={() => send('preview.open')}>Aç</button><button onClick={() => send('preview.close')} aria-label="Kapat">×</button></div></header><section className="file-preview-content">
    {state.kind === 'image' && <img src={state.image} alt={state.file} />}
    {state.kind === 'pdf' && <><img src={state.image} alt={`${state.file}, sayfa ${state.pdfPage}`} /><div className="file-preview-pdf-controls"><button disabled={state.pdfPage <= 1} onClick={() => send('preview.pdf-page', { delta: -1 })}>Önceki sayfa</button><span>{state.pdfPage} / {state.pdfPageCount}</span><button disabled={state.pdfPage >= state.pdfPageCount} onClick={() => send('preview.pdf-page', { delta: 1 })}>Sonraki sayfa</button></div></>}
    {state.kind === 'text' && <pre>{state.text}</pre>}
    {state.kind === 'video' && <video key={state.mediaUrl} src={state.mediaUrl} controls playsInline onError={() => send('preview.media-error')} />}
    {state.kind === 'audio' && <div className="file-preview-audio"><span>♫</span><strong>{state.file}</strong><audio key={state.mediaUrl} src={state.mediaUrl} controls onError={() => send('preview.media-error')} /></div>}
    {state.kind === 'message' && <div className="file-preview-message">{state.message}</div>}
    </section><footer><span>Alt + ← / → · Dosyalar&nbsp;&nbsp; Esc · Kapat</span>{state.kind === 'pdf' && <span>← / → · Sayfalar</span>}</footer></main></div>;
}

function FloatShelfView({ state }: { state: FloatShelfState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const selection = new Set(state.selected);
  const toggleSelection = (path: string, additive: boolean) => {
    const next = additive ? new Set(selection) : new Set<string>();
    if (additive && next.has(path)) next.delete(path); else next.add(path);
    send('float.select', { paths: [...next] });
  };
  const actions = [
    ['open', 'Open'], ['preview', 'Quick preview'], ['reveal', 'Show in Explorer'],
    ['copy', 'Copy'], ['copyPath', 'Copy path'], ['share', 'Share'],
    ['move', 'Move'], ['terminal', 'Open terminal'],
  ] as const;
  return <div className={`feature-root feature-root-float-shelf ${state.expanded ? 'expanded' : 'collapsed'}`}
    onMouseEnter={() => send('float.hover', { inside: true })} onMouseLeave={() => send('float.hover', { inside: false })}
    onKeyDown={event => {
      if (event.key === 'Escape') send('float.expand', { value: false });
      else if (event.key === 'Delete') send('float.remove');
      else if (event.key === ' ') send('float.preview');
      else if (event.key === 'Enter') send('float.open');
      else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') { event.preventDefault(); send('float.select', { paths: state.items.map(item => item.path) }); }
    }} tabIndex={0}>
    <main className="float-shelf-card">
      {!state.expanded ? <>
        <header className="float-shelf-header float-shelf-compact"><button className="float-shelf-grip" onPointerDown={event => { if (event.button === 0) send('float.move'); }} title="Move Float">⠿</button><strong>Float</strong><button onClick={() => send('float.close')} aria-label="Close Float">×</button></header>
        <button className="float-shelf-stack" onClick={() => send('float.expand', { value: true })} aria-label="Expand Float">
          {state.items.slice(-3).map((item, index) => <span key={item.path} className="float-stack-card" style={{ zIndex: index + 1, transform: `translate(${(2 - index) * -4}px, ${(2 - index) * 3}px) rotate(${(2 - index) * -6}deg)` }}>{item.image ? <img src={item.image} alt="" /> : item.folder ? '▰' : item.extension || 'FILE'}</span>)}
          {state.items.length === 0 && <span className="float-stack-empty">＋</span>}
        </button>
        <button className="float-shelf-count" onClick={() => send('float.expand', { value: true })}>{state.count} {state.count === 1 ? 'item' : 'items'} <span>Open</span></button>
      </> : <>
        <header className="float-shelf-header">
          <button className="float-shelf-grip" onPointerDown={event => { if (event.button === 0) send('float.move'); }} title="Move Float">⠿</button>
          <div><strong>Float</strong><span>{state.count} {state.count === 1 ? 'item' : 'items'}{state.edge ? ` · ${state.edge}` : ''}</span></div>
          <button onClick={() => send('float.expand', { value: false })}>Collapse</button>
          <button onClick={() => send('float.close')} aria-label="Close Float">×</button>
        </header>
        <nav className="float-shelf-toolbar">
          <button onClick={() => send('float.add')}>Add files</button>
          <button onClick={() => send('float.mode', { icons: !state.icons })}>{state.icons ? 'List view' : 'Icon view'}</button>
          <button onClick={() => send('float.reuse')}>Use in Nashi</button>
          <button onClick={() => send('float.clear')}>Clear</button>
          <details><summary>More</summary><div>{actions.map(([id, label]) => <button key={id} onClick={() => id === 'open' ? send('float.open') : id === 'preview' ? send('float.preview') : id === 'reveal' ? send('float.reveal') : send('float.action', { id })}>{label}</button>)}<hr />{['Top', 'Right', 'Bottom', 'Left', 'Detach'].map(edge => <button key={edge} onClick={() => send('float.edge', { edge })}>{edge === 'Detach' ? 'Detach from edge' : `Attach ${edge.toLowerCase()}`}</button>)}</div></details>
        </nav>
        <section className={`float-shelf-items ${state.icons ? 'icon-mode' : 'list-mode'}`} aria-label="Files">
          {state.items.map(item => <button key={item.path} className={`float-shelf-item ${selection.has(item.path) ? 'selected' : ''}`} onClick={event => toggleSelection(item.path, event.ctrlKey || event.metaKey)} onDoubleClick={() => send('float.open')} draggable onDragStart={event => { event.preventDefault(); send('float.drag-files', { paths: selection.has(item.path) ? state.selected : [item.path] }); }} title={item.path}>
            <span className={`float-shelf-art ${item.folder ? 'folder' : ''}`}>{item.image ? <img src={item.image} alt="" /> : item.folder ? '▰' : item.extension || 'FILE'}</span><span className="float-shelf-name">{item.name}</span>
          </button>)}
          {state.items.length === 0 && <button className="float-shelf-empty" onClick={() => send('float.add')}>＋<span>Add files to Float</span></button>}
        </section>
        <footer><span role="status">{state.notice || (state.selected.length ? `${state.selected.length} selected` : 'Select files · Space to preview')}</span><button onClick={() => send('float.remove')} disabled={!state.selected.length}>Remove selected</button></footer>
      </>}
    </main>
  </div>;
}

function WheelView({ state }: { state: WheelState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const rootRef = useRef<HTMLDivElement>(null);
  useEffect(() => { if (!state.externalDrag) rootRef.current?.focus(); }, [state.externalDrag]);
  const count = state.choices.length;
  const point = (radius: number, angle: number) => {
    const radians = angle * Math.PI / 180;
    return [270 + radius * Math.cos(radians), 270 + radius * Math.sin(radians)];
  };
  const keyDown = (event: React.KeyboardEvent) => {
    const key = event.key === 'ArrowRight' ? 'Right' : event.key === 'ArrowLeft' ? 'Left' : event.key === 'ArrowUp' ? 'Up' : event.key === 'ArrowDown' ? 'Down' : event.key === ' ' ? 'Space' : event.key === 'Enter' ? 'Enter' : event.key === 'Escape' ? 'Escape' : '';
    if (key) { event.preventDefault(); send('wheel.key', { key }); }
  };
  return <div ref={rootRef} className={`feature-root feature-root-wheel ${state.externalDrag ? 'external-drag' : ''} ${state.finish}`} onKeyDown={keyDown} tabIndex={state.externalDrag ? -1 : 0}>
    <svg viewBox="0 0 540 540" role="group" aria-label={state.center}>
      <circle cx="270" cy="270" r="264" className="wheel-backdrop" />
      {state.choices.map((choice, index) => {
        const angle = state.startAngle + 360 * index / count;
        const half = 180 / count - 1;
        const from = angle - half, to = angle + half;
        const innerStart = point(108, from), outerStart = point(250, from), outerEnd = point(250, to), innerEnd = point(108, to);
        const largeArc = to - from > 180 ? 1 : 0;
        const path = `M ${innerStart[0]} ${innerStart[1]} L ${outerStart[0]} ${outerStart[1]} A 250 250 0 ${largeArc} 1 ${outerEnd[0]} ${outerEnd[1]} L ${innerEnd[0]} ${innerEnd[1]} A 108 108 0 ${largeArc} 0 ${innerStart[0]} ${innerStart[1]} Z`;
        const [labelX, labelY] = point(179, angle);
        return <g key={choice.id} className={state.selected === index ? 'selected' : ''} onPointerEnter={() => !state.externalDrag && send('wheel.highlight', { index })} onClick={() => !state.externalDrag && send('wheel.select', { index })} role={state.externalDrag ? undefined : 'button'} tabIndex={state.externalDrag ? undefined : 0} aria-label={choice.title}>
          <path d={path} className="wheel-sector" />
          <text x={labelX} y={labelY - 8} className="wheel-symbol">{choice.symbol}</text>
          <text x={labelX} y={labelY + 12} className="wheel-label">{choice.title}</text>
        </g>;
      })}
      <circle cx="270" cy="270" r="105" className="wheel-center" />
      <text x="270" y="264" className="wheel-center-title">{state.center}</text>
      <text x="270" y="287" className="wheel-center-subtitle">{state.subtitle}</text>
    </svg>
    {!state.externalDrag && <button className="wheel-dismiss" onClick={() => send('wheel.close')} aria-label="Close menu">×</button>}
  </div>;
}

function DictationStatusView({ state }: { state: DictationStatusState }) {
  const send = (command: string) => window.chrome?.webview?.postMessage({ command, payload: {} });
  return <div className={`feature-root feature-root-dictation ${state.result ? 'has-result' : ''}`}>
    <section className="dictation-card">
      <div className="dictation-indicator"><i className={state.recording ? 'recording' : ''} /><span>{state.recording ? 'LISTENING' : state.result ? 'TRANSCRIPTION' : 'DICTATION'}</span></div>
      <p role="status">{state.message}</p>
      {state.recording && <div className="dictation-level"><i style={{ width: `${Math.max(3, Math.min(100, state.audioLevel))}%` }} /></div>}
      {state.result && <><pre>{state.result}</pre><button className="dictation-copy" onClick={() => send('dictation.copy')}>Copy text</button></>}
      {!state.result && <small>Esc · Cancel</small>}
    </section>
  </div>;
}

function ZonePreviewView({ state }: { state: ZonePreviewState }) {
  return <div className="feature-root feature-root-zone-preview" style={{ width: state.width, height: state.height }}>
    {state.zones.map(zone => <div key={zone.index} className={`tiling-zone ${zone.active ? 'active' : ''}`} style={{ left: zone.x, top: zone.y, width: zone.width, height: zone.height, borderWidth: zone.active ? state.borderThickness + 1 : state.borderThickness, '--zone-color': zone.active ? state.activeColor : state.inactiveColor } as React.CSSProperties}>
      {state.showNumbers && <span>{zone.index + 1}</span>}
    </div>)}
    {state.merged && <div className="tiling-zone-merged" style={{ left: state.merged.x, top: state.merged.y, width: state.merged.width, height: state.merged.height, borderWidth: state.borderThickness + 1, '--zone-color': state.activeColor } as React.CSSProperties} />}
  </div>;
}

function CropThumbnailView({ state }: { state: CropThumbnailState }) {
  const send = () => window.chrome?.webview?.postMessage({ command: 'crop-thumbnail.close', payload: {} });
  return <div className="feature-root feature-root-crop-thumbnail"><header><span><strong>{state.title}</strong><small>{state.subtitle}</small></span><button onClick={send} aria-label="Kırpılmış görünümü kapat">×</button></header></div>;
}

function CropSelectionView({ state }: { state: CropSelectionState }) {
  const [selection, setSelection] = useState<{ startX: number; startY: number; endX: number; endY: number } | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  useEffect(() => { rootRef.current?.focus(); }, []);
  const send = (command: string, payload: Record<string, number> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const pointerMove = (event: React.PointerEvent<HTMLDivElement>) => {
    setSelection(current => current ? { ...current, endX: event.clientX, endY: event.clientY } : null);
  };
  const pointerUp = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!selection) return;
    const left = Math.min(selection.startX, event.clientX);
    const top = Math.min(selection.startY, event.clientY);
    const width = Math.abs(event.clientX - selection.startX);
    const height = Math.abs(event.clientY - selection.startY);
    setSelection(null);
    send('crop-selection.complete', { x: left, y: top, width, height });
  };
  const rect = selection ? {
    left: Math.min(selection.startX, selection.endX), top: Math.min(selection.startY, selection.endY),
    width: Math.abs(selection.endX - selection.startX), height: Math.abs(selection.endY - selection.startY),
  } : null;
  return <div ref={rootRef} className="feature-root feature-root-crop-selection" tabIndex={0}
    onPointerDown={event => { if (event.button === 0) { event.currentTarget.setPointerCapture(event.pointerId); setSelection({ startX: event.clientX, startY: event.clientY, endX: event.clientX, endY: event.clientY }); } }}
    onPointerMove={pointerMove} onPointerUp={pointerUp}
    onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); send('crop-selection.cancel'); } }}>
    {!selection && <div className="crop-selection-hint">{state.hint}</div>}
    {rect && <div className="crop-selection-rect" style={rect} />}
  </div>;
}

function TextSelectionView({ state }: { state: TextSelectionState }) {
  const [selection, setSelection] = useState<{ x: number; y: number; width: number; height: number } | null>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  useEffect(() => { rootRef.current?.focus(); }, []);
  const send = (command: string, payload: Record<string, number> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const pointer = useRef<{ x: number; y: number } | null>(null);
  const update = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!pointer.current) return;
    const x = Math.min(pointer.current.x, event.clientX), y = Math.min(pointer.current.y, event.clientY);
    setSelection({ x, y, width: Math.abs(event.clientX - pointer.current.x), height: Math.abs(event.clientY - pointer.current.y) });
  };
  const complete = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!pointer.current) return;
    const x = Math.min(pointer.current.x, event.clientX), y = Math.min(pointer.current.y, event.clientY);
    const width = Math.abs(event.clientX - pointer.current.x), height = Math.abs(event.clientY - pointer.current.y);
    pointer.current = null;
    send('screen-selection.complete', { x, y, width, height });
  };
  return <div ref={rootRef} className="feature-root feature-root-text-selection" tabIndex={0}
    onPointerDown={event => { if (event.button === 0) { event.currentTarget.setPointerCapture(event.pointerId); pointer.current = { x: event.clientX, y: event.clientY }; setSelection({ x: event.clientX, y: event.clientY, width: 0, height: 0 }); } }}
    onPointerMove={update} onPointerUp={complete}
    onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); send('screen-selection.cancel'); } }}>
    {!pointer.current && !selection && <div className="text-selection-hint">{state.hint}</div>}
    {selection && <div className="text-selection-rect" style={selection} />}
  </div>;
}

function ToolEditorView({ state }: { state: ToolEditorState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const playback = (action: string, value?: number) => send('tool-editor.playback', value === undefined ? { action } : { action, value });
  const [seekValue, setSeekValue] = useState(state.currentTime);
  const isSeeking = useRef(false);
  useEffect(() => { if (!isSeeking.current) setSeekValue(state.currentTime); }, [state.currentTime]);
  const formatTime = (seconds: number) => { const minutes = Math.floor(seconds / 60); const remaining = Math.floor(seconds % 60); const millis = Math.floor((seconds % 1) * 1000); return `${minutes.toString().padStart(2, '0')}:${remaining.toString().padStart(2, '0')}.${millis.toString().padStart(3, '0')}`; };
  return <div className="feature-root feature-root-tool-editor"><main><header><div><span className="nashi-eyebrow">DOSYA DÜZENLEME</span><h1>{state.title}</h1><p>{state.file} · Özgün dosya korunur</p></div></header>{state.media && <section className="tool-editor-playback"><div className="tool-editor-transport"><button aria-label="Geri sar" onClick={() => playback('back')}>−1 sn</button><button className="tool-editor-play" aria-label={state.playing ? 'Duraklat' : 'Oynat'} onClick={() => playback('toggle')}>{state.playing ? 'Ⅱ' : '▶'}</button><button aria-label="İleri sar" onClick={() => playback('forward')}>+1 sn</button>{state.canPlayRange && <button onClick={() => playback('range')}>Seçili alanı oynat</button>}</div><label className="tool-editor-seek"><span>{formatTime(seekValue)}</span><input aria-label="Önizleme konumu" type="range" min="0" max={Math.max(state.duration, 0.01)} step="0.01" value={Math.min(seekValue, state.duration)} onPointerDown={() => { isSeeking.current = true; }} onPointerUp={() => { isSeeking.current = false; }} onBlur={() => { isSeeking.current = false; }} onChange={event => { const value = Number(event.target.value); setSeekValue(value); playback('seek', value); }} /><span>{formatTime(state.duration)}</span></label>{state.showComparison && <div className="tool-editor-comparison"><button disabled={state.processingPreview} onClick={() => playback('original')}>Özgün</button><button disabled={state.processingPreview} onClick={() => playback('processed')}>{state.processingPreview ? 'Hazırlanıyor…' : state.comparisonLabel}</button></div>}{state.playbackStatus && <p className="tool-editor-playback-status">{state.playbackStatus}</p>}</section>}<section className="tool-editor-options">{state.controls.map(control => {
    if (control.type === 'help') return <p className="tool-editor-help" key={control.id}>{control.label}</p>;
    if (control.type === 'button') return <button className="tool-editor-action" key={control.id} onClick={() => send('tool-editor.activate', { id: control.id })}>{control.label}</button>;
    if (control.type === 'checkbox') return <label className="tool-editor-check" key={control.id}><span>{control.label}</span><input type="checkbox" checked={control.value === true} onChange={event => send('tool-editor.change', { id: control.id, value: event.target.checked })} /></label>;
    if (control.type === 'range') return <label className="tool-editor-range" key={control.id}><span>{control.label}<output>{Number(control.value).toFixed(2)}</output></span><input type="range" min={control.minimum} max={control.maximum} step="0.01" value={Number(control.value)} onChange={event => send('tool-editor.change', { id: control.id, value: Number(event.target.value) })} /></label>;
    if (control.type === 'select') return <label className="tool-editor-field" key={control.id}><span>{control.label}</span><select value={Number(control.value ?? 0)} onChange={event => send('tool-editor.change', { id: control.id, value: Number(event.target.value) })}>{(control.options ?? []).map((option, index) => <option key={`${control.id}-${index}`} value={index}>{option}</option>)}</select></label>;
    if (control.type === 'list') return <div className="tool-editor-list-wrap" key={control.id}><span className="tool-editor-label">{control.label}</span><div className="tool-editor-list">{(control.items ?? []).map((item, index) => <button className={Number(control.value) === index ? 'selected' : ''} key={`${control.id}-${index}`} onClick={() => send('tool-editor.change', { id: control.id, value: index })}>{item}</button>)}</div>{control.reorderable && <div className="tool-editor-list-actions"><button onClick={() => send('tool-editor.move', { id: control.id, delta: -1 })}>Yukarı taşı</button><button onClick={() => send('tool-editor.move', { id: control.id, delta: 1 })}>Aşağı taşı</button></div>}</div>;
    return <label className="tool-editor-field" key={control.id}><span>{control.label}</span><input type={control.type === 'password' ? 'password' : 'text'} value={String(control.value ?? '')} onChange={event => send('tool-editor.change', { id: control.id, value: event.target.value })} /></label>;
  })}{state.controls.length === 0 && <p className="tool-editor-empty">Bu işlem için ek ayar gerekmiyor.</p>}</section><footer><span>Özgün dosya korunur.</span><div><button onClick={() => send('tool-editor.cancel')}>Vazgeç</button><button onClick={() => send('tool-editor.reset')}>Sıfırla</button><button className="nashi-primary" onClick={() => send('tool-editor.save')}>Yeni kopyayı kaydet ↗</button></div></footer></main></div>;
}

function ProgressView({ state }: { state: ProgressState }) {
  const send = (command: string) => window.chrome?.webview?.postMessage({ command, payload: {} });
  return <div className="feature-root feature-root-progress"><main><div className="progress-mark">{state.complete ? '✓' : '↗'}</div><section><strong>{state.label}</strong><p>{state.message}</p>{!state.complete && <div className={`progress-track ${state.fraction === null ? 'indeterminate' : ''}`}><i style={state.fraction === null ? undefined : { width: `${Math.max(0, Math.min(1, state.fraction)) * 100}%` }} /></div>}</section>{state.complete ? state.canReveal && <button onClick={() => send('progress.reveal')}>Klasörde göster</button> : <button disabled={state.cancelled} onClick={() => send('progress.cancel')}>{state.cancelled ? 'İptal ediliyor…' : 'İptal'}</button>}</main></div>;
}

function QrResultsView({ state }: { state: QrResultsState }) {
  const send = (command: string, index: number) => window.chrome?.webview?.postMessage({ command, payload: { index } });
  return <div className="feature-root feature-root-qr-results"><main>
    <header><div><span className="nashi-eyebrow">BELGE ARAÇLARI</span><h1>QR kod sonuçları</h1><p>{state.status}</p></div><button onClick={() => window.chrome?.webview?.postMessage({ command: 'window.close', payload: {} })} aria-label="Kapat">×</button></header>
    <section className="qr-results-list">{state.results.map(result => <article key={`${result.index}-${result.file}`}>
      <small>{result.file}</small><code>{result.value}</code><div><button onClick={() => send('qr.copy', result.index)}>Kopyala</button>{result.canOpen && <button className="nashi-primary" onClick={() => send('qr.open', result.index)}>Bağlantıyı aç ↗</button>}</div>
    </article>)}{state.results.length === 0 && <div className="qr-results-empty"><span>⌕</span><strong>QR kodu bulunamadı</strong><p>Daha büyük veya daha net bir görsel deneyin.</p></div>}</section>
  </main></div>;
}

function ClipboardShelfView({ state }: { state: ClipboardShelfState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const [actionsOpen, setActionsOpen] = useState(false);
  const [folderChooserOpen, setFolderChooserOpen] = useState(false);
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);
  useEffect(() => { if (state.searchFocusGeneration > 0) document.querySelector<HTMLInputElement>('.clipboard-search input')?.focus(); }, [state.searchFocusGeneration]);
  useEffect(() => { if (state.actionsOpenGeneration > 0) setActionsOpen(true); }, [state.actionsOpenGeneration]);
  useEffect(() => { if (state.folderPickerGeneration > 0) setFolderChooserOpen(true); }, [state.folderPickerGeneration]);
  useEffect(() => { if (state.deleteConfirmGeneration > 0) setDeleteConfirmOpen(true); }, [state.deleteConfirmGeneration]);
  const runAction = (id: number) => {
    if (state.actions.find(item => item.id === id)?.name === 'Klasöre ekle') {
      setFolderChooserOpen(true);
      setActionsOpen(false);
      return;
    }
    send('clipboard.action', { id });
  };
  const action = (name: string) => state.actions.find(item => item.name === name);
  const selected = new Set(state.selected);
  const selectedFolder = state.folders.find(folder => folder.selected);
  const selectedTag = state.tagList.find(tag => tag.selected);
  const chooseItem = (item: ClipboardCard, event: React.MouseEvent) => {
    if (state.selectMode || state.libraryMode || event.ctrlKey || event.metaKey) {
      const next = event.ctrlKey || event.metaKey ? selected.has(item.id) ? state.selected.filter(id => id !== item.id) : [...state.selected, item.id] : [item.id];
      send('clipboard.select', { ids: next });
      return;
    }
    send('clipboard.quick-copy', { ids: [item.id] });
  };
  const renderCard = (item: ClipboardCard) => <button key={item.id} className={`clipboard-card ${selected.has(item.id) ? 'selected' : ''}`} onClick={event => chooseItem(item, event)} onDoubleClick={() => send('clipboard.preview', { ids: [item.id] })} title={item.title}>
    {item.preview ? <img src={item.preview} alt="" /> : <span className={`clipboard-card-icon kind-${item.kind}`}>{item.kind === 'image' ? '▧' : item.kind === 'recording' ? '◉' : item.kind === 'link' ? '↗' : 'T'}</span>}
    <strong>{item.pinned ? '★ ' : ''}{item.title}</strong>{item.description && <small>{item.description}</small>}{item.tags.length > 0 && <small className="clipboard-tags">{item.tags.join(' · ')}</small>}<small className="clipboard-created">{item.created}</small>
  </button>;
  const primaryNames = ['Kopyala', 'Yapıştır', 'Önizle', 'Sabitle', 'Kaydı kırp', 'Klasöre ekle', 'Dışa aktar', 'Etiket ekle', 'Ekran al'];
  const primaryActions = primaryNames.map(name => action(name)).filter((item): item is ClipboardAction => item !== undefined);
  const selectionRequired = new Set(['Kopyala', 'Yapıştır', 'Önizle', 'Sabitle', 'Kaydı kırp', 'Klasöre ekle', 'Adlandır ve sabitle', 'Dışa aktar', 'Etiket ekle', 'Tanı nan metni kopyala', 'Bağlantıyı aç', 'Bağlantı bilgilerini yenile', 'Raftan kaldır', 'Çöpe taşı', 'Geri yükle']);
  const actionButton = (item: ClipboardAction) => <button key={item.id} disabled={selectionRequired.has(item.name) && state.selectionCount === 0} onClick={() => runAction(item.id)}>{item.name}</button>;
  const filter = (key: string, value: unknown) => send('clipboard.filter', { key, value });
  const modeButton = (library: boolean) => <button className={state.libraryMode === library ? 'active' : ''} onClick={() => send('clipboard.mode', { library })}>{library ? 'Kitaplık' : 'Raflar'}</button>;
  const listOptions = <div className="clipboard-view-options"><label><input type="checkbox" checked={state.showImages} onChange={event => send('clipboard.preference', { key: 'showImages', value: event.target.checked })} /> Görseller</label><label><input type="checkbox" checked={state.showTexts} onChange={event => send('clipboard.preference', { key: 'showTexts', value: event.target.checked })} /> Metinler</label><label><input type="checkbox" checked={state.showLinks} onChange={event => send('clipboard.preference', { key: 'showLinks', value: event.target.checked })} /> Bağlantılar</label></div>;
  return <div className="feature-root feature-root-clipboard" tabIndex={0} onKeyDown={event => { const target = event.target; if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target instanceof HTMLButtonElement && (event.key === ' ' || event.key === 'Enter') || event.repeat) return; if (event.key === ' ' || event.key.startsWith('Arrow')) event.preventDefault(); send('clipboard.key', { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey }); }}>
    <header className="clipboard-topbar"><div className="clipboard-brand"><span>▤</span><strong>ClipBoardLine</strong></div><nav>{modeButton(false)}{modeButton(true)}</nav><label className="clipboard-search"><span>⌕</span><input value={state.query} onChange={event => send('clipboard.search', { value: event.target.value })} placeholder="Metin, OCR, başlık, etiket ara" /><select value={state.searchMode} onChange={event => filter('searchMode', Number(event.target.value))}><option value={0}>Metin / OCR</option><option value={1}>Görsel</option><option value={2}>Metin + görsel</option></select></label><button className="clipboard-gear" onClick={() => action('Ayarlar') && runAction(action('Ayarlar')!.id)}>Ayarlar</button></header>
    <section className="clipboard-filters"><label><input type="checkbox" checked={state.pinnedOnly} onChange={event => filter('pinned', event.target.checked)} /> Sabitlenenler</label><label><input type="checkbox" checked={state.trash} onChange={event => filter('trash', event.target.checked)} /> Çöp</label><label><input type="checkbox" checked={state.selectMode} onChange={event => filter('selectMode', event.target.checked)} /> Çoklu seçim</label><select value={state.type} onChange={event => filter('type', Number(event.target.value))}><option value={0}>Tüm türler</option><option value={1}>Görseller</option><option value={2}>Metinler</option><option value={3}>Bağlantılar</option><option value={4}>Kayıtlar</option></select><select value={state.date} onChange={event => filter('date', Number(event.target.value))}><option value={0}>Tüm tarihler</option><option value={1}>Bugün</option><option value={2}>Son 7 gün</option><option value={3}>Son 30 gün</option></select><input value={state.domain} onChange={event => filter('domain', event.target.value)} placeholder="Alan adı" /><input value={state.tagsFilter} onChange={event => filter('tags', event.target.value)} placeholder="Etiketler (virgülle)" /><select value={state.sort} onChange={event => filter('sort', Number(event.target.value))}><option value={0}>Varsayılan</option><option value={1}>Son kopyalanan</option><option value={2}>Başlık A–Z</option><option value={3}>Başlık Z–A</option><option value={4}>En eski</option><option value={5}>En yeni</option><option value={6}>Tür</option><option value={7}>Etiketler</option></select></section>
    <section className="clipboard-actions"><div>{primaryActions.map(actionButton)}</div><button className={actionsOpen ? 'active' : ''} onClick={() => setActionsOpen(value => !value)}>Tüm eylemler {actionsOpen ? '⌃' : '⌄'}</button>{actionsOpen && <div className="clipboard-action-menu">{state.actions.filter(item => !primaryNames.includes(item.name) && item.name !== 'Eylemler' && item.name !== 'Ayarlar').map(actionButton)}</div>}</section>
    {state.libraryMode ? <div className="clipboard-library-layout"><aside className="clipboard-sidebar"><strong>Görünümler</strong>{[['all', 'Tümü'], ['saved', 'Kaydedilenler'], ['recent', 'Son kopyalananlar'], ['unorganized', 'Düzenlenmemiş']].map(([value, label]) => <button key={value} className={state.scope === value ? 'active' : ''} onClick={() => send('clipboard.scope', { value })}>{label}</button>)}<div className="clipboard-sidebar-heading"><strong>Klasörler</strong></div>{state.folders.map(folder => <button key={folder.id} className={folder.selected ? 'active' : ''} style={{ paddingLeft: 9 + folder.depth * 12 }} onClick={() => send('clipboard.folder', { id: folder.id })}>{folder.name}</button>)}<div className="clipboard-sidebar-heading"><strong>Etiketler</strong></div>{state.tagList.map(tag => <button key={tag.name} className={tag.selected ? 'active' : ''} onClick={() => send('clipboard.tag', { value: tag.name })}># {tag.name}</button>)}{listOptions}<label className="clipboard-select-field"><span>Kart boyutu</span><select value={state.cardSize} onChange={event => send('clipboard.preference', { key: 'cardSize', value: event.target.value })}><option value="small">Küçük</option><option value="medium">Orta</option><option value="large">Büyük</option></select></label><label><input type="checkbox" checked={state.listView} onChange={event => send('clipboard.preference', { key: 'listView', value: event.target.checked })} /> Liste görünümü</label></aside><main className={`clipboard-library-content ${state.listView ? 'list-mode' : ''}`}><div className="clipboard-results-heading"><strong>{state.count} kayıt</strong><span>Sayfa {state.page + 1} / {state.pageCount}</span><div><button disabled={state.page <= 0} onClick={() => send('clipboard.page', { row: 0, delta: -1 })}>Önceki</button><button disabled={state.page + 1 >= state.pageCount} onClick={() => send('clipboard.page', { row: 0, delta: 1 })}>Sonraki</button></div></div><div className="clipboard-card-grid">{state.items.map(renderCard)}{state.items.length === 0 && <p className="clipboard-empty">Bu görünümde kayıt yok.</p>}</div></main><aside className="clipboard-inspector"><strong>Kayıt ayrıntıları</strong>{!state.inspectorEnabled ? <p>Bir kayıt seçerek ayrıntılarını düzenle.</p> : <><label>Başlık<input value={state.title} onChange={event => send('clipboard.detail', { key: 'title', value: event.target.value })} /></label><label>İçerik<textarea readOnly={state.inspectorContentReadOnly} value={state.content} onChange={event => send('clipboard.detail', { key: 'content', value: event.target.value })} /></label><label>Notlar<textarea value={state.notes} onChange={event => send('clipboard.detail', { key: 'notes', value: event.target.value })} /></label><label>Etiketler<input value={state.itemTags} onChange={event => send('clipboard.detail', { key: 'tags', value: event.target.value })} /></label><small>Değişiklikler otomatik kaydedilir.</small></>}</aside></div> : <main className="clipboard-shelves">{state.rows.filter(row => row.visible).map(row => <section className="clipboard-shelf-row" key={row.index}><header><strong>{row.title}</strong><span>{row.count} kayıt · {row.page + 1}/{row.pageCount}</span><button disabled={row.page <= 0} onClick={() => send('clipboard.page', { row: row.index, delta: -1 })}>‹</button><button disabled={row.page + 1 >= row.pageCount} onClick={() => send('clipboard.page', { row: row.index, delta: 1 })}>›</button></header><div className="clipboard-card-strip">{row.items.map(renderCard)}{row.items.length === 0 && <p className="clipboard-empty">Henüz kayıt yok.</p>}</div></section>)}</main>}
    {state.libraryMode && selectedFolder && <div className="clipboard-selection-tools"><span>Klasör: {selectedFolder.name}</span><button onClick={() => send('clipboard.folder.rename', { id: selectedFolder.id })}>Yeniden adlandır</button><button className="danger" onClick={() => send('clipboard.folder.remove', { id: selectedFolder.id })}>Klasörü kaldır</button></div>}
    {state.libraryMode && selectedTag && <div className="clipboard-selection-tools"><span>Etiket: #{selectedTag.name}</span><button onClick={() => send('clipboard.tag.rename', { name: selectedTag.name })}>Yeniden adlandır</button><button className="danger" onClick={() => send('clipboard.tag.remove', { name: selectedTag.name })}>Etiketi kaldır</button></div>}
    <footer className="clipboard-status"><span role="status">{state.status}</span><span>{state.selectionCount ? `${state.selectionCount} seçili` : 'Space · Önizle  Enter · Yapıştır  P · Sabitle'}</span></footer>
    {folderChooserOpen && <div className="clipboard-folder-picker-backdrop"><section className="clipboard-folder-picker" role="dialog" aria-modal="true" aria-labelledby="clipboard-folder-picker-title">
      <header><div><span className="nashi-eyebrow">KÜTÜPHANE</span><h2 id="clipboard-folder-picker-title">{state.folderPickerPin ? 'Klasöre ekle ve sabitle' : 'Klasöre ekle'}</h2></div><button onClick={() => setFolderChooserOpen(false)} aria-label="Kapat">×</button></header>
      <div>{state.folders.map(folder => <button key={folder.id} style={{ paddingLeft: 10 + folder.depth * 12 }} onClick={() => { send('clipboard.assign-folder', { id: folder.id, ids: state.selected }); setFolderChooserOpen(false); }}>{folder.name}</button>)}{state.folders.length === 0 && <p>Henüz klasör yok.</p>}</div>
      <footer><button onClick={() => send('clipboard.folder.create', { ids: state.selected })}>＋ Yeni klasör</button><button onClick={() => setFolderChooserOpen(false)}>Vazgeç</button></footer>
    </section></div>}
    {deleteConfirmOpen && <div className="clipboard-folder-picker-backdrop"><section className="clipboard-delete-confirm" role="alertdialog" aria-modal="true" aria-labelledby="clipboard-delete-title">
      <span className="clipboard-delete-icon">!</span><h2 id="clipboard-delete-title">Kayıtlar kalıcı olarak silinsin mi?</h2><p>{state.permanentDeleteCount} kayıt geri alınamayacak şekilde silinecek.</p><footer><button onClick={() => { send('clipboard.delete-confirm', { confirm: false }); setDeleteConfirmOpen(false); }}>Vazgeç</button><button className="danger" onClick={() => { send('clipboard.delete-confirm', { confirm: true }); setDeleteConfirmOpen(false); }}>Kalıcı sil</button></footer>
    </section></div>}
  </div>;
}

function ClipboardSettingsView({ state }: { state: ClipboardSettingsState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const toggle = (key: keyof ClipboardSettingsState, label: string, detail: string) => <label className="clipboard-settings-toggle" key={key}>
    <span><strong>{label}</strong><small>{detail}</small></span>
    <input type="checkbox" checked={Boolean(state[key])} onChange={event => send('clipboard-settings.toggle', { key, value: event.target.checked })} />
  </label>;
  const retention = (key: 'imageRetentionDays' | 'textRetentionDays' | 'linkRetentionDays', label: string) => <label className="clipboard-settings-select" key={key}>
    <span>{label}</span><select value={state[key]} onChange={event => send('clipboard-settings.select', { key, value: Number(event.target.value) })}>
      {[1, 3, 7, 15, 30, state[key]].filter((value, index, values) => values.indexOf(value) === index).sort((a, b) => a - b).map(days => <option key={days} value={days}>{days} gün</option>)}
    </select>
  </label>;
  return <div className="feature-root feature-root-clipboard-settings"><main>
    <header><div><span className="nashi-eyebrow">KİŞİSELLEŞTİR</span><h1>Pano ayarları</h1><p>Yakalama, arşiv ve yerel kısayolları düzenle.</p></div><button onClick={() => send('clipboard-settings.close')} aria-label="Kapat">×</button></header>
    <section className="clipboard-settings-scroll">
      <h2>Yakalama ve gizlilik</h2>
      {toggle('captureText', 'Metinleri yakala', 'Kopyaladığın metinleri bu cihazda sakla.')}
      {toggle('captureImages', 'Görselleri yakala', 'Kopyalanan görseller yerel pano geçmişine eklenir.')}
      {toggle('captureLinks', 'Bağlantıları yakala', 'Tek başına kopyalanan bağlantıları da ekle.')}
      {toggle('fetchLinkMetadata', 'Bağlantı önizlemeleri', 'Genel HTTPS sayfalarından başlık ve küçük resim al.')}
      {toggle('indexImages', 'Görsellerde OCR', 'Görsel metinlerini cihazda indeksle.')}
      <h2>Görünüm</h2>
      {toggle('showImages', 'Görsel rafı', 'Görsel kayıtlarını ana rafta göster.')}
      {toggle('showTexts', 'Metin rafı', 'Metin kayıtlarını ana rafta göster.')}
      {toggle('showLinks', 'Bağlantı rafı', 'Bağlantı kayıtlarını ana rafta göster.')}
      {toggle('listView', 'Liste görünümü', 'Kartlar yerine yoğun liste düzeni kullan.')}
      {toggle('sounds', 'Kopyalama sesi', 'Bir kayıt panoya kopyalandığında ses çal.')}
      <label className="clipboard-settings-select"><span>Kart boyutu</span><select value={state.cardSize} onChange={event => send('clipboard-settings.select', { key: 'cardSize', value: event.target.value })}><option value="small">Küçük</option><option value="medium">Orta</option><option value="large">Büyük</option></select></label>
      <h2>Saklama süresi</h2>
      {retention('imageRetentionDays', 'Görseller')}{retention('textRetentionDays', 'Metinler')}{retention('linkRetentionDays', 'Bağlantılar')}
      <h2>İzlenen klasörler <small>(en fazla 3)</small></h2>
      <div className="clipboard-settings-folders">{state.watchFolders.map((folder, index) => <div key={`${folder}-${index}`}><span title={folder}>{folder}</span><button onClick={() => send('clipboard-settings.folder-remove', { index })} aria-label="Klasörü kaldır">Kaldır</button></div>)}{state.watchFolders.length < 3 && <button onClick={() => send('clipboard-settings.folder-add')}>＋ Klasör ekle</button>}</div>
      <h2>Kısayollar</h2>
      <p className="clipboard-settings-help">Her satıra <code>eylem=tuşlar</code> yazın. Boş değer ilgili kısayolu kapatır.</p>
      <textarea className="clipboard-settings-shortcuts" spellCheck={false} value={state.shortcuts} onChange={event => send('clipboard-settings.shortcuts', { value: event.target.value })} />
    </section>
    <footer><span role="status">{state.status}</span><div><button onClick={() => send('clipboard-settings.close')}>Kapat</button></div></footer>
  </main></div>;
}

function ClipboardPromptView({ state }: { state: ClipboardPromptState }) {
  const send = (command: string, value?: string) => window.chrome?.webview?.postMessage({ command, payload: value === undefined ? {} : { value } });
  return <div className="feature-root feature-root-clipboard-prompt"><main>
    <header><div><span className="nashi-eyebrow">PANO KÜTÜPHANESİ</span><h1>{state.title}</h1></div><button onClick={() => send('clipboard-prompt.cancel')} aria-label="Kapat">×</button></header>
    <input autoFocus value={state.value} onChange={event => send('clipboard-prompt.change', event.target.value)} onKeyDown={event => { if (event.key === 'Enter') send('clipboard-prompt.save'); if (event.key === 'Escape') send('clipboard-prompt.cancel'); }} />
    <footer><button onClick={() => send('clipboard-prompt.cancel')}>Vazgeç</button><button className="nashi-primary" onClick={() => send('clipboard-prompt.save')}>Tamam</button></footer>
  </main></div>;
}

function ClipboardPreviewView({ state }: { state: ClipboardPreviewState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const editorRef = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { if (state.editingText) editorRef.current?.focus(); }, [state.editingText, state.index]);
  const keyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const target = event.target;
    const editing = target instanceof HTMLElement && ['INPUT', 'TEXTAREA'].includes(target.tagName);
    if (editing) {
      if (event.key === 'Escape' || event.ctrlKey && event.key === 'Enter') {
        event.preventDefault(); send('clipboard-preview.key', { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey });
      }
      return;
    }
    if (['Escape', ' ', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key) || event.ctrlKey || event.altKey || event.shiftKey) {
      if (event.key.startsWith('Arrow') || event.key === ' ') event.preventDefault();
      send('clipboard-preview.key', { key: event.key, ctrl: event.ctrlKey, shift: event.shiftKey, alt: event.altKey });
    }
  };
  return <div className="feature-root feature-root-clipboard-preview" onKeyDown={keyDown} tabIndex={0} autoFocus><main><header><div className="clipboard-preview-nav"><button disabled={!state.canPrevious} onClick={() => send('clipboard-preview.navigate', { delta: -1 })}>‹</button><div><strong title={state.file}>{state.file}</strong><small>{state.index} / {state.count}</small></div><button disabled={!state.canNext} onClick={() => send('clipboard-preview.navigate', { delta: 1 })}>›</button></div><div className="clipboard-preview-actions">
    {state.canEditImage && <><button onClick={() => send('clipboard-preview.edit-image', { tool: 'crop' })}>Kırp</button><button onClick={() => send('clipboard-preview.edit-image', { tool: 'annotate' })}>İşaretle</button><label className="clipboard-preview-zoom">{Math.round(state.zoom * 100)}%<input type="range" min="1" max="4" step="0.05" value={state.zoom} onChange={event => send('clipboard-preview.zoom', { value: Number(event.target.value) })} /></label></>}
    {state.canEditText && <button onClick={() => send(state.editingText ? 'clipboard-preview.text-finish' : 'clipboard-preview.text-edit')}>{state.editingText ? 'Düzenlemeyi bitir' : 'Düzenle'}</button>}
    {state.canTrim && <button onClick={() => send('clipboard-preview.trim')}>Kaydı kırp</button>}{state.canOpenLink && <button onClick={() => send('clipboard-preview.open-link')}>Bağlantıyı aç ↗</button>}
    <button onClick={() => send('clipboard-preview.copy')}>Kopyala</button><button onClick={() => send('clipboard-preview.paste')}>Yapıştır</button><button className="clipboard-preview-close" onClick={() => send('clipboard-preview.close')}>×</button>
  </div></header><section className={`clipboard-preview-content kind-${state.kind}`} onWheel={event => { if (state.kind === 'image' && (event.ctrlKey || event.deltaY !== 0)) { event.preventDefault(); send('clipboard-preview.zoom', { value: Math.max(1, Math.min(4, state.zoom * (event.deltaY < 0 ? 1.12 : 1 / 1.12))) }); } }}>
    {state.kind === 'image' && <img src={state.image} alt={state.file} style={{ transform: `scale(${state.zoom})` }} onDoubleClick={() => send('clipboard-preview.zoom', { value: 1 })} />}
    {state.kind === 'link' && <article className="clipboard-preview-link">{state.image && <img src={state.image} alt="" />}<div>{state.author && <small>{state.author}</small>}<textarea ref={editorRef} readOnly={!state.editingText} value={state.text} onChange={event => send('clipboard-preview.text-change', { value: event.target.value })} /></div></article>}
    {state.kind === 'text' && <textarea ref={editorRef} className="clipboard-preview-text" readOnly={!state.editingText} value={state.text} onChange={event => send('clipboard-preview.text-change', { value: event.target.value })} />}
    {state.kind === 'recording' && <video src={state.mediaUrl} controls playsInline />}
    {state.kind === 'message' && <p>{state.message}</p>}
  </section><footer><span>{state.message || '← / → · Gezin  Esc · Kapat  Ctrl+Enter · Yapıştır'}</span><span>{state.kind === 'image' ? 'Tekerlekle yakınlaştır · Çift tıkla sığdır' : 'Değişiklikler otomatik kaydedilir'}</span></footer></main></div>;
}

function RecordingTrimView({ state }: { state: RecordingTrimState }) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [position, setPosition] = useState(0);
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const setPoint = (field: 'start' | 'end') => send('recording-trim.set-point', { field, position: videoRef.current?.currentTime ?? position });
  return <div className="feature-root feature-root-recording-trim" tabIndex={0} onKeyDown={event => { if (event.key === 'Escape') send('recording-trim.cancel'); else if (event.key === 'Enter' && !(event.target instanceof HTMLInputElement)) send('recording-trim.save', { start: state.start, end: state.end }); }}><main><header><div><span className="nashi-eyebrow">KAYIT DÜZENLEME</span><h1>Kayıt videosunu kırp</h1><p>{state.file} · Yeni kopya oluşturulur</p></div><button onClick={() => send('recording-trim.cancel')} aria-label="Kapat">×</button></header><video ref={videoRef} src={state.mediaUrl} controls playsInline onTimeUpdate={event => setPosition(event.currentTarget.currentTime)} /><label className="recording-trim-position"><span>{position.toFixed(2)} sn</span><input type="range" min="0" max={Math.max(.01, state.duration)} step="0.01" value={Math.min(position, state.duration || position)} onChange={event => { const time = Number(event.target.value); setPosition(time); if (videoRef.current) videoRef.current.currentTime = time; }} disabled={!state.ready || state.working} /></label><section className="recording-trim-range"><label><span>Başlangıç (saniye)</span><input inputMode="decimal" value={state.start} onChange={event => send('recording-trim.range', { start: event.target.value, end: state.end })} disabled={state.working} /></label><button onClick={() => setPoint('start')} disabled={!state.ready || state.working}>Geçerli konumu başlangıç yap</button><label><span>Bitiş (saniye)</span><input inputMode="decimal" value={state.end} onChange={event => send('recording-trim.range', { start: state.start, end: event.target.value })} disabled={state.working} /></label><button onClick={() => setPoint('end')} disabled={!state.ready || state.working}>Geçerli konumu bitiş yap</button></section><footer><div className="recording-trim-status" role="status">{state.status || 'Kırpılacak aralığı seçin.'}{state.working && <div className="progress-track"><i style={{ width: `${Math.max(0, Math.min(1, state.progress)) * 100}%` }} /></div>}</div><div><button disabled={state.working} onClick={() => send('recording-trim.cancel')}>{state.working ? 'İptal et' : 'Vazgeç'}</button><button className="nashi-primary" disabled={!state.ready || state.working} onClick={() => send('recording-trim.save', { start: state.start, end: state.end })}>{state.working ? 'Kırpılıyor…' : 'Kırp ve kaydet'}</button></div></footer></main></div>;
}

function NashiView({ state }: { state: NashiState }) {
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const navigate = (page: NashiState['page']) => send('nashi.navigate', { page });
  const pageKey = (event: React.KeyboardEvent<HTMLDivElement>) => {
    const target = event.target;
    const typing = target instanceof HTMLElement && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
    if (typing && !(event.ctrlKey && ['o', 'f'].includes(event.key.toLowerCase()))) return;
    if (event.ctrlKey && event.key.toLowerCase() === 'o') { event.preventDefault(); send('nashi.choose'); }
    else if (event.ctrlKey && event.key.toLowerCase() === 'f') { event.preventDefault(); navigate('library'); document.querySelector<HTMLInputElement>('.nashi-search')?.focus(); }
    else if (event.ctrlKey && event.key.toLowerCase() === 'v' && !typing) { event.preventDefault(); send('nashi.paste'); }
    else if (event.ctrlKey && event.key === '1') navigate('home');
    else if (event.ctrlKey && event.key === '2') navigate('library');
    else if (event.ctrlKey && event.key === ' ') { event.preventDefault(); send('nashi.open-wheel'); }
    else if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === 'd') { event.preventDefault(); send('nashi.paste-link'); }
  };
  const itemActions = (id: string) => <div className="nashi-item-actions"><button title="Önizle" onClick={() => send('nashi.library-action', { id, action: 'preview' })}>◉</button><button title="Kullan" onClick={() => send('nashi.library-action', { id, action: 'use' })}>↖</button><button title="Gezgin’de göster" onClick={() => send('nashi.library-action', { id, action: 'reveal' })}>▱</button></div>;
  const items = state.page === 'library' ? state.library : state.recent;
  return <div className="feature-root feature-root-nashi" onKeyDown={pageKey} tabIndex={0}>
    <aside className="nashi-sidebar"><div className="nashi-brand"><span>✿</span><strong>Nashi</strong></div><nav><button className={state.page === 'home' ? 'active' : ''} onClick={() => navigate('home')}><span>⌂</span> Ana sayfa</button><button className={state.page === 'library' ? 'active' : ''} onClick={() => navigate('library')}><span>▦</span> Kitaplık</button><button className={state.page === 'download' ? 'active' : ''} onClick={() => navigate('download')}><span>⇩</span> Web’den indir</button></nav><div className="nashi-sidebar-bottom"><div className="nashi-health"><i className={state.mediaReady ? 'ready' : ''} /> Medya araçları {state.mediaReady ? 'hazır' : 'kurulmalı'}<i className={state.officeReady ? 'ready' : ''} /> Office araçları {state.officeReady ? 'hazır' : 'kurulmalı'}</div><button onClick={() => send('nashi.setup-media')}>Medya araçlarını kur</button><button onClick={() => send('nashi.setup-office')}>Office araçlarını kur</button><button onClick={() => send('nashi.settings')}>Ayarlar</button><button onClick={() => send('nashi.shortcuts')}>Kısayollar</button></div></aside>
    <main className="nashi-main"><header className="nashi-topbar"><div><span className="nashi-eyebrow">DOSYALARIN YENİ HALİ</span><h1>{state.page === 'home' ? 'Ne yapmak istersin?' : state.page === 'library' ? 'Kitaplığın' : 'Web’den indir'}</h1></div><div><span className="nashi-status" role="status">{state.status || 'Dosyaların bilgisayarında kalır.'}</span><button className="nashi-icon-button" onClick={() => send('nashi.close')} aria-label="Kapat">×</button></div></header>
      {state.page === 'home' && <div className="nashi-page"><section className="nashi-welcome"><div><span className="nashi-eyebrow">DÖNÜŞTÜR · DÜZENLE · PAYLAŞ</span><h2>Dosyaların.<br />Yeni bir başlangıç.</h2><p>Görsel, video, belge veya arşivlerini tek bir yerden hazırla.</p><div className="nashi-actions"><button className="nashi-primary" disabled={state.busy} onClick={() => send('nashi.choose')}>Dosya seç <kbd>Ctrl O</kbd></button><button onClick={() => send('nashi.paste')}>Panodan yapıştır</button></div></div><div className="nashi-welcome-art"><span>✳</span><i>◌</i><b>↗</b></div></section>
        {state.files.length > 0 && <section className="nashi-section"><div className="nashi-section-heading"><div><span className="nashi-eyebrow">SEÇİLİ DOSYALAR</span><h2>{state.files.length} dosya hazır</h2></div><button onClick={() => send('nashi.choose')}>Dosya ekle</button><button onClick={() => send('nashi.clear')}>Temizle</button></div><div className="nashi-file-list">{state.files.map(file => <article key={file.path}><span className="nashi-file-icon">{file.name.split('.').pop()?.slice(0, 4).toUpperCase()}</span><div><strong>{file.name}</strong><small>{file.directory}</small></div><button onClick={() => send('nashi.remove', { path: file.path })} aria-label={`${file.name} seçimini kaldır`}>×</button></article>)}</div><div className="nashi-section-heading nashi-tool-heading"><div><span className="nashi-eyebrow">ARAÇLAR</span><h2>Sonraki adımı seç</h2></div><span>{state.formats.length} çıktı biçimi</span></div><div className="nashi-tool-grid">{state.formats.map(format => <button key={format} onClick={() => send('nashi.tool', { id: `convert:${format}` })}><span>⇄</span><strong>{format.toUpperCase()}</strong><small>Biçime dönüştür</small></button>)}{state.choices.map(choice => <button key={choice.id} onClick={() => send('nashi.tool', { id: choice.id })}><span>{choice.symbol || '✳'}</span><strong>{choice.title}</strong><small>Seçili dosyada uygula</small></button>)}</div></section>}
        {state.files.length === 0 && <section className="nashi-empty-drop" onDoubleClick={() => send('nashi.choose')}><div>＋</div><strong>Dosyalarını buraya ekle</strong><span>veya yukarıdaki Dosya seç düğmesini kullan</span></section>}
        <section className="nashi-section"><div className="nashi-section-heading"><div><span className="nashi-eyebrow">SON İŞLEMLER</span><h2>Yakın zamanda</h2></div><input value={state.recentQuery} onChange={event => send('nashi.recent-search', { query: event.target.value })} placeholder="Son işlemlerde ara" /><button onClick={() => navigate('library')}>Tümünü gör →</button></div><div className="nashi-library-list">{items.map(item => <article key={item.id}><span className="nashi-file-icon">{item.paths.length > 1 ? item.paths.length : item.name.split('.').pop()?.slice(0, 4).toUpperCase()}</span><div><strong>{item.name}</strong><small>{item.action} · {item.created}{item.missing ? ' · Bazı dosyalar bulunamadı' : ''}</small></div>{itemActions(item.id)}</article>)}{items.length === 0 && <p className="nashi-empty-note">Henüz bir işlem yok. İlk dosyanı ekleyerek başlayabilirsin.</p>}</div></section>
      </div>}
      {state.page === 'library' && <section className="nashi-library-page"><div className="nashi-search-row"><input className="nashi-search" value={state.query} onChange={event => send('nashi.search', { query: event.target.value })} placeholder="Dosya veya araç ara…" /><kbd>Ctrl F</kbd></div><div className="nashi-library-list">{state.library.map(item => <article key={item.id}><span className="nashi-file-icon">{item.paths.length > 1 ? item.paths.length : item.name.split('.').pop()?.slice(0, 4).toUpperCase()}</span><div><strong>{item.name}</strong><small>{item.action} · {item.created}{item.missing ? ` · ${item.available}/${item.paths.length} dosya bulundu` : ''}</small></div>{itemActions(item.id)}</article>)}{state.library.length === 0 && <p className="nashi-empty-note">Eşleşen kayıt yok.</p>}</div></section>}
      {state.page === 'download' && <section className="nashi-download-page"><div className="nashi-download-card"><span className="nashi-download-symbol">⇩</span><span className="nashi-eyebrow">WEB MEDYASI</span><h2>Bağlantıdan dosyaya.</h2><p>Herkese açık bir video, ses veya görsel bağlantısı ekle.</p><label>Bağlantı<input type="url" value={state.downloadUrl} onChange={event => send('nashi.download-update', { url: event.target.value })} placeholder="https://…" /></label><div className="nashi-download-options"><label>İçerik<select value={state.downloadKind} onChange={event => send('nashi.download-update', { kind: event.target.value })}>{state.downloadKinds.map(kind => <option key={kind} value={kind}>{kind === 'video' ? 'Video' : kind === 'audio' ? 'Ses' : kind === 'thumbnail' ? 'Küçük resim' : kind === 'photos' ? 'Fotoğraflar' : 'Görsel'}</option>)}</select></label><label>Kalite<select value={state.quality} onChange={event => send('nashi.download-update', { quality: Number(event.target.value) })}><option value={0}>En iyi</option><option value={1}>1080p</option><option value={2}>720p</option></select></label></div>{!state.mediaReady && <div className="nashi-notice">Video ve ses indirmek için medya araçlarını bir kez kur.</div>}<div className="nashi-actions"><button className="nashi-primary" disabled={state.busy || !state.downloadUrl.trim()} onClick={() => send('nashi.download')}>{state.busy ? 'İşleniyor…' : 'İndir'}</button>{!state.mediaReady && <button onClick={() => send('nashi.setup-media')}>Araçları kur</button>}</div></div></section>}
    </main>
  </div>;
}

const previewNetSpeed: NetSpeedState = {
  download: 'â†“ 12.4 MB/s',
  upload: 'â†‘ 860 KB/s',
  fontSize: 11,
  showDownload: true,
  showUpload: true,
  layout: 'Yan yana',
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
  title: 'Geceye Doğru',
  artist: 'Örnek Sanatçı',
  sourceName: 'Spotify',
  sourceType: 'Spotify',
  sourceBadge: 'Uygulamayı aç',
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

function ScreenRulerView({ state }: { state: ScreenRulerState }) {
  const root = useRef<HTMLDivElement>(null);
  const origin = useRef<{ x: number; y: number } | null>(null);
  const [selection, setSelection] = useState<{ x: number; y: number; width: number; height: number } | null>(null);
  const [measured, setMeasured] = useState<{ width: number; height: number; dpi: number } | null>(null);
  const send = (command: string, payload: Record<string, unknown> = {}) => window.chrome?.webview?.postMessage({ command, payload });
  const pixelRatio = Math.max(1, window.devicePixelRatio || 1);
  const updateSelection = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!origin.current) return;
    const x = Math.min(origin.current.x, event.clientX);
    const y = Math.min(origin.current.y, event.clientY);
    const width = Math.abs(event.clientX - origin.current.x);
    const height = Math.abs(event.clientY - origin.current.y);
    setSelection({ x, y, width, height });
    setMeasured({ width: width * pixelRatio, height: height * pixelRatio, dpi: pixelRatio * 96 });
  };
  const finishSelection = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!origin.current) return;
    updateSelection(event);
    const width = Math.abs(event.clientX - origin.current.x);
    const height = Math.abs(event.clientY - origin.current.y);
    origin.current = null;
    if (width < 4 || height < 4) {
      setSelection(null);
      setMeasured(null);
      return;
    }
    send('screen-ruler.complete', {
      widthPixels: width * pixelRatio,
      heightPixels: height * pixelRatio,
      dpiX: pixelRatio * 96,
      dpiY: pixelRatio * 96,
    });
  };
  const keyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    send('screen-ruler.cancel');
  };
  let measurement = '';
  if (measured) {
    const format = (value: number) => new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 }).format(value);
    if (state.unit === 'Santimetre') measurement = `${format(measured.width / measured.dpi * 2.54)} × ${format(measured.height / measured.dpi * 2.54)} cm`;
    else if (state.unit === 'İnç') measurement = `${format(measured.width / measured.dpi)} × ${format(measured.height / measured.dpi)} in`;
    else measurement = `${Math.round(measured.width)} × ${Math.round(measured.height)} px`;
  }
  useEffect(() => { root.current?.focus(); }, []);
  return <div ref={root} className="feature-root screen-ruler-overlay" tabIndex={0} onKeyDown={keyDown} onContextMenu={event => { event.preventDefault(); send('screen-ruler.cancel'); }}
    onPointerDown={event => { if (event.button !== 0) return; origin.current = { x: event.clientX, y: event.clientY }; setSelection({ x: event.clientX, y: event.clientY, width: 0, height: 0 }); setMeasured(null); event.currentTarget.setPointerCapture(event.pointerId); }}
    onPointerMove={updateSelection} onPointerUp={finishSelection} onPointerCancel={() => { origin.current = null; setSelection(null); setMeasured(null); }}>
    {!selection && <div className="screen-ruler-hint"><strong>Ekran cetveli</strong><span>Ölçmek için sürükleyin</span><kbd>ESC</kbd><small>Sağ tık · İptal</small></div>}
    {selection && <div className="screen-ruler-selection" style={{ left: selection.x, top: selection.y, width: selection.width, height: selection.height }} />}
    {selection && measured && <output className="screen-ruler-measurement" style={{ left: selection.x, top: Math.max(12, selection.y - 42) }}>{measurement}</output>}
  </div>;
}

function ScreenColorPickerView() {
  const root = useRef<HTMLDivElement>(null);
  const send = (command: string) => window.chrome?.webview?.postMessage({ command, payload: {} });
  useEffect(() => { root.current?.focus(); }, []);
  return <div ref={root} className="feature-root screen-color-picker-overlay" tabIndex={0}
    onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); send('screen-color-picker.cancel'); } }}
    onContextMenu={event => { event.preventDefault(); send('screen-color-picker.cancel'); }}
    onPointerDown={event => { if (event.button !== 0) return; event.currentTarget.setPointerCapture(event.pointerId); send('screen-color-picker.begin'); }}
    onPointerUp={event => { if (event.button === 0) send('screen-color-picker.commit'); }}
    onPointerCancel={() => send('screen-color-picker.cancel')}>
    <div className="screen-color-picker-hint"><span className="screen-color-picker-swatch" /><div><strong>Renk seçici</strong><span>Rengi almak için ekrana tıklayın</span></div><kbd>ESC</kbd><small>Sağ tık · İptal</small></div>
  </div>;
}

export function FeatureApp({ view }: { view: string }) {
  const [state, setState] = useState<Record<string, unknown> | null>(null);
  useEffect(() => {
    document.body.classList.toggle('island-tool-theme', islandToolViews.has(view));
    document.body.dataset.toolAppearance = typeof state?.appearance === 'string' ? state.appearance : 'dark';
    return () => { document.body.classList.remove('island-tool-theme'); delete document.body.dataset.toolAppearance; };
  }, [view, state?.appearance]);
  const [seekPosition, setSeekPosition] = useState<number | null>(null);
  const mediaCardRef = useRef<HTMLElement>(null);
  const mediaReady = view === 'media-flyout' && state !== null;

  useEffect(() => {
    const card = mediaCardRef.current;
    if (!mediaReady || !card) return;
    let previousHeight = 0;
    const observer = new ResizeObserver(() => {
      const height = Math.ceil(card.getBoundingClientRect().height);
      if (height === previousHeight) return;
      previousHeight = height;
      window.chrome?.webview?.postMessage({ command: 'media.resize', payload: { height } });
    });
    observer.observe(card);
    return () => observer.disconnect();
  }, [mediaReady]);
  const [widgetMenuOpen, setWidgetMenuOpen] = useState(false);
  const [historyDraft, setHistoryDraft] = useState<{ search: string; app: string; tag: string; from: string; until: string } | null>(null);
  const [historyTagsDraft, setHistoryTagsDraft] = useState<string | null>(null);
  const [historyPreviewOpen, setHistoryPreviewOpen] = useState(false);
  const [statisticsTab, setStatisticsTab] = useState('applications');
  const launcherInput = useRef<HTMLInputElement>(null);
  const launcherState = state as unknown as LauncherState | null;
  const [activeKeyboardTab, setActiveKeyboardTab] = useState<KeyboardEditorField>('keys');
  const captureRef = useRef<HTMLDivElement>(null);
  const keyboardEditorState = state as unknown as KeyboardEditorState | null;

  useEffect(() => {
    if (view === 'launcher' && launcherState) {
      launcherInput.current?.focus();
      launcherInput.current?.select();
    }
  }, [view, launcherState?.focusGeneration]);

  useEffect(() => {
    if (view === 'keyboard-mapping-editor' && keyboardEditorState?.captureTarget) captureRef.current?.focus();
  }, [view, keyboardEditorState?.captureTarget]);

  useEffect(() => {
    const bridge = window.chrome?.webview;
    if (!bridge) {
      if (view === 'default-paths' || view === 'default-apps') return;
      if (import.meta.env.DEV) {
        const preview = view === 'dynamic-island' ? previewIsland : view === 'net-speed-overlay'
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

  useEffect(() => {
    const beginDrag = (event: PointerEvent) => {
      if (event.button !== 0 || !(event.target instanceof Element)) return;
      const header = event.target.closest('.feature-root header, .defaults-panel > header');
      if (!header || event.target.closest('button,input,select,textarea,a,[role="button"]')) return;
      window.chrome?.webview?.postMessage({ command: 'feature.drag', payload: {} });
    };
    document.addEventListener('pointerdown', beginDrag, true);
    return () => document.removeEventListener('pointerdown', beginDrag, true);
  }, []);

  const send = (command: string, payload: Record<string, unknown> = {}) =>
    window.chrome?.webview?.postMessage({ command, payload });

  if (view === 'dynamic-island') return <DynamicIsland state={state as unknown as IslandState | null} />;
  if (view === 'automation-active') {
    const automation = state as unknown as { active: { id: string; name: string; shortcut: string }[] } | null;
    return <div className="feature-root automation-active-overlay" role="status" aria-live="polite">
      {automation?.active.map(item => <div className="automation-active-item" key={item.id}>
        <span className="automation-active-dot" aria-hidden="true" />
        <div><strong>{item.shortcut} aktif</strong><small>{item.name}</small></div>
      </div>)}
    </div>;
  }
  if (view === 'default-paths') return <DefaultPathsPanel state={state} />;
  if (view === 'default-apps') return <DefaultAppsPanel state={state} />;

  if (view === 'app-dialog') {
    const dialog = state as unknown as AppDialogState | null;
    return dialog ? <div className="feature-root feature-root-app-dialog" onKeyDown={event => { if (event.key === 'Escape') send('dialog.resolve', { confirmed: false }); }}><section role="dialog" aria-modal="true" aria-labelledby="app-dialog-title" aria-describedby="app-dialog-message">
      <div className="app-dialog-mark" aria-hidden="true">{dialog.isConfirmation ? '?' : 'i'}</div>
      <div className="app-dialog-copy"><h1 id="app-dialog-title">{dialog.title}</h1><p id="app-dialog-message">{dialog.message}</p></div>
      <footer>{dialog.isConfirmation && <button onClick={() => send('dialog.resolve', { confirmed: false })}>{dialog.cancelLabel}</button>}<button className="app-dialog-primary" autoFocus onClick={() => send('dialog.resolve', { confirmed: true })}>{dialog.confirmLabel}</button></footer>
    </section></div> : <div className="feature-root feature-root-error" />;
  }

  if (view === 'nashi-file-preview') {
    const preview = state as unknown as FilePreviewState | null;
    return preview ? <NashiFilePreviewView state={preview} /> : <div className="feature-root feature-root-error">Önizleme hazırlanıyor…</div>;
  }

  if (view === 'nashi-float-shelf') {
    const shelf = state as unknown as FloatShelfState | null;
    return shelf ? <FloatShelfView state={shelf} /> : <div className="feature-root feature-root-error">Float is getting ready…</div>;
  }

  if (view === 'nashi-wheel') {
    const wheel = state as unknown as WheelState | null;
    return wheel ? <WheelView state={wheel} /> : <div className="feature-root feature-root-error">Menu is getting ready…</div>;
  }

  if (view === 'nashi-tool-editor') {
    const editor = state as unknown as ToolEditorState | null;
    return editor ? <ToolEditorView state={editor} /> : <div className="feature-root feature-root-error">Düzenleyici hazırlanıyor…</div>;
  }

  if (view === 'nashi-progress') {
    const progress = state as unknown as ProgressState | null;
    return progress ? <ProgressView state={progress} /> : <div className="feature-root feature-root-error">İşlem başlatılıyor…</div>;
  }

  if (view === 'nashi-qr-results') {
    const qrResults = state as unknown as QrResultsState | null;
    return qrResults ? <QrResultsView state={qrResults} /> : <div className="feature-root feature-root-error">QR sonuçları hazırlanıyor…</div>;
  }

  if (view === 'clipboard-shelf') {
    const clipboard = state as unknown as ClipboardShelfState | null;
    return clipboard ? <ClipboardShelfView state={clipboard} /> : <div className="feature-root feature-root-error">Pano rafı hazırlanıyor…</div>;
  }

  if (view === 'clipboard-settings') {
    const settings = state as unknown as ClipboardSettingsState | null;
    return settings ? <ClipboardSettingsView state={settings} /> : <div className="feature-root feature-root-error">Pano ayarları hazırlanıyor…</div>;
  }

  if (view === 'clipboard-prompt') {
    const prompt = state as unknown as ClipboardPromptState | null;
    return prompt ? <ClipboardPromptView state={prompt} /> : <div className="feature-root feature-root-error">Pano girişi hazırlanıyor…</div>;
  }

  if (view === 'clipboard-preview') {
    const preview = state as unknown as ClipboardPreviewState | null;
    return preview ? <ClipboardPreviewView state={preview} /> : <div className="feature-root feature-root-error">Önizleme hazırlanıyor…</div>;
  }

  if (view === 'clipboard-recording-trim') {
    const trim = state as unknown as RecordingTrimState | null;
    return trim ? <RecordingTrimView state={trim} /> : <div className="feature-root feature-root-error">Kayıt hazırlanıyor…</div>;
  }

  if (view === 'nashi-pdf-organizer') {
    const organizer = state as unknown as PdfOrganizerState | null;
    return organizer ? <PdfOrganizerView state={organizer} /> : <div className="feature-root feature-root-error">PDF sayfaları hazırlanıyor…</div>;
  }

  if (view === 'nashi-metadata-editor') {
    const metadata = state as unknown as MetadataEditorState | null;
    return metadata ? <MetadataEditorView state={metadata} /> : <div className="feature-root feature-root-error">Üst veriler yükleniyor…</div>;
  }

  if (view === 'nashi-setup') {
    const setup = state as unknown as NashiSetupState | null;
    return setup ? <NashiSetupView state={setup} /> : <div className="feature-root feature-root-error">Kurulum durumu yükleniyor…</div>;
  }

  if (view === 'nashi-settings') {
    const settings = state as unknown as NashiSettingsState | null;
    return settings ? <NashiSettingsView state={settings} /> : <div className="feature-root feature-root-error">Ayarlar yükleniyor…</div>;
  }

  if (view === 'nashi') {
    const nashi = state as unknown as NashiState | null;
    return nashi ? <NashiView state={nashi} /> : <div className="feature-root feature-root-error">Nashi yükleniyor…</div>;
  }

  if (view === 'capture-editor') {
    const editor = state as unknown as CaptureEditorState | null;
    return editor ? <CaptureEditorView state={editor} /> : <div className="feature-root feature-root-error">Görüntü yükleniyor…</div>;
  }

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
        <footer><span>{environment.status}</span><button onClick={() => send('environment.add')}>Değişken ekle</button><button onClick={() => send('environment.reload')}>Yenile</button><span>Değişiklikler otomatik kaydedilir.</span></footer>
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
        <footer><span>{profiles.status}</span><button onClick={() => send('profiles.variable-add')}>Değişken ekle</button><span>Değişiklikler otomatik kaydedilir.</span></footer>
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

  if (view === 'hosts-file-editor') {
    const hosts = state as unknown as HostsState | null;
    const filteredRows = hosts?.rows.filter(row => `${row.address} ${row.hostNames} ${row.comment}`.toLowerCase().includes(hosts.filter.trim().toLowerCase())) ?? [];
    return <div className="feature-root feature-root-hosts">{hosts && <section className="hosts-panel">
      <header><div><strong>Hosts dosyası</strong><small>Etkin girdiler Windows ad çözümlemesini değiştirir. Kaydederken yedek alınır ve yönetici onayı istenir.</small></div><button onClick={() => send('hosts.close')} aria-label="Kapat">×</button></header>
      <div className="hosts-toolbar"><input value={hosts.filter} onChange={event => send('hosts.filter', { query: event.target.value })} placeholder="IP, ana bilgisayar adı veya yorumda ara" aria-label="Hosts girdilerinde ara" /><button onClick={() => send('hosts.reload')}>Yeniden yükle</button></div>
      <div className="hosts-table"><div className="hosts-table-heading"><span>IP adresi</span><span>Ana bilgisayar adları</span><span>Yorum</span><span>Durum</span></div>{filteredRows.map(row => <button key={row.lineIndex} className={`hosts-row ${hosts.selectedLine === row.lineIndex ? 'selected' : ''}`} onClick={() => send('hosts.select', { lineIndex: row.lineIndex })}><span>{row.address}</span><span>{row.hostNames}</span><span>{row.comment || '—'}</span><span className={row.isActive ? 'active' : 'inactive'}>{row.isActive ? 'Etkin' : 'Kapalı'}</span></button>)}{filteredRows.length === 0 && <div className="history-empty">Eşleşen hosts girdisi yok.</div>}</div>
      <footer><span role="status">{hosts.status}{hosts.dirty ? ' · Kaydedilmemiş değişiklikler' : ''}</span><div><button onClick={() => send('hosts.add')}>Yeni girdi</button><button disabled={!hosts.canEdit} onClick={() => send('hosts.edit')}>Düzenle</button><button disabled={!hosts.canEdit} onClick={() => send('hosts.toggle')}>Etkinleştir / devre dışı bırak</button><button disabled={!hosts.canEdit} onClick={() => send('hosts.remove')}>Sil</button><span>Değişiklikler otomatik kaydedilir.</span></div></footer>
      {hosts.entry && <div className="history-confirm-backdrop"><section className="hosts-entry-dialog"><header><div><strong>{hosts.entry.lineIndex < 0 ? 'Yeni hosts girdisi' : 'Hosts girdisini düzenle'}</strong><small>IP adresi ve bir veya daha fazla ana bilgisayar adı girin.</small></div></header><label>IP adresi<input value={hosts.entry.address} onChange={event => send('hosts.entry-update', { address: event.target.value })} /></label><label>Ana bilgisayar adları<input value={hosts.entry.hostNames} onChange={event => send('hosts.entry-update', { hostNames: event.target.value })} /></label><label>Yorum<input value={hosts.entry.comment} onChange={event => send('hosts.entry-update', { comment: event.target.value })} /></label><label className="hosts-entry-enabled"><input type="checkbox" checked={hosts.entry.isActive} onChange={event => send('hosts.entry-update', { isActive: event.target.checked })} /> Etkin</label>{hosts.entry.error && <p role="alert">{hosts.entry.error}</p>}<div><button onClick={() => send('hosts.entry-cancel')}>Vazgeç</button><button className="primary" onClick={() => send('hosts.entry-save')}>Uygula</button></div></section></div>}
      {hosts.confirmRemove && <div className="history-confirm-backdrop"><section className="history-confirm"><strong>Hosts girdisi silinsin mi?</strong><p>Seçili eşleme kaydedilene kadar bellekteki taslaktan kaldırılır.</p><div><button onClick={() => send('hosts.remove-confirm', { confirm: false })}>Vazgeç</button><button className="danger" onClick={() => send('hosts.remove-confirm', { confirm: true })}>Sil</button></div></section></div>}
      {hosts.confirmDiscard && <div className="history-confirm-backdrop"><section className="history-confirm"><strong>Kaydedilmemiş değişiklikler var</strong><p>Devam ederseniz düzenlemeler kaybolur.</p><div><button onClick={() => send('hosts.discard-confirm', { confirm: false })}>Düzenlemeye dön</button><button className="danger" onClick={() => send('hosts.discard-confirm', { confirm: true })}>Değişiklikleri sil</button></div></section></div>}
    </section>}</div>;
  }

  if (view === 'capture-image-preview') {
    const preview = state as unknown as CaptureImagePreviewState | null;
    return <div className="feature-root feature-root-image-preview" onKeyDown={event => { if (event.key === 'Escape') send('capture-image-preview.close'); }} tabIndex={0} autoFocus>
      {preview && <><header><strong>{preview.pinned ? 'Sabit görüntü' : 'Görsel önizlemesi'}</strong><button onClick={() => send('capture-image-preview.close')} aria-label="Kapat">×</button></header><div className="capture-image-preview-frame"><img src={preview.image} alt="Ekran görüntüsü önizlemesi" /></div></>}
    </div>;
  }

  if (view === 'keyboard-mapping-editor') {
    const editor = keyboardEditorState;
    const captureTarget = editor?.captureTarget;
    const tabs = [
      ['keys', 'Tuş eşlemeleri', 'Her satırda kaynak tuş = gönderilecek tuş. Örnek: A = B'],
      ['shortcuts', 'Kısayollar', 'Her satırda kaynak kısayol = gönderilecek kısayol. Örnek: Ctrl+C = Ctrl+Insert'],
      ['applications', 'Uygulamaya özel', 'Her satırda işlem.exe: kaynak = hedef. Örnek: msedge.exe: Ctrl+F = Ctrl+H'],
      ['text', 'Metin dizileri', 'Tuş veya kısayol = yazdırılacak metin. Örnek: H = Hello!'],
      ['actions', 'Uygulama veya URI aç', 'Örnek: Ctrl+Alt+W = uri|https://example.com veya Ctrl+Alt+N = app|notepad.exe']
    ] as const;
    const currentTab = tabs.find(([key]) => key === activeKeyboardTab) ?? tabs[0];
    const handleCaptureKey = (event: React.KeyboardEvent<HTMLDivElement>) => {
      if (!captureTarget) return;
      if (['Control', 'Alt', 'Shift', 'Meta'].includes(event.key) || (event.key === 'Tab' && !event.ctrlKey)) { event.preventDefault(); return; }
      event.preventDefault();
      event.stopPropagation();
      send('keyboard-editor.capture-key', { key: event.key, ctrlKey: event.ctrlKey, altKey: event.altKey, shiftKey: event.shiftKey, metaKey: event.metaKey });
    };
    return <div className="feature-root feature-root-keyboard-editor">{editor && <section className="keyboard-editor-panel">
      <header><div><strong>Klavye eşlemeleri</strong><small>Tuşları, kısayolları, metin dizilerini ve uygulama eylemlerini yönetin.</small></div><button onClick={() => send('keyboard-editor.cancel')} aria-label="Kapat">×</button></header>
      <nav className="keyboard-editor-tabs">{tabs.map(([key, title]) => <button key={key} className={activeKeyboardTab === key ? 'active' : ''} onClick={() => setActiveKeyboardTab(key)}>{title}</button>)}</nav>
      <div className="keyboard-editor-body" ref={captureRef} tabIndex={editor.captureTarget ? 0 : -1} onKeyDown={handleCaptureKey}>
        <p>{currentTab[2]}</p>
        <div className="keyboard-capture-row"><button onClick={event => { const textarea = event.currentTarget.closest('.keyboard-editor-body')?.querySelector('textarea'); send('keyboard-editor.capture-start', { field: activeKeyboardTab, selectionStart: textarea?.selectionStart ?? 0, selectionLength: (textarea?.selectionEnd ?? 0) - (textarea?.selectionStart ?? 0) }); }} title="Tuş veya kısayola basın, Enter ile ekleyin">Ata</button><span>{editor.captureTarget ? editor.captureStatus : 'İmleci tuşun yazılacağı yere koyup Ata’ya basın.'}</span></div>
        <textarea spellCheck={false} value={editor[activeKeyboardTab]} onChange={event => send('keyboard-editor.update', { field: activeKeyboardTab, value: event.target.value })} aria-label={currentTab[1]} />
      </div>
      <footer><span role="alert">{editor.error}</span><div><button onClick={() => send('keyboard-editor.cancel')}>Kapat</button><span>Geçerli eşlemeler otomatik uygulanır.</span></div></footer>
    </section>}</div>;
  }

  if (view === 'display-brightness') {
    const display = state as unknown as DisplayBrightnessState | null;
    return <div className="feature-root feature-root-display-brightness">{display && <section className="display-panel">
      <header><div><strong>Power Display</strong><small>Ekran parlaklığı ve monitör denetimleri</small></div><button onClick={() => send('display.close')} aria-label="Kapat">×</button></header>
      <section className="display-profiles"><input value={display.profileName} maxLength={48} onChange={event => send('display.profile-name', { name: event.target.value })} placeholder="Profil adı" aria-label="Profil adı" /><select value={display.selectedProfileName} onChange={event => send('display.profile-select', { name: event.target.value })}><option value="">Profil seçin</option>{display.profiles.map(name => <option key={name} value={name}>{name}</option>)}</select><button onClick={() => send('display.profile-save')}>Profili kaydet</button><button disabled={!display.selectedProfileName} onClick={() => send('display.profile-apply')}>Uygula</button><button disabled={!display.selectedProfileName} onClick={() => send('display.profile-delete')}>Sil</button></section>
      <div className="display-toolbar"><label><input type="checkbox" checked={display.linkLevels} onChange={event => send('display.link-levels', { enabled: event.target.checked })} /> Parlaklık seviyelerini bağla</label><button onClick={() => send('display.refresh')}>Ekranları yenile</button></div>
      <div className="display-status" role="status">{display.status}</div>
      <div className="display-monitor-list">{display.monitors.map(monitor => <article className="display-monitor" key={monitor.id}><header><div><strong>{monitor.name}</strong><small>{monitor.isInternalPanel ? 'Dahili panel · Windows WMI' : 'Harici ekran · DDC/CI'}</small></div></header><div className="display-controls">{monitor.controls.map(control => <label className={`display-control display-control-${control.type}`} key={`${monitor.id}-${control.code}`}><span>{control.label}{control.type === 'slider' && <b>{control.value}%</b>}</span>{control.type === 'slider' ? <input type="range" min="0" max="100" value={Number(control.value)} onChange={event => send('display.slider', { id: monitor.id, code: control.code, value: Number(event.target.value) })} aria-label={`${monitor.name} ${control.label}`} /> : control.type === 'select' ? <select value={String(control.value)} onChange={event => send('display.selector', { id: monitor.id, code: control.code, value: Number(event.target.value) })}>{control.options?.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}</select> : <small>{control.value}</small>}</label>)}</div></article>)}{display.monitors.length === 0 && <div className="history-empty">Uyumlu ekran bulunamadı veya henüz taranmadı.</div>}</div>
      {display.confirmColorPreset && <div className="history-confirm-backdrop"><section className="history-confirm"><strong>Renk ön ayarı değiştirilsin mi?</strong><p>{display.confirmColorPreset.monitorName} ekranındaki renk ön ayarını değiştirmek görüntüyü beklenmedik biçimde etkileyebilir.{display.confirmColorPreset.label ? ` Seçim: 0x${display.confirmColorPreset.label}.` : ''}</p><div><button onClick={() => send('display.color-confirm', { confirm: false })}>Vazgeç</button><button className="primary" onClick={() => send('display.color-confirm', { confirm: true })}>Değiştir</button></div></section></div>}
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

  if (view === 'capture-recording') {
    const recording = state as unknown as CaptureRecordingState | null;
    return recording ? <div className="feature-root feature-root-recording"><section className="recording-panel">
      <span className={`recording-indicator ${recording.paused ? 'paused' : ''}`} />
      <div className="recording-copy"><strong>{recording.gif ? 'GIF kaydı' : 'Ekran kaydı'}</strong><small>{recording.status}</small></div>
      <div className="recording-actions"><button disabled={recording.busy} onClick={() => send('capture-recording.pause')}>{recording.paused ? 'Sürdür' : 'Duraklat'}</button><button className="recording-finish" disabled={recording.busy} onClick={() => send('capture-recording.finish')}>{recording.busy ? 'Kaydediliyor…' : 'Bitir ve kaydet'}</button><button disabled={recording.busy} onClick={() => send('capture-recording.cancel')}>İptal</button></div>
    </section></div> : <div className="feature-root feature-root-error" />;
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
            {history.preview ? <><button className="history-preview-button" onClick={() => setHistoryPreviewOpen(true)} title="Büyük önizleme"><span className="history-preview-frame"><img className="history-preview" src={history.preview} alt="Seçili kayıt önizlemesi" />{history.cursor && <i className="history-cursor-marker" style={{ left: `${history.cursor.x / history.cursor.width * 100}%`, top: `${history.cursor.y / history.cursor.height * 100}%` }} />}</span><span>Büyüt</span></button>{history.kind === 'recall' && <input className="history-selected-tags" value={historyTagsDraft ?? history.selectedTags} onChange={event => { setHistoryTagsDraft(event.target.value); send('history.save-tags', { tags: event.target.value }); }} placeholder="Seçili uygulama etiketleri" />}</> : <div className="history-preview-empty">{history.mediaType ? `${history.mediaType.replace('.', '').toUpperCase()} kaydı` : 'Bir kayıt seçin'}</div>}
            <textarea readOnly value={history.selectedText} aria-label="Kayıt metni" />

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
    return <div className="feature-root feature-root-paste" tabIndex={0} autoFocus onKeyDown={event => { if (event.key === 'Escape') send('paste.cancel'); }}>
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

  if (view === 'dictation-status') {
    const dictation = state as unknown as DictationStatusState | null;
    return dictation ? <DictationStatusView state={dictation} /> : <div className="feature-root feature-root-error">Dictation is getting ready…</div>;
  }

  if (view === 'tiling-zone-preview') {
    const zones = state as unknown as ZonePreviewState | null;
    return zones ? <ZonePreviewView state={zones} /> : <div className="feature-root feature-root-error" />;
  }

  if (view === 'crop-thumbnail') {
    const thumbnail = state as unknown as CropThumbnailState | null;
    return thumbnail ? <CropThumbnailView state={thumbnail} /> : <div className="feature-root feature-root-error" />;
  }

  if (view === 'crop-selection') {
    const selection = state as unknown as CropSelectionState | null;
    return selection ? <CropSelectionView state={selection} /> : <div className="feature-root feature-root-error" />;
  }

  if (view === 'screen-text-selection') {
    const selection = state as unknown as TextSelectionState | null;
    return selection ? <TextSelectionView state={selection} /> : <div className="feature-root feature-root-error" />;
  }

  if (view === 'screen-ruler') {
    const ruler = state as unknown as ScreenRulerState | null;
    return ruler ? <ScreenRulerView state={ruler} /> : <div className="feature-root screen-ruler-overlay" />;
  }

  if (view === 'screen-color-picker') return <ScreenColorPickerView />;

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
        <button className="widget-track" onClick={() => send('widget.open-flyout')} title={widget.tooltip || widget.title}><span className="widget-player-mark">♫</span><span><small>{widget.player}</small><strong>{widget.title}</strong></span></button>
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
      {network && (network.showDownload || network.showUpload) && <div className={`network-speed-display ${network.layout === 'Alt alta' ? 'network-speed-stacked' : ''}`} style={{ fontSize: network.fontSize }}>
        {network.showDownload && <span className="network-speed-value download-value">{network.download}</span>}
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
    return <OsdControls kind={osd.kind} percent={osd.percent ?? 0} isMuted={osd.isMuted} send={send} />;
  }

  if (view === 'media-flyout') {
    const media = state as unknown as MediaFlyoutState | null;
    if (!media) return <div className="feature-root feature-root-transparent" />;
    const repeatLabel = media.repeatMode === 'track' ? 'Bu parça' : media.repeatMode === 'list' ? 'Tüm parçalar' : 'Kapalı';
    return <div className="feature-root feature-root-media">
      <article ref={mediaCardRef} className="media-flyout-card" onMouseEnter={() => send('media.hover', { value: true })} onMouseLeave={() => send('media.hover', { value: false })}>
        <header className="media-source-row">
          <button className={`media-source ${media.sourceType.toLowerCase()}`} onClick={() => send('media.open-source')}>
            <span className="media-source-icon">{media.sourceType === 'Spotify' ? 'S' : media.sourceType === 'YouTube' ? '▶' : '♫'}</span>
            <span>{media.sourceName || 'Medya'}</span><small>{media.sourceBadge}</small>
          </button>
          <button className={`media-pin ${media.pinned ? 'active' : ''}`} onClick={() => send('media.pin', { value: !media.pinned })} aria-label={media.pinned ? 'Panel sabitlemesini kaldır' : 'Paneli sabitle'} title={media.pinned ? 'Sabitlemeyi kaldır' : 'Sabitle'}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m16 3 5 5-4 1-3 4 2 2-1 1-4-4-6 8 1-10-4-4 1-1 2 2 4-3 1-4Z" /></svg>
          </button>
        </header>
        <div className="media-track-row">
          <div className="media-artwork">{media.artwork ? <img src={media.artwork} alt="Albüm kapağı" /> : <span>♫</span>}</div>
          <div className="media-track-copy"><strong title={media.title}>{media.title}</strong><span title={media.artist}>{media.artist || 'Bilinmeyen sanatçı'}</span></div>
        </div>
        {media.showSessionSelector && <select className="media-session-select" value={media.selectedSessionId} onChange={event => send('media.select-session', { id: event.target.value })} aria-label="Medya oturumu seç">
          {media.sessions.map(session => <option key={session.id} value={session.id}>{session.label}</option>)}
        </select>}
        <div className="media-timeline">
          <input type="range" min="0" max={Math.max(1, media.duration)} value={Math.min(seekPosition ?? media.position, media.duration || 1)} onChange={event => setSeekPosition(Number(event.target.value))} onPointerUp={() => { if (seekPosition !== null) send('media.seek', { position: seekPosition }); setSeekPosition(null); }} onKeyUp={event => { if (event.key.startsWith('Arrow') && seekPosition !== null) { send('media.seek', { position: seekPosition }); setSeekPosition(null); } }} aria-label="Parçada ilerle" />
          <div><span>{media.currentTime}</span><span>{media.totalTime}</span></div>
        </div>
        <footer className="media-controls">
          <button className={`media-option ${media.shuffleEnabled ? 'active' : ''}`} disabled={!media.canShuffle} onClick={() => send('media.shuffle')} aria-label="Karıştır" title="Karıştır"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m16 3 4 4-4 4M4 7h2.5a4 4 0 0 1 3.2 1.6l4.6 6.8a4 4 0 0 0 3.2 1.6H20M16 13l4 4-4 4M4 17h2.5a4 4 0 0 0 2.7-1" /></svg></button>
          <button className="media-skip" disabled={!media.canPrevious} onClick={() => send('media.previous')} aria-label="Önceki parça" title="Önceki parça"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 5h2v14H6zm3 7 10-7v14z" /></svg></button>
          <button className="media-play" disabled={!media.canPlay} onClick={() => send('media.toggle')} aria-label={media.isPlaying ? 'Duraklat' : 'Oynat'} title={media.isPlaying ? 'Duraklat' : 'Oynat'}>
            {media.isPlaying ? <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 5h4v14H7zm6 0h4v14h-4z" /></svg> : <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m7 4 13 8-13 8z" /></svg>}
          </button>
          <button className="media-skip" disabled={!media.canNext} onClick={() => send('media.next')} aria-label="Sonraki parça" title="Sonraki parça"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M16 5h2v14h-2zm-1 7L5 5v14z" /></svg></button>
          <button className={`media-option ${media.repeatMode !== 'off' ? 'active' : ''}`} disabled={!media.canRepeat} onClick={() => send('media.repeat')} aria-label={`Tekrar: ${repeatLabel}`} title={`Tekrar: ${repeatLabel}`}><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="m17 2 4 4-4 4M3 11V9a3 3 0 0 1 3-3h15M7 22l-4-4 4-4m14 3v2a3 3 0 0 1-3 3H3" />{media.repeatMode === 'track' && <path d="M11 10h2v5m-2 0h4" />}</svg></button>
        </footer>
      </article>
    </div>;
  }

  return <div className="feature-root feature-root-error">GÃ¶rÃ¼nÃ¼m bulunamadÄ±</div>;
}
