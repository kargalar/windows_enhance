import { getLanguage } from './localization';

type IdentifiedModule = { id: string; category: string };

export const shortcutModuleIds = ['media-shortcuts', 'window-shortcuts', 'system-shortcuts', 'desktop-shortcuts', 'text-shortcuts', 'program-shortcuts', 'monitor-shortcuts', 'combination-translator', 'keyboard-manager'];
export const isShortcutModule = (module: IdentifiedModule) => shortcutModuleIds.includes(module.id);

const familiesTr: [string, string[]][] = [
  ['Görev Çubuğu', ['taskbar', 'tray-icons', 'shell-customization', 'net-speed']],
  ['Başlat Menüsü', ['start-menu']],
  ['Sağ Tık Menüsü', ['context-menu', 'classic-context-menu']],
  ['Gezgin', ['explorer-tweaks', 'explorer-navigation', 'explorer-mouse-navigation', 'explorer-folder-sizes', 'folder-dialog']],
  ['Kısayollar ve Klavye', shortcutModuleIds],
  ['Pencere Yönetimi', ['window-control', 'window-tweaks', 'always-on-top', 'fancy-zones', 'tiling', 'crop-and-lock']],
  ['Medya', ['media-flyout']],
  ['Varsayılanlar', ['default-paths', 'default-apps']],
];

const familiesEn: [string, string[]][] = [
  ['Taskbar', ['taskbar', 'tray-icons', 'shell-customization', 'net-speed']],
  ['Start Menu', ['start-menu']],
  ['Context Menu', ['context-menu', 'classic-context-menu']],
  ['Explorer', ['explorer-tweaks', 'explorer-navigation', 'explorer-mouse-navigation', 'explorer-folder-sizes', 'folder-dialog']],
  ['Shortcuts & Keyboard', shortcutModuleIds],
  ['Window Management', ['window-control', 'window-tweaks', 'always-on-top', 'fancy-zones', 'tiling', 'crop-and-lock']],
  ['Media', ['media-flyout']],
  ['Defaults', ['default-paths', 'default-apps']],
];

export function groupModules<T extends IdentifiedModule>(modules: T[], lang?: string) {
  const current = lang || getLanguage();
  const isTr = current === 'tr' || (!lang && modules.some(m => ['Verimlilik', 'Gezgin', 'Görev Çubuğu', 'Başlat', 'Görünüm', 'Pencereler'].includes(m.category)));
  const families = isTr ? familiesTr : familiesEn;
  const groups = new Map<string, T[]>();
  for (const module of modules) {
    const name = families.find(([, ids]) => ids.includes(module.id))?.[0] ?? module.category;
    groups.set(name, [...(groups.get(name) ?? []), module]);
  }
  return [...groups].map(([name, modules]) => ({ name, modules }));
}
