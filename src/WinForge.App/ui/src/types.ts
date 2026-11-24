import type { PcHistoryState } from './PcHistoryPanel';
import type { AutomationState } from './AutomationPanel';
import type { ShortcutActionsState } from './ShortcutActionsPanel';
import type { ContextMenuState } from './ContextMenuPanel';

export type Setting = {
  key: string;
  label: string;
  description?: string;
  section?: string;
  kind: 'toggle' | 'number' | 'text' | 'choice' | 'palette' | 'action';
  defaultValue?: unknown;
  value?: unknown;
  min: number;
  max: number;
  options: string[];
  optionLabels: Record<string, string>;
};

export type Module = {
  id: string;
  name: string;
  category: string;
  description: string;
  warning?: string;
  isActive: boolean;
  error?: string;
  panelAvailable?: boolean;
  panelLoaded?: boolean;
  panelError?: string;
  settings: Setting[];
  panel?: PcHistoryState | AutomationState | ShortcutActionsState | ContextMenuState | { kind: 'codex-quota'; updatedAt: string; accounts: { id: string; email: string; alias: string; visible: boolean; status: string; error: string; windows: { label: string; remaining: number; resetsAt: number | null; resetLabel: string }[] }[] } | { kind: 'start-sections'; buttons: { id: string; name: string; visible: boolean }[] } | { kind: 'default-paths' | 'default-apps'; state: Record<string, unknown> | null };
};

export type Appearance = {
  tone: string;
  accent: string;
  glow: boolean;
  tones: string[];
  accents: { name: string; hex: string }[];
  language?: string;
};

export type AppState = {
  modules: Module[];
  categories: string[];
  activeCount: number;
  totalCount: number;
  version: string;
  appearance: Appearance;
  dataDirectory?: string;
  persistence?: { pending: boolean; error?: string; recovery?: string };
};

