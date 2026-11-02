import { useEffect, useRef } from 'react';
import './default-settings.css';
import { t } from '../localization';
import { copy } from '../moduleDesigns';
import { Icon } from '../Icon';

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

export function DefaultPathsPanel({ state, sendCommand = send, inline = false, filterQuery='' }: { state: Record<string, unknown> | null; sendCommand?: Send; inline?: boolean; filterQuery?:string }) {
  const send = sendCommand;
  const paths = state as PathsState | null;
  const confirmation = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    if (paths?.pending && confirmation.current && !confirmation.current.open) confirmation.current.showModal();
  }, [paths?.pending]);
  return <section className="defaults-panel paths-workspace">
    {!inline && <header><div><h1>{t('defaults.pathsTitle')}</h1><p>{t('defaults.pathsDesc')}</p></div>{!inline && <button onClick={() => send('defaults.close')}>{t('defaults.close')}</button>}</header>}
    {!inline && <p className="defaults-note">{t('defaults.note')}</p>}
    <section className="defaults-list" aria-label="User folders">
      {!paths && <p>{t('loading.generic')}</p>}
      {paths?.rows.filter(row=>`${row.name} ${row.current}`.toLocaleLowerCase().includes(filterQuery.toLocaleLowerCase())).map(row => <article className="defaults-folder" key={row.key}>
        <header className="folder-card-heading"><Icon name="folder" size={24}/><h2>{row.name}</h2></header><div className="folder-current"><small>{copy(['Geçerli konum','Current location'])}</small><p title={row.current}>{row.current || '-'}</p></div>
        {row.error ? <p role="alert">{row.error}</p> : <>
          <label className="folder-destination-label">{copy(['Yeni hedef','New destination'])}</label><div className="defaults-path-edit"><input aria-label={`${row.name} path`} value={row.draft} onChange={event => send('paths.edit', { key: row.key, path: event.target.value })} /><button onClick={() => send('paths.browse', { key: row.key })}>{t('defaults.browse')}</button><button className="primary" disabled={!row.draft.trim() || row.draft === row.current} onClick={() => send('paths.apply', { key: row.key })}>{t('defaults.apply')}</button></div>
          {row.original && <div className="defaults-original"><small title={row.original}>{copy(['Özgün konum','Original location'])}: {row.original}</small><button disabled={row.original === row.current} onClick={() => send('paths.restore', { key: row.key })}>{t('defaults.restore')}</button></div>}
        </>}
      </article>)}
      <article className="defaults-folder"><h2>{t('defaults.storageTitle')}</h2><p>{t('defaults.storageDesc')}</p><button onClick={() => send('paths.storage')}>{t('defaults.storageButton')}</button></article>
    </section>
    <footer><span role="status">{paths?.status}</span><button onClick={() => send('defaults.reload')}>{t('defaults.reload')}</button></footer>
    {paths?.pending && <dialog ref={confirmation} className="defaults-confirm" aria-labelledby="paths-confirm-title" onCancel={event => { event.preventDefault(); send('paths.cancel'); }}><h2 id="paths-confirm-title">{t('defaults.confirmTitle', undefined, { name: paths.pending.name })}</h2><p>{t('defaults.confirmFrom', undefined, { from: paths.pending.from })}</p><p>{t('defaults.confirmTo', undefined, { target: paths.pending.target })}</p><p>{t('defaults.confirmWarning')}</p><div><button autoFocus onClick={() => send('paths.cancel')}>{t('defaults.cancelButton')}</button><button className="primary" onClick={() => send('paths.confirm')}>{t('defaults.confirmButton')}</button></div></dialog>}
  </section>;
}

export function DefaultAppsPanel({ state, sendCommand = send, inline = false, filterQuery='' }: { state: Record<string, unknown> | null; sendCommand?: Send; inline?: boolean;filterQuery?:string }) {
  const send = sendCommand;
  const apps = state as AppsState | null;
  return <section className="defaults-panel apps-workspace">
    {!inline && <header><div><h1>{t('defaults.appsTitle')}</h1><p>{t('defaults.appsDesc')}</p></div>{!inline && <button onClick={() => send('defaults.close')}>{t('defaults.close')}</button>}</header>}
    <details className="workspace-help"><summary>{copy(['Varsayılan program nasıl değiştirilir?','How to change a default application'])}</summary><p className="defaults-note">{t('defaults.appsNote')}</p></details>
    <div className="defaults-toolbar"><input type="search" aria-label="Search extension" placeholder={t('defaults.searchExt')} value={apps?.query ?? ''} onChange={event => send('apps.filter', { query: event.target.value })} /><button onClick={() => send('apps.settings')}>{t('defaults.windowsApps')}</button></div>
    <small>{apps ? copy([`${apps.count} dosya türü · ${apps.rows.length} sonuç gösteriliyor`,`${apps.count} file types · showing ${apps.rows.length} results`]) : t('loading.generic')}</small>
    <section className="defaults-list" aria-label="File associations">
      {apps?.rows.filter(row=>`${row.extension} ${row.program}`.toLocaleLowerCase().includes(filterQuery.toLocaleLowerCase())).map(row => <article className="defaults-association" key={row.extension}><strong>{row.extension}</strong><div><span>{row.program || t('defaults.noAppFound')}</span><small title={row.executable}>{row.executable || ''}</small></div><button onClick={() => send('apps.choose', { extension: row.extension })}>{t('defaults.copyAndChoose')}</button></article>)}
      {apps?.rows.length === 0 && <p>{t('defaults.noResultsExt')}</p>}
    </section>
    <footer><span role="status">{apps?.status}</span><button onClick={() => send('defaults.reload')}>{t('defaults.reload')}</button></footer>
  </section>;
}
