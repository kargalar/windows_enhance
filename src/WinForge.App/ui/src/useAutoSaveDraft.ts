import { useEffect, useRef, useState } from 'react';

function comparable(value: unknown): string {
  return JSON.stringify(value, (key, item) => {
    if (key === 'running' || key === 'hasSecret' || (key === 'processName' && !item)) return undefined;
    if (item && typeof item === 'object' && !Array.isArray(item))
      return Object.fromEntries(Object.keys(item).sort().map(name => [name, item[name]]));
    return item;
  });
}

// Keep edits made while a native acknowledgement is in flight, and flush on navigation.
export function useAutoSaveDraft<T>(value: T, save: (value: T) => void) {
  const [draft, setDraft] = useState(value);
  const current = useRef(value);
  const submitted = useRef(JSON.stringify(value));
  const waiting = useRef(false);
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined);
  const callback = useRef(save);
  callback.current = save;
  const commit = () => {
    clearTimeout(timer.current); timer.current = undefined;
    const snapshot = JSON.stringify(current.current);
    if (snapshot === submitted.current) return;
    submitted.current = snapshot;
    waiting.current = true;
    callback.current(current.current);
  };
  const snapshot = JSON.stringify(value);
  useEffect(() => {
    if (JSON.stringify(current.current) !== submitted.current) return;
    if (waiting.current) {
      const incoming = value && typeof value === 'object' && 'hasSecret' in value && value.hasSecret && 'value' in value && value.value === ''
        ? { ...value, value: (JSON.parse(submitted.current) as { value: string }).value } : value;
      if (comparable(incoming) !== comparable(JSON.parse(submitted.current))) return;
    }
    waiting.current = false;
    current.current = value; submitted.current = snapshot; setDraft(value);
  }, [snapshot]);
  useEffect(() => () => { if (timer.current !== undefined) commit(); }, []);
  const update = (next: T | ((previous: T) => T), immediate = false) => {
    current.current = typeof next === 'function' ? (next as (previous: T) => T)(current.current) : next;
    setDraft(current.current);
    clearTimeout(timer.current);
    if (immediate) commit(); else timer.current = setTimeout(commit, 450);
  };
  const cancel = () => { clearTimeout(timer.current); timer.current = undefined; };
  return [draft, update, cancel] as const;
}
