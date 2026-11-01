type IdentifiedModule = { id: string; category: string };
const families: [string, string[]][] = [
  ['Görev Çubuğu', ['taskbar', 'tray-icons', 'shell-customization', 'net-speed']],
  ['Başlat Menüsü', ['start-menu']],
  ['Sağ Tık Menüsü', ['context-menu', 'classic-context-menu']],
  ['Gezgin', ['explorer-tweaks', 'explorer-navigation', 'explorer-mouse-navigation', 'explorer-folder-sizes', 'folder-dialog']],
  ['Kısayollar ve Klavye', ['keyboard-manager', 'shortcut-guide', 'media-shortcuts', 'window-shortcuts', 'desktop-shortcuts', 'text-shortcuts', 'program-shortcuts', 'monitor-shortcuts', 'combination-translator']],
  ['Pencere Yönetimi', ['window-control', 'window-tweaks', 'always-on-top', 'fancy-zones', 'tiling', 'workspaces', 'crop-and-lock', 'square-corners']],
  ['Medya', ['media-flyout', 'dynamic-island']],
  ['Varsayılanlar', ['default-paths', 'default-apps']],
];
export function groupModules<T extends IdentifiedModule>(modules: T[]) {
  const groups = new Map<string, T[]>();
  for (const module of modules) {
    const name = families.find(([, ids]) => ids.includes(module.id))?.[0] ?? module.category;
    groups.set(name, [...(groups.get(name) ?? []), module]);
  }
  return [...groups].map(([name, modules]) => ({ name, modules }));
}
