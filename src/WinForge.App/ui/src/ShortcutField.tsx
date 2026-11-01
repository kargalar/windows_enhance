import { useEffect, useRef, useState } from 'react';

const keys: Record<string, string> = {
  Minus: 'OemMinus', Equal: 'OemPlus', Comma: 'OemComma', Period: 'OemPeriod',
  Semicolon: 'OemSemicolon', Quote: 'OemQuotes', Slash: 'OemQuestion',
  Backslash: 'OemPipe', IntlBackslash: 'OemBackslash', Backquote: 'OemTilde',
  BracketLeft: 'OemOpenBrackets', BracketRight: 'OemCloseBrackets',
  NumpadAdd: 'Add', NumpadSubtract: 'Subtract', NumpadMultiply: 'Multiply',
  NumpadDivide: 'Divide', NumpadDecimal: 'Decimal', NumpadEnter: 'Enter',
  MediaTrackNext: 'MediaNextTrack', MediaTrackPrevious: 'MediaPreviousTrack',
  AudioVolumeUp: 'VolumeUp', AudioVolumeDown: 'VolumeDown', AudioVolumeMute: 'VolumeMute',
  ' ': 'Space', ArrowLeft: 'Left', ArrowRight: 'Right', ArrowUp: 'Up', ArrowDown: 'Down',
};

type Modifiers = { ctrlKey: boolean; altKey: boolean; shiftKey: boolean; metaKey: boolean };
function chord(key: string, event: Modifiers) {
  return [event.ctrlKey && 'Ctrl', event.altKey && 'Alt', event.shiftKey && 'Shift', event.metaKey && 'Win', key].filter(Boolean).join('+');
}

export function ShortcutField({ value, label, onChange, onCommit, onCapture, allowMouse = false, allowWheel = false, capturing = true }: {
  value: string; label: string; onChange: (value: string) => void; onCommit?: (value: string) => void;
  onCapture?: (active: boolean) => void; allowMouse?: boolean; allowWheel?: boolean; capturing?: boolean;
}) {
  const [recording, setRecording] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const recordingRef = useRef(false);
  const editingRef = useRef(false);
  const latest = useRef(value);
  latest.current = value;
  const callbacks = useRef({ onCapture, onChange, onCommit });
  callbacks.current = { onCapture, onChange, onCommit };
  const end = () => {
    recordingRef.current = false;
    setRecording(false);
    if (!editingRef.current) return;
    editingRef.current = false;
    callbacks.current.onCapture?.(false);
    window.chrome?.webview?.postMessage({ command: 'shortcut.capture-end', payload: {} });
  };
  const begin = () => {
    if (editingRef.current) return;
    editingRef.current = true;
    window.chrome?.webview?.postMessage({ command: 'shortcut.capture-begin', payload: {} });
    callbacks.current.onCapture?.(true);
  };
  const change = (next: string) => { latest.current = next; callbacks.current.onChange(next); };
  const finish = (next: string) => {
    change(next);
    end();
    callbacks.current.onCommit?.(next);
  };
  useEffect(() => () => {
    if (!editingRef.current) return;
    editingRef.current = false;
    callbacks.current.onCapture?.(false);
    window.chrome?.webview?.postMessage({ command: 'shortcut.capture-end', payload: {} });
  }, []);
  useEffect(() => { if (!capturing && recordingRef.current) end(); }, [capturing]);
  return <div className="input-action-row" onBlur={event => {
    if (event.currentTarget.contains(event.relatedTarget as Node | null)) return;
    const wasRecording = recordingRef.current;
    end();
    if (!wasRecording) callbacks.current.onCommit?.(latest.current.trim());
  }}>
    <input ref={input} className="white-control" aria-label={label} value={recording ? 'Tuş veya fare kombinasyonuna basın…' : value} readOnly={recording}
      onFocus={begin} onChange={event => change(event.target.value)}
      onKeyDown={event => {
        if (!recordingRef.current) {
          if (event.key === 'Enter') { event.preventDefault(); end(); callbacks.current.onCommit?.(latest.current.trim()); }
          return;
        }
        event.preventDefault(); event.stopPropagation();
        if (event.key === 'Escape') { end(); return; }
        if (['Control', 'Alt', 'Shift', 'Meta', 'AltGraph'].includes(event.key)) return;
        const key = event.code.startsWith('Key') ? event.code.slice(3) : event.code.startsWith('Digit') ? event.code.slice(5) : /^Numpad\d$/.test(event.code) ? 'NumPad' + event.code.slice(6) : keys[event.code] ?? keys[event.key] ?? event.key;
        finish(chord(key, event));
      }}
      onMouseDown={event => {
        if (!recordingRef.current || !allowMouse) return;
        event.preventDefault();
        finish(chord(['MouseLeft', 'MouseMiddle', 'MouseRight', 'MouseX1', 'MouseX2'][event.button], event));
      }}
      onContextMenu={event => { if (recordingRef.current) event.preventDefault(); }}
      onWheel={event => {
        if (!recordingRef.current || !(allowMouse || allowWheel)) return;
        event.stopPropagation();
        finish(chord(event.deltaY < 0 ? 'WheelUp' : 'WheelDown', event));
      }} />
    <button type="button" className="secondary-button" onClick={() => {
      if (recordingRef.current) { end(); return; }
      begin(); recordingRef.current = true; setRecording(true); input.current?.focus();
    }}>{recording ? 'İptal' : allowMouse ? 'Tuş / fare ata' : 'Tuş ata'}</button>
  </div>;
}
