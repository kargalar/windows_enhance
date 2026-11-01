import { useEffect, useRef } from 'react';
import './default-settings.css';

type Send = (command: string, payload?: Record<string, unknown>) => void;
type PathsState = {
  status: string;
  rows: { key: string; name: string; current: string; original: string; draft: string; error: string }[];
  pending: { name: string; from: string; target: string; restore: boolean } | null;
};
type AppsState = {
  status: string; query: string; count: number;
  rows: { extension: string; program: string; executable: string }[];
};

const send: Send = (command, payload = {}) => window.chrome?.webview?.postMessage({ command, payload });

export function DefaultPathsPanel({ state, sendCommand = send, inline = false }: { state: Record<string, unknown> | null; sendCommand?: Send; inline?: boolean }) {
  const send = sendCommand;
  const paths = state as PathsState | null;
  const confirmation = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    if (paths?.pending && confirmation.current && !confirmation.current.open) confirmation.current.showModal();
  }, [paths?.pending]);
  return <section className="defaults-panel">
    {!inline && <header><div><h1>Varsayılan Yollar</h1><p>Kullanıcı klasörlerinizin yeni kayıt konumlarını belirleyin.</p></div>{!inline && <button onClick={() => send('defaults.close')}>Kapat</button>}</header>}
    {!inline && <p className="defaults-note">Mevcut dosyalar taşınmaz. Değişiklikler kalıcıdır; modülü kapatmak geri almaz. OneDrive veya kurum yönlendirmesi kullanıyorsanız önce ilgili ayarları kontrol edin.</p>}
    <section className="defaults-list" aria-label="Kullanıcı klasörleri">
      {!paths && <p>Konumlar okunuyor…</p>}
      {paths?.rows.map(row => <article className="defaults-folder" key={row.key}>
        <h2>{row.name}</h2><p title={row.current}>Şu an: {row.current || 'Okunamadı'}</p>
        {row.error ? <p role="alert">{row.error}</p> : <>
          <div className="defaults-path-edit"><input aria-label={`${row.name} yeni konumu`} value={row.draft} onChange={event => send('paths.edit', { key: row.key, path: event.target.value })} /><button onClick={() => send('paths.browse', { key: row.key })}>Gözat…</button><button className="primary" disabled={!row.draft.trim() || row.draft === row.current} onClick={() => send('paths.apply', { key: row.key })}>Uygula</button></div>
          {row.original && <div className="defaults-original"><small title={row.original}>İlk değişiklikten önce: {row.original}</small><button disabled={row.original === row.current} onClick={() => send('paths.restore', { key: row.key })}>Önceki konuma dön</button></div>}
        </>}
      </article>)}
      <article className="defaults-folder"><h2>Yeni uygulamaların kurulum konumu</h2><p>Windows’un desteklediği yeni uygulama ve içerik sürücüsünü Depolama ayarlarında seçin. Klasik masaüstü programlarının kurulum klasörü kendi yükleyicilerinden belirlenir.</p><button onClick={() => send('paths.storage')}>Yeni içeriklerin kaydedileceği yeri aç</button></article>
    </section>
    <footer><span role="status">{paths?.status}</span><button onClick={() => send('defaults.reload')}>Yenile</button></footer>
    {paths?.pending && <dialog ref={confirmation} className="defaults-confirm" aria-labelledby="paths-confirm-title" onCancel={event => { event.preventDefault(); send('paths.cancel'); }}><h2 id="paths-confirm-title">{paths.pending.name} konumu değişsin mi?</h2><p>Önceki: {paths.pending.from}</p><p>Yeni: {paths.pending.target}</p><p>Mevcut dosyalar eski konumda kalacak. Hedef klasörün mevcut olması gerekir. Açık uygulamaları yeniden başlatmanız gerekebilir.</p><div><button autoFocus onClick={() => send('paths.cancel')}>Vazgeç</button><button className="primary" onClick={() => send('paths.confirm')}>Konumu değiştir</button></div></dialog>}
  </section>;
}

export function DefaultAppsPanel({ state, sendCommand = send, inline = false }: { state: Record<string, unknown> | null; sendCommand?: Send; inline?: boolean }) {
  const send = sendCommand;
  const apps = state as AppsState | null;
  return <section className="defaults-panel">
    {!inline && <header><div><h1>Varsayılan Programlar</h1><p>Dosya türlerinin hangi uygulamayla açıldığını yönetin.</p></div>{!inline && <button onClick={() => send('defaults.close')}>Kapat</button>}</header>}
    <p className="defaults-note">Program seçimi Windows Ayarları’nda tamamlanır. “Kopyala ve program seç” uzantıyı panoya kopyalar ve bu ekranı açar. Uzantıyı dosya türü aramasına yapıştırın, uygulamayı seçip varsayılan olarak ayarlayın; ardından buradan Yenile’ye basın.</p>
    <div className="defaults-toolbar"><input type="search" aria-label="Dosya uzantısı ara" placeholder="Uzantı ara veya ekle: .pdf, .txt, .jpg…" value={apps?.query ?? ''} onChange={event => send('apps.filter', { query: event.target.value })} /><button onClick={() => send('apps.settings')}>Windows varsayılan uygulamaları</button></div>
    <small>{apps ? `${apps.count} dosya türü · İlk ${apps.rows.length} sonuç gösteriliyor. Listeyi aramayla daraltın.` : 'Eşleştirmeler okunuyor…'}</small>
    <section className="defaults-list" aria-label="Dosya eşleştirmeleri">
      {apps?.rows.map(row => <article className="defaults-association" key={row.extension}><strong>{row.extension}</strong><div><span>{row.program || 'Varsayılan uygulama bulunamadı'}</span><small title={row.executable}>{row.executable || 'Windows Ayarları’ndan bir uygulama seçebilirsiniz.'}</small></div><button onClick={() => send('apps.choose', { extension: row.extension })}>Kopyala ve program seç</button></article>)}
      {apps?.rows.length === 0 && <p>Sonuç bulunamadı. .pdf gibi bir dosya uzantısı girin.</p>}
    </section>
    <footer><span role="status">{apps?.status}</span><button onClick={() => send('defaults.reload')}>Yenile</button></footer>
  </section>;
}
