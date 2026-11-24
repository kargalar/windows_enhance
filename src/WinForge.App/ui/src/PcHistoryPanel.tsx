import { useState } from 'react';
import './pc-history.css';

type Row = { id: number; app: string; title: string; created: string; tags: string };
type Total = { app: string; seconds: number; estimated: boolean };
type Day = Total & { date: string };
type Context = { date: string; app: string; type: string; context: string; seconds: number };
type Tag = { tag: string; seconds: number; applicationCount: number };
type Network = { date: string; received: number; sent: number };

const sections = [
  ['apps', 'Uygulamalar'],
  ['days', 'Günlük'],
  ['contexts', 'Siteler ve klasörler'],
  ['tags', 'Etiketler'],
  ['network', 'İnternet'],
] as const;
type Section = 'history' | typeof sections[number][0];

export type PcHistoryState = {
  kind: 'pc-history';
  search: string;
  app: string;
  tag: string;
  from: string;
  until: string;
  offset: number;
  section: Section;
  statisticsOffset: number;
  loading: boolean;
  error: string | null;
  summary: { activeSeconds: number; idleSeconds: number; captures: number; mostUsedApp: string } | null;
  rows: Row[];
  hasMore: boolean;
  selected: (Row & { text: string; media: string }) | null;
  preview: string | null;
  statistics: (Total | Day | Context | Tag | Network)[];
  statisticsTotal: number;
  maximumApplicationSeconds: number;
};

const historyPageSize = 200;
const statisticsPageSize = 100;
const duration = (seconds: number) => {
  const minutes = Math.floor(seconds / 60);
  return minutes >= 60 ? `${Math.floor(minutes / 60)} sa ${minutes % 60} dk` : `${minutes} dk`;
};
const bytes = (value: number) => {
  if (value < 1) return '0 B';
  const unit = Math.min(4, Math.floor(Math.log(value) / Math.log(1024)));
  return `${(value / 1024 ** unit).toFixed(unit ? 1 : 0)} ${['B', 'KB', 'MB', 'GB', 'TB'][unit]}`;
};

export function PcHistoryPanel({ state, command }: { state: PcHistoryState; command: (action: string, data?: Record<string, unknown>) => void }) {
  const [filters, setFilters] = useState({ search: state.search, app: state.app, tag: state.tag, from: state.from, until: state.until });
  const [confirm, setConfirm] = useState(false);
  const [bulkDelete, setBulkDelete] = useState('');
  const [zoom, setZoom] = useState(false);
  const [tagDraft, setTagDraft] = useState<string | null>(null);
  const { section, loading, error, summary, rows, selected, preview } = state;
  const history = section === 'history';

  const changeSection = (next: Section) => {
    setConfirm(false);
    setZoom(false);
    setTagDraft(null);
    command('section', { section: next });
  };
  const applyFilters = (values: typeof filters) => {
    setConfirm(false);
    setZoom(false);
    setTagDraft(null);
    command('filter', values);
  };

  return <div className="pc-history">
    <div className="pc-history-intro">
      <div><h3>Geçmişiniz, tek bakışta</h3><p>Ekran kayıtlarını inceleyin ve bilgisayarınızda geçirdiğiniz zamanı keşfedin.</p></div>
      <button className="pc-refresh" disabled={loading} onClick={() => command('refresh')}>↻ Yenile</button>
    </div>
    {summary && <div className="pc-metrics">
      <div><span>Aktif süre</span><strong>{duration(summary.activeSeconds)}</strong><small>Seçilen aralıkta</small></div>
      <div><span>Boşta geçen süre</span><strong>{duration(summary.idleSeconds)}</strong><small>Etkinlik algılanmadığında</small></div>
      <div><span>Ekran kaydı</span><strong>{summary.captures.toLocaleString('tr-TR')}</strong><small>Kaydedilen görüntü</small></div>
      <div><span>En çok kullanılan</span><strong title={summary.mostUsedApp}>{summary.mostUsedApp || '—'}</strong><small>Uygulama</small></div>
    </div>}
    <form className="pc-filters" onSubmit={event => {
      event.preventDefault();
      if (!filters.from || !filters.until || filters.from <= filters.until) applyFilters(filters);
    }}>
      <label>Başlangıç<input type="date" value={filters.from} onChange={event => setFilters({ ...filters, from: event.target.value })}/></label>
      <label>Bitiş<input type="date" min={filters.from || undefined} value={filters.until} onChange={event => setFilters({ ...filters, until: event.target.value })}/></label>
      <label className="pc-search">Metin veya pencere<input type="search" placeholder="Geçmişte ara" value={filters.search} onChange={event => setFilters({ ...filters, search: event.target.value })}/></label>
      <label>Uygulama<input placeholder="Uygulama adı" value={filters.app} onChange={event => setFilters({ ...filters, app: event.target.value })}/></label>
      <label>Etiket<input placeholder="Etiket" value={filters.tag} onChange={event => setFilters({ ...filters, tag: event.target.value })}/></label>
      <button type="submit">Uygula</button>
      <button type="button" className="pc-quiet" onClick={() => {
        const empty = { search: '', app: '', tag: '', from: '', until: '' };
        setFilters(empty);
        applyFilters(empty);
      }}>Temizle</button>
    </form>
    <div className="pc-tabs" role="tablist" aria-label="PC geçmişi bölümleri">
      <button role="tab" aria-selected={history} onClick={() => changeSection('history')}>▧ Ekran geçmişi</button>
      <button role="tab" aria-selected={!history} onClick={() => { if (history) changeSection('apps'); }}>▥ Kullanım istatistikleri</button>
    </div>
    {loading && <div className="panel-loading" role="status"><span className="loader"/>Geçmiş yükleniyor…</div>}
    {error && <div className="pc-empty" role="alert">{error}</div>}
    {history ? <div className="pc-history-layout" aria-busy={loading}>
      <section className="pc-card">
        <div className="pc-card-head">
          <div><h4>Kayıtlar</h4><p>En yeniden eskiye · {rows.length} kayıt</p></div>
          <div className="pc-actions">
            <button className="pc-quiet" disabled={loading} onClick={() => command('retry-ocr')}>OCR yeniden dene</button>
            <button className="pc-danger" disabled={loading || (!state.from && !state.until)} onClick={() => setBulkDelete('delete-range')}>Aralığı sil</button>
            <button className="pc-danger" disabled={loading} onClick={() => setBulkDelete('delete-all')}>Tümünü sil</button>
          </div>
        </div>
        <div className="pc-records">
          {rows.map(row => <button key={row.id} className={`pc-record ${selected?.id === row.id ? 'selected' : ''}`} onClick={() => {
            setTagDraft(null);
            setConfirm(false);
            command('select', { id: row.id });
          }}>
            <span className="pc-record-icon">▧</span>
            <span><strong>{row.title || row.app || 'Başlıksız kayıt'}</strong>
              <small>{row.app || 'Bilinmeyen uygulama'} · {new Date(row.created).toLocaleString('tr-TR')}</small>
              {row.tags && <em>{row.tags}</em>}
            </span>
          </button>)}
          {!rows.length && !loading && !error && <div className="pc-empty">Bu aralıkta kayıt bulunamadı. Filtreleri değiştirmeyi deneyin.</div>}
        </div>
        <div className="pc-pagination">
          <button disabled={loading || state.offset === 0} onClick={() => command('page', { delta: -historyPageSize })}>← Önceki</button>
          <span>{rows.length > 0 ? `${state.offset + 1} – ${state.offset + rows.length}` : '0 kayıt'}</span>
          <button disabled={loading || !state.hasMore} onClick={() => command('page', { delta: historyPageSize })}>Sonraki →</button>
        </div>
      </section>
      <section className="pc-card pc-detail">
        <div className="pc-card-head"><div><h4>Kayıt ayrıntısı</h4><p>{selected?.app || 'Bir kayıt seçin'}</p></div></div>
        {selected ? <>
          <button className="pc-image" disabled={loading || !preview} onClick={() => setZoom(true)}>
            {preview ? <img src={preview} alt="Seçili ekran kaydı"/> : <span>Bu kayıt için görüntü bulunamadı</span>}
          </button>
          <div className="pc-detail-body">
            <strong>{selected.title || 'Başlıksız kayıt'}</strong>
            <small>{new Date(selected.created).toLocaleString('tr-TR')}</small>
            <label>Uygulama etiketleri<input value={tagDraft ?? selected.tags} disabled={loading} placeholder="Etiketleri virgülle ayırın"
              onChange={event => setTagDraft(event.target.value)} onBlur={() => {
                if (tagDraft !== null) { command('tags', { tags: tagDraft }); setTagDraft(null); }
              }}/></label>
            <label>Algılanan metin<textarea readOnly value={selected.text} placeholder="Bu kayıtta algılanan metin yok"/></label>
            <div className="pc-actions">
              <button disabled={loading} onClick={() => navigator.clipboard?.writeText(selected.text)}>Metni kopyala</button>
              <button disabled={loading || !selected.media} onClick={() => command('open-media')}>Medyayı aç</button>
              {!confirm ? <button className="pc-danger" disabled={loading} onClick={() => setConfirm(true)}>Kaydı sil</button> : <>
                <span>Kalıcı olarak silinsin mi?</span>
                <button className="pc-danger" disabled={loading} onClick={() => { command('delete'); setConfirm(false); }}>Evet, sil</button>
                <button onClick={() => setConfirm(false)}>Vazgeç</button>
              </>}
            </div>
          </div>
        </> : !loading && <div className="pc-empty">Ayrıntıları görmek için bir kayıt seçin.</div>}
      </section>
    </div> : <section className="pc-card pc-statistics" aria-busy={loading}>
      <div className="pc-card-head"><div><h4>Kullanım dökümü</h4><p>Seçilen tarihlerdeki yerel etkinlik verileri</p></div></div>
      <div className="pc-subtabs">{sections.map(([id, label]) => <button key={id} aria-pressed={section === id} onClick={() => changeSection(id)}>{label}</button>)}</div>
      {!loading && !error && <>
        <Statistics state={state}/>
        {state.statisticsTotal > statisticsPageSize && <div className="pc-pagination">
          <button disabled={state.statisticsOffset === 0} onClick={() => command('statistics-page', { delta: -statisticsPageSize })}>← Önceki</button>
          <span>{state.statisticsOffset + 1} – {state.statisticsOffset + state.statistics.length} / {state.statisticsTotal}</span>
          <button disabled={state.statisticsOffset + state.statistics.length >= state.statisticsTotal}
            onClick={() => command('statistics-page', { delta: statisticsPageSize })}>Sonraki →</button>
        </div>}
      </>}
    </section>}
    {bulkDelete && <div className="pc-zoom"><section className="pc-delete-dialog" role="alertdialog" aria-modal="true" aria-label="Geçmişi sil">
      <h4>Geçmiş kalıcı olarak silinsin mi?</h4>
      <p>{bulkDelete === 'delete-all' ? 'Tüm PC geçmişi kayıtları silinecek.' : 'Seçilen tarih aralığındaki tüm PC geçmişi kayıtları silinecek.'}</p>
      <div className="pc-actions">
        <button onClick={() => setBulkDelete('')}>Vazgeç</button>
        <button className="pc-danger" disabled={loading} onClick={() => { command(bulkDelete); setBulkDelete(''); }}>Evet, sil</button>
      </div>
    </section></div>}
    {zoom && preview && <div className="pc-zoom" onClick={() => setZoom(false)}>
      <button aria-label="Önizlemeyi kapat">×</button><img src={preview} alt="Büyük ekran kaydı"/>
    </div>}
  </div>;
}

function Statistics({ state }: { state: PcHistoryState }) {
  const { section, statistics, maximumApplicationSeconds } = state;
  switch (section) {
    case 'apps':
      return statistics.length ? (statistics as Total[]).map(row => <div className="pc-bar-row" key={`${row.app}-${row.estimated}`}>
        <div><strong>{row.app}</strong><span>{duration(row.seconds)}{row.estimated ? ' · tahmini' : ''}</span></div>
        <div className="pc-bar"><i style={{ width: `${row.seconds / maximumApplicationSeconds * 100}%` }}/></div>
      </div>) : <div className="pc-empty">Henüz kullanım kaydı yok.</div>;
    case 'days':
      return <div className="pc-table">{(statistics as Day[]).map(row => <div key={JSON.stringify([row.date, row.app, row.estimated])}>
        <span>{row.date}</span><strong>{row.app}</strong><span>{duration(row.seconds)}{row.estimated ? ' · tahmini' : ''}</span>
      </div>)}{!statistics.length && <div className="pc-empty">Günlük kullanım kaydı yok.</div>}</div>;
    case 'contexts':
      return <div className="pc-table">{(statistics as Context[]).map(row => <div key={JSON.stringify([row.date, row.app, row.type, row.context])}>
        <span>{row.date}</span><strong>{row.context}</strong><span>{row.app} · {duration(row.seconds)}</span>
      </div>)}{!statistics.length && <div className="pc-empty">Site veya klasör kaydı yok.</div>}</div>;
    case 'tags':
      return <div className="pc-table">{(statistics as Tag[]).map(row => <div key={row.tag}>
        <strong>{row.tag}</strong><span>{row.applicationCount} uygulama</span><span>{duration(row.seconds)}</span>
      </div>)}{!statistics.length && <div className="pc-empty">Henüz etiketli kullanım yok.</div>}</div>;
    case 'network':
      return <div className="pc-table"><div><strong>Tarih</strong><strong>İndirilen</strong><strong>Yüklenen</strong></div>
        {(statistics as Network[]).map(row => <div key={row.date}><span>{row.date}</span><span>{bytes(row.received)}</span><span>{bytes(row.sent)}</span></div>)}
        {!statistics.length && <div className="pc-empty">İnternet kullanımı kaydı yok. Takibi kayıt ayarlarından açabilirsiniz.</div>}
      </div>;
    default:
      return null;
  }
}
