import type { Page } from '@playwright/test';
import type { AppState } from '../src/types';

// Controlled native transport for production-bundle layout tests. Actual native
// command persistence is verified by DashboardInterfaceChecks in WebView2.
export async function nativeHarness(page:Page,snapshot:AppState) {
  await page.addInitScript(snapshot=>{
    const listeners=new Set<(event:{data:unknown})=>void>();
    const payload=structuredClone(snapshot);let version=0;
    const reply=(requestId:string)=>queueMicrotask(()=>{const data={type:'state',protocolVersion:1,sessionId:'design-audit',version:++version,requestId,payload:structuredClone(payload)};listeners.forEach(fn=>fn({data}));});
    Object.defineProperty(window,'chrome',{configurable:true,value:{webview:{
      addEventListener(_type:string,fn:(event:{data:unknown})=>void){listeners.add(fn);},
      removeEventListener(_type:string,fn:(event:{data:unknown})=>void){listeners.delete(fn);},
      postMessage(message:{command:string;requestId:string;payload:Record<string,unknown>}){
        const p=message.payload;
        if(message.command==='module.toggle'){payload.modules.find(m=>m.id===p.id)!.isActive=Boolean(p.enabled);}
        if(message.command==='setting.update'){payload.modules.find(m=>m.id===p.moduleId)!.settings.find(s=>s.key===p.key)!.value=p.value;}
        if(message.command==='appearance.update')Object.assign(payload.appearance,p);
        if(['ui.ready','ui.resync','module.toggle','setting.update','appearance.update'].includes(message.command))reply(message.requestId);
      },
    }}});
  },snapshot);
}
