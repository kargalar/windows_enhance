import type React from 'react';

export function Icon({ name, size = 18 }: { name: string; size?: number }) {
  const paths: Record<string, React.ReactNode> = {
    home: <><path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z" /></>,
    star: <path d="m12 3 2.8 5.7 6.2.9-4.5 4.4 1.1 6.2-5.6-3-5.6 3 1.1-6.2L3 9.6l6.2-.9Z" />,
    monitor: <><rect x="3" y="4" width="18" height="13" rx="1.5" /><path d="M12 17v4m-4 0h8" /></>,
    window: <><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M3 8h18" /></>,
    folder: <path d="M3 7V5a2 2 0 0 1 2-2h4l3 4h7a2 2 0 0 1 2 2v10H3V7Z" />,
    bolt: <path d="m13 2-10 12h8l-1 8 11-13h-8Z" />,
    back: <path d="m10 5-7 7 7 7M3 12h18" />,
    taskbar: <><rect x="2" y="6" width="20" height="13" rx="1" /><path d="M3 16h18m-16 0h.1m3 0h.1" /></>,
    windows: <><rect x="3" y="9" width="13" height="13" rx="1" /><path d="M9 9V3h13v13h-6" /></>,
    text: <><path d="M3 7V3h4m10 0h4v4m0 10v4h-4M7 21H3v-4M8 8h8m-4 0v9" /></>,
    pipette: <><path d="m14 5 5 5M3 21l4-1L20 7a3 3 0 0 0-4-4L3 16v5Z" /><path d="m12 7 5 5" /></>,
    files: <><path d="M11 14H4V2h8l4 4v3M11 2v5h5" /><rect x="10" y="11" width="12" height="11" rx="1" /><path d="M13 15h6m-6 3h6" /></>,
    volume: <><path d="m11 3-6 5H2v8h3l6 5V3Zm5 4a7 7 0 0 1 0 10m3-13a11 11 0 0 1 0 16" /></>,
    search: <><circle cx="11" cy="11" r="7" /><path d="m20 20-4-4" /></>,
    sliders: <><path d="M4 7h9m4 0h3M4 17h3m4 0h9" /><circle cx="15" cy="7" r="2" /><circle cx="9" cy="17" r="2" /></>,
    settings: <><path d="m9.5 3-.6 2.1-2 .9-2.1-.6-2.3 4 1.5 1.5v2.2L2.5 15l2.3 4 2.1-.6 2 .9.6 2.1h5l.6-2.1 2-.9 2.1.6 2.3-4-1.5-1.6v-2.2l1.5-1.5-2.3-4-2.1.6-2-.9-.6-2.1Z" /><circle cx="12" cy="12" r="3" /></>,
    power: <><path d="M12 2v10" /><path d="M5.6 5.6a9 9 0 1 0 12.8 0" /></>,
    layers: <><path d="m12 3 9 5-9 5-9-5 9-5Z" /><path d="m3 12 9 5 9-5M3 16l9 5 9-5" /></>,
    close: <><path d="m18 6-12 12M6 6l12 12" /></>,
    arrow: <><path d="M5 12h14m-7-7 7 7-7 7" /></>,
    check: <path d="m5 12 4 4L19 6" />,
    keyboard: <><rect x="2" y="5" width="20" height="14" rx="2" /><path d="M6 9h.01M10 9h.01M14 9h.01M18 9h.01M6 13h.01M10 13h.01M14 13h.01M18 13h.01M8 16h8" /></>,
    grid: <><rect x="3" y="3" width="7" height="7" rx="1.5" /><rect x="14" y="3" width="7" height="7" rx="1.5" /><rect x="3" y="14" width="7" height="7" rx="1.5" /><rect x="14" y="14" width="7" height="7" rx="1.5" /></>,
  };
  return <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">{paths[name] ?? paths.layers}</svg>;
}

