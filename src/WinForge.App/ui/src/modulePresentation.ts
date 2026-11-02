import type { Module } from './types';

export function moduleGroup(module: Module): string {
  if (['Taskbar', 'Görev çubuğu', 'Görev Çubuğu', 'Başlat', 'Start Menu', 'Görünüm', 'Appearance'].includes(module.category)) return 'Appearance';
  if (['Fare', 'Mouse', 'Arama', 'Search', 'Pencereler', 'Windows'].includes(module.category)) return 'Productivity';
  if (['Sistem', 'System'].includes(module.category)) return 'System';
  if (['Gezgin', 'Explorer'].includes(module.category)) return 'Explorer';
  return module.category;
}

export function moduleIcon(module: Module) {
  const text = `${module.id} ${module.name}`.toLowerCase();
  if (/taskbar|görev çubuğu/.test(text)) return 'taskbar';
  if (/window|pencere|tiling/.test(text)) return 'windows';
  if (/text|metin|ocr/.test(text)) return 'text';
  if (/color|renk/.test(text)) return 'pipette';
  if (/rename|adlandır/.test(text)) return 'files';
  if (/media|medya|ses|volume|sound/.test(text)) return 'volume';
  if (/folder|gezgin|explorer/.test(text) || module.category === 'Gezgin' || module.category === 'Explorer') return 'folder';
  return moduleGroup(module) === 'System' ? 'settings' : 'grid';
}

