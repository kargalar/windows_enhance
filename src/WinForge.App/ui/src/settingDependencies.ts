import type { Module } from './types';

// Controls remain editable while their feature is off. Explain exactly which
// switch is needed instead of silently disabling a user's stored configuration.
const gates:Record<string,Record<string,string[]>>={
  taskbar:{'volume-step':['scroll-volume'],'background-color':['design-enabled','custom-background'],'background-opacity':['design-enabled','custom-background'],'dock-scale':['design-enabled','dock-animation'],'dock-duration':['design-enabled','dock-animation']},
  mouse:{'scroll-lines':['tweaks-enabled'],'double-click':['tweaks-enabled'],'acceleration':['tweaks-enabled'],'click-size':['spotlight-enabled','click'],'click-duration':['spotlight-enabled','click'],'click-buttons':['spotlight-enabled','click'],'crosshairs-opacity':['spotlight-enabled'],'crosshairs-thickness':['spotlight-enabled']},
  'start-menu':{width:['appearance-enabled'],height:['appearance-enabled'],'icon-size':['appearance-enabled'],opacity:['appearance-enabled'],'custom-rules':['appearance-enabled'],recommendations:['cleanup-enabled'],'new-apps':['cleanup-enabled'],recent:['cleanup-enabled'],web:['search-enabled'],highlights:['search-enabled'],suggestions:['search-enabled']},
  'theme-switch':{'light-time':['schedule-enabled'],'dark-time':['schedule-enabled']},
  'display-brightness':{'day-start-time':['brightness-schedule-enabled'],'day-brightness':['brightness-schedule-enabled'],'night-start-time':['brightness-schedule-enabled'],'night-brightness':['brightness-schedule-enabled'],'brightness-hotkey-step':['brightness-hotkeys-enabled'],'brightness-dim-hotkey':['brightness-hotkeys-enabled'],'brightness-brighten-hotkey':['brightness-hotkeys-enabled']},
  'always-on-top':{'border-color':['border-enabled'],'border-thickness':['border-enabled']},
  'media-flyout':{'taskbar-widget-scroll-mode':['taskbar-media-widget-enabled'],'taskbar-visualizer-position':['taskbar-visualizer-enabled'],'taskbar-visualizer-baseline':['taskbar-visualizer-enabled'],'taskbar-visualizer-hide-when-silent':['taskbar-visualizer-enabled'],'caps-lock-flyout-enabled':['lock-keys-flyout-enabled'],'num-lock-flyout-enabled':['lock-keys-flyout-enabled'],'scroll-lock-flyout-enabled':['lock-keys-flyout-enabled']},
  'screen-capture':{'upload-url':['upload']},
  'auto-correct':{'custom-replacements':['text-expansions']},
};
export function inactiveDependencies(module:Module,key:string):string[]{
  return (gates[module.id]?.[key]??[]).map(key=>module.settings.find(s=>s.key===key)).filter(s=>s&&(s.value??s.defaultValue)===false).map(s=>s!.label);
}
