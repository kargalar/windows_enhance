import type { AppState } from './types';

export const previewState: AppState = {
  modules: [
    { id: 'taskbar-tools', name: 'Taskbar Tools', category: 'Taskbar', description: 'Adds small, useful enhancements to the Windows taskbar.', isActive: true, settings: [
      { key: 'seconds', label: 'Show seconds in clock', description: 'Adds seconds to the taskbar clock.', kind: 'toggle', value: true, min: 0, max: 1, options: [], optionLabels: {} },
      { key: 'opacity', label: 'Transparency level', description: 'Adjusts background opacity of the taskbar.', kind: 'number', value: 72, min: 0, max: 100, options: [], optionLabels: {} },
      { key: 'action', label: 'Open settings', kind: 'action', min: 0, max: 0, options: [], optionLabels: {} },
    ] },
    { id: 'media-flyout', name: 'Media and Audio Flyout', category: 'System', description: 'Gathers volume and media playback controls in a compact flyout.', isActive: true, settings: [
      { key: 'hotkey-enabled', label: 'Shortcut enabled', kind: 'toggle', value: true, min: 0, max: 1, options: [], optionLabels: {} },
      { key: 'corner', label: 'Panel position', kind: 'choice', value: 'bottom-right', min: 0, max: 0, options: ['bottom-right', 'bottom-left'], optionLabels: { 'bottom-right': 'Bottom right', 'bottom-left': 'Bottom left' } },
      { key: 'palette', label: 'Accent color', kind: 'palette', value: '#ffffff', min: 0, max: 0, options: ['#ffffff', '#73a8ff', '#53d989'], optionLabels: {} },
    ] },
    { id: 'display-brightness', name: 'Display Brightness', category: 'System', description: 'Controls brightness and schedules for supported displays.', isActive: false, warning: 'External monitor controls depend on DDC/CI support.', settings: [
      { key: 'brightness-step', label: 'Shortcut step (%)', section: 'Appearance', description: 'Brightness difference applied on each shortcut key press.', kind: 'number', value: 8, min: 1, max: 25, options: [], optionLabels: {} },
      { key: 'brightness-hotkey', label: 'Brightness shortcut', kind: 'text', value: 'Ctrl+Alt+F12', min: 0, max: 0, options: [], optionLabels: {} },
    ] },
  ],
  categories: ['Taskbar', 'System'], activeCount: 2, totalCount: 3, version: '1.0.0',
  appearance: { tone: 'Açık', accent: 'Mavi', glow: false, tones: ['Açık', 'Siyah', 'Grafit'], accents: [{ name: 'Mavi', hex: '#75A7FF' }, { name: 'Yeşil', hex: '#4CD97B' }, { name: 'Kırmızı', hex: '#FF6B6B' }], language: 'en' },
};

