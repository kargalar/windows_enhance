import { useCallback, useEffect, useRef, useState, type KeyboardEvent, type UIEvent } from 'react';
import { Icon } from '../Icon';
import './clipboard-history.css';

type Filter = 'all' | 'text' | 'image';
type Clip = {
  id: number;
  copiedAt: number;
  text: string | null;
  thumbnail: string | null;
  width: number;
  height: number;
};

export type ClipboardHistoryState = {
  session: number;
  language: string;
  appearance: 'light' | 'dark';
  query: string;
  kind: Filter;
  loading: boolean;
  loadingMore?: boolean;
  error: string;
  page: number;
  hasNext: boolean;
  items: Clip[];
};

const labels = {
  tr: {
    title: 'Pano Geçmişi', search: 'Geçmişte ara', all: 'Tümü', text: 'Metinler', image: 'Görseller',
    type: 'İçerik türü', imageItem: 'Görsel', textItem: 'Metin', close: 'Kapat',
    copy: 'Panoya kopyala', select: 'Seç', empty: 'Pano geçmişi boş',
    emptyHint: 'Kopyaladığın metin ve görseller burada görünür.', noResults: 'Eşleşen öğe yok',
    loading: 'Geçmiş yükleniyor…', loadingMore: 'Daha fazla yükleniyor…', endOfHistory: 'Geçmişin sonu',
    itemCount: (n: number) => `${n} öğe`,
  },
  en: {
    title: 'Clipboard History', search: 'Search history', all: 'All', text: 'Text', image: 'Images',
    type: 'Content type', imageItem: 'Image', textItem: 'Text', close: 'Close',
    copy: 'Copy to clipboard', select: 'Select', empty: 'Clipboard history is empty',
    emptyHint: 'Text and images you copy will appear here.', noResults: 'No matching items',
    loading: 'Loading history…', loadingMore: 'Loading more…', endOfHistory: 'End of history',
    itemCount: (n: number) => `${n} ${n === 1 ? 'item' : 'items'}`,
  },
};

export function ClipboardHistoryView({ state, send }: {
  state: ClipboardHistoryState;
  send: (command: string, payload?: Record<string, unknown>) => void;
}) {
  const text = labels[state.language === 'tr' ? 'tr' : 'en'];
  const [query, setQuery] = useState('');
  const [kind, setKind] = useState<Filter>('all');
  const [selected, setSelected] = useState<number | null>(null);
  const search = useRef<HTMLInputElement>(null);
  const scrollContainer = useRef<HTMLDivElement>(null);
  const list = useRef<HTMLUListElement>(null);
  const sentinel = useRef<HTMLDivElement>(null);
  const requestedPage = useRef<number | null>(null);
  const matches = query.trim() === state.query && kind === state.kind;
  const loadingMore = Boolean(state.loadingMore);
  const active = state.items.find(item => item.id === selected) ?? state.items[0];
  const dates = new Intl.DateTimeFormat(state.language, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });

  useEffect(() => { search.current?.focus(); }, []);

  useEffect(() => {
    if (!loadingMore) requestedPage.current = null;
  }, [state.page, state.query, state.kind, state.items.length, loadingMore]);

  useEffect(() => {
    if (matches) return;
    const update = window.setTimeout(() => send('clipboard-history.filter', { query, kind }), 140);
    return () => window.clearTimeout(update);
  }, [query, kind, matches, send]);

  useEffect(() => {
    list.current?.querySelector('[data-selected="true"]')?.scrollIntoView({ block: 'nearest' });
  }, [active?.id]);

  const requestMore = useCallback(() => {
    if (!matches || state.loading || loadingMore || !state.hasNext || requestedPage.current === state.page) return;
    requestedPage.current = state.page;
    send('clipboard-history.load-more', { page: state.page });
  }, [matches, state.loading, loadingMore, state.hasNext, state.page, send]);

  useEffect(() => {
    const root = scrollContainer.current;
    const target = sentinel.current;
    if (!root || !target || !state.hasNext || !matches || state.loading || loadingMore) return;
    const observer = new IntersectionObserver(
      entries => {
        if (entries.some(entry => entry.isIntersecting)) requestMore();
      },
      { root, rootMargin: '0px 0px 140px 0px', threshold: 0 },
    );
    observer.observe(target);
    return () => observer.disconnect();
  }, [state.hasNext, matches, state.loading, loadingMore, state.items.length, requestMore]);

  function handleScroll(event: UIEvent<HTMLDivElement>) {
    const el = event.currentTarget;
    if (el.scrollHeight - el.scrollTop - el.clientHeight <= 140) requestMore();
  }

  function copy(id: number) {
    if (matches) send('clipboard-history.copy', { id });
  }

  function handleKey(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault();
      send('clipboard-history.close');
    }
    if (!matches || !active || event.altKey || event.ctrlKey || event.metaKey || event.nativeEvent.isComposing) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      if (event.target instanceof HTMLButtonElement) return;
      event.preventDefault();
      const current = state.items.indexOf(active);
      const index = Math.max(0, Math.min(state.items.length - 1, current + (event.key === 'ArrowDown' ? 1 : -1)));
      setSelected(state.items[index].id);
      if (event.key === 'ArrowDown' && index >= state.items.length - 3) requestMore();
    }
    if (event.key === 'Enter' && event.target === search.current) {
      event.preventDefault();
      copy(active.id);
    }
  }

  return <div className="feature-root clipboard-history-root" data-appearance={state.appearance} lang={state.language}>
    <section className="clipboard-history" role="dialog" aria-labelledby="clipboard-history-title" onKeyDown={handleKey}>
      <header className="clipboard-history-header">
        <span className="clipboard-history-mark"><Icon name="files" size={16} /></span>
        <h1 id="clipboard-history-title">{text.title}</h1>
        <kbd>Win + V</kbd>
        <button className="clipboard-history-close" aria-label={text.close} title={text.close} onClick={() => send('clipboard-history.close')}>
          <Icon name="close" size={14} />
        </button>
      </header>
      <div className="clipboard-history-tools">
        <label className="clipboard-history-search">
          <Icon name="search" size={15} />
          <input ref={search} type="search" aria-label={text.search} placeholder={text.search} maxLength={512}
            value={query} onChange={event => {
              setSelected(null);
              requestedPage.current = null;
              scrollContainer.current?.scrollTo({ top: 0 });
              setQuery(event.target.value);
            }} />
        </label>
        <div className="clipboard-history-filters" role="group" aria-label={text.type}>
          {(['all', 'text', 'image'] as const).map(filter => <button key={filter} aria-pressed={kind === filter}
            onClick={() => {
              setKind(filter);
              setSelected(null);
              requestedPage.current = null;
              scrollContainer.current?.scrollTo({ top: 0 });
            }}>{text[filter]}</button>)}
        </div>
      </div>
      {state.error && <p className="clipboard-history-error" role="alert">{state.error}</p>}
      <div className="clipboard-history-results" aria-busy={state.loading || loadingMore || !matches}>
        <div ref={scrollContainer} className="clipboard-history-scroll" onScroll={handleScroll}>
          <ul ref={list} className="clipboard-history-list">
            {state.items.map(item => <li key={item.id}>
              <button className="clipboard-history-item" data-selected={item.id === active?.id}
                onClick={() => copy(item.id)} disabled={!matches} aria-label={`${text.copy}: ${item.text || `${text.imageItem} ${item.width} × ${item.height}`}`}>
                {item.thumbnail
                  ? <img className="clipboard-history-thumbnail" src={item.thumbnail} alt="" draggable={false} />
                  : <span className="clipboard-history-type"><Icon name="text" size={15} /></span>}
                <span className="clipboard-history-item-content">
                  {item.text && <span className="clipboard-history-excerpt">{item.text}</span>}
                  <span className="clipboard-history-metadata">
                    <span>{item.thumbnail ? `${text.imageItem} · ${item.width} × ${item.height}` : text.textItem}</span>
                    <span className="clipboard-history-dot" aria-hidden="true">·</span>
                    <time dateTime={new Date(item.copiedAt).toISOString()}>{dates.format(item.copiedAt)}</time>
                  </span>
                </span>
                <span className="clipboard-history-copy" aria-hidden="true"><Icon name="files" size={15} /></span>
              </button>
            </li>)}
          </ul>
          {(state.hasNext || loadingMore) && (
            <div ref={sentinel} className="clipboard-history-sentinel" role={loadingMore ? 'status' : undefined}>
              {loadingMore && <>
                <span className="clipboard-history-pulse" aria-hidden="true"><i /><i /><i /></span>
                <span>{text.loadingMore}</span>
              </>}
            </div>
          )}
          {!state.hasNext && !loadingMore && state.items.length > 0 && state.page > 1 && (
            <div className="clipboard-history-end">{text.endOfHistory}</div>
          )}
        </div>
        {state.items.length === 0 && !state.error && <div className="clipboard-history-empty" role="status">
          <span className="clipboard-history-mark"><Icon name={query || kind !== 'all' ? 'search' : 'files'} size={20} /></span>
          <strong>{state.loading || !matches ? text.loading : query || kind !== 'all' ? text.noResults : text.empty}</strong>
          {matches && !state.loading && !query && kind === 'all' && <p>{text.emptyHint}</p>}
        </div>}
      </div>
      <footer className="clipboard-history-footer">
        <span className="clipboard-history-keys"><kbd>↑ ↓</kbd><kbd>Enter</kbd> {text.select} <kbd>Esc</kbd> {text.close}</span>
        {state.items.length > 0 && <span className="clipboard-history-count">{text.itemCount(state.items.length)}</span>}
      </footer>
    </section>
  </div>;
}
