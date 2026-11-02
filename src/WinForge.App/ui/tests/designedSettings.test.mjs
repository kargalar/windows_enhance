import assert from 'node:assert/strict';
import test,{after} from 'node:test';
import {Window} from 'happy-dom';
import {createTestServer} from './testServer.mjs';

const window=new Window({url:'https://appassets.winforge/'});
for(const name of ['window','document','HTMLElement','HTMLInputElement','HTMLTextAreaElement','Node','Event','KeyboardEvent','MouseEvent'])globalThis[name]=name==='window'?window:window[name];
globalThis.IS_REACT_ACT_ENVIRONMENT=true;
const {createElement:h,act,StrictMode}=await import('react');
const {createRoot}=await import('react-dom/client');
const server=await createTestServer();
const {DesignedSetting}=await server.ssrLoadModule('/src/DesignedSetting.tsx');
const {RuleWorkbench}=await server.ssrLoadModule('/src/RuleWorkbench.tsx');
const {ShortcutActionsPanel}=await server.ssrLoadModule('/src/ShortcutActionsPanel.tsx');
const {setLanguage}=await server.ssrLoadModule('/src/localization.ts');setLanguage('en');
after(async()=>{await server.close();await window.happyDOM.close();});
const setting=(key,value,extra={})=>({key,label:key,kind:'text',value,defaultValue:'',min:0,max:100,options:[],optionLabels:{},...extra});
async function mount(component){const container=document.createElement('div');document.body.append(container);const root=createRoot(container);await act(async()=>root.render(h(StrictMode,null,component)));return{container,async close(){await act(async()=>root.unmount());container.remove();},async render(component){await act(async()=>root.render(h(StrictMode,null,component)));}};}
async function click(element){assert.ok(element);await act(async()=>element.click());}
async function type(element,value){assert.ok(element);await act(async()=>{const prototype=element.tagName==='TEXTAREA'?window.HTMLTextAreaElement.prototype:window.HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(prototype,'value').set.call(element,value);element.dispatchEvent(new window.Event('input',{bubbles:true}));});}

test('visual position choices preserve native values and friendly labels',async()=>{
 const saved=[];const view=await mount(h(DesignedSetting,{moduleId:'media-flyout',design:'position',setting:setting('position','bottom-right',{kind:'choice',options:['bottom-right','top-left'],optionLabels:{'bottom-right':'Bottom right','top-left':'Top left'}}),onUpdate:(...v)=>saved.push(v)}));
 try{await click([...view.container.querySelectorAll('button')].find(b=>b.textContent==='Top left'));assert.deepEqual(saved,[['position','top-left']]);assert.equal(view.container.querySelector('[aria-pressed=true]').textContent,'Bottom right');}finally{await view.close();}
});

test('application lists honor each native delimiter and retain untouched entries',async()=>{
 const saved=[];const view=await mount(h(DesignedSetting,{moduleId:'window-control',design:'list',setting:setting('Processes','mstsc.exe,game*'),onUpdate:(...v)=>saved.push(v)}));
 try{await type(view.container.querySelector('input'),'code.exe');await click([...view.container.querySelectorAll('button')].find(b=>b.textContent==='Add'));assert.deepEqual(saved.at(-1),['Processes','mstsc.exe,game*,code.exe']);await click(view.container.querySelector('button[aria-label="game*: Remove"]'));assert.deepEqual(saved.at(-1),['Processes','mstsc.exe,code.exe']);}finally{await view.close();}
});

test('mapping editors retain incomplete drafts and serialize only complete rows',async()=>{
 const saved=[];const view=await mount(h(DesignedSetting,{moduleId:'auto-correct',design:'pairs',setting:setting('custom-replacements','brb=be right back'),onUpdate:(...v)=>saved.push(v)}));
 await click([...view.container.querySelectorAll('button')].find(b=>b.textContent==='Add row'));
 let fields=view.container.querySelectorAll('.mapping-row input');
 await type(fields[2],'omw');await act(async()=>new Promise(r=>setTimeout(r,470)));assert.deepEqual(saved,[]);
 await type(fields[3],'on my way');await view.close();
 assert.deepEqual(saved,[['custom-replacements','brb=be right back;omw=on my way']]);
});

test('grid profile validation follows native dimensions and normalizes the multiplication sign',async()=>{
 const saved=[];const view=await mount(h(DesignedSetting,{moduleId:'fancy-zones',design:'grid-profiles',setting:setting('zone-monitor-grid-profiles',''),onUpdate:(...v)=>saved.push(v)}));
 await click([...view.container.querySelectorAll('button')].find(b=>b.textContent==='Add row'));
 const fields=view.container.querySelectorAll('input');await type(fields[0],'DISPLAY1');await type(fields[1],'7x5');await act(async()=>new Promise(r=>setTimeout(r,470)));assert.deepEqual(saved,[]);
 await type(fields[1],'3×2');await view.close();assert.deepEqual(saved,[['zone-monitor-grid-profiles','DISPLAY1=3x2']]);
});

test('post-capture ordering only emits a permitted native workflow',async()=>{
 const saved=[];const view=await mount(h(DesignedSetting,{moduleId:'screen-capture',design:'workflow',setting:setting('workflow','save-copy-edit-upload',{kind:'choice',options:['save-copy-edit-upload','copy-save-edit-upload']}),onUpdate:(...v)=>saved.push(v)}));
 try{await click(view.container.querySelector('button[aria-label="Save: Move later"]'));assert.deepEqual(saved,[['workflow','copy-save-edit-upload']]);}finally{await view.close();}
});

test('invalid JSON is not transmitted and a corrected draft can be saved',async()=>{
 const saved=[];const view=await mount(h(DesignedSetting,{moduleId:'start-menu',design:'json',setting:setting('custom-rules',''),onUpdate:(...v)=>saved.push(v)}));
 const input=view.container.querySelector('textarea');await type(input,'[{');await act(async()=>new Promise(r=>setTimeout(r,470)));assert.deepEqual(saved,[]);assert.equal(input.getAttribute('aria-invalid'),'true');
 await type(input,'{}');await act(async()=>new Promise(r=>setTimeout(r,470)));assert.deepEqual(saved,[]);assert.equal(input.getAttribute('aria-invalid'),'true');
 await type(input,'[]');await view.close();assert.deepEqual(saved,[['custom-rules','[]']]);
});

test('rule workbench selects new records and keeps existing editor drafts mounted during selection',async()=>{
 const rules=[{id:'one',name:'One',shortcut:'F1',enabled:true},{id:'two',name:'Two',shortcut:'F2',enabled:false}];
 const render=rule=>h('input',{defaultValue:rule.name,'aria-label':rule.name});
 const view=await mount(h(RuleWorkbench,{rules,kind:'shortcut',render}));
 try{const first=view.container.querySelector('input');await type(first,'Unsaved local draft');await click(view.container.querySelectorAll('nav button')[1]);assert.equal(first.closest('[data-rule-editor]').hidden,true);await click(view.container.querySelectorAll('nav button')[0]);assert.equal(first.value,'Unsaved local draft');await view.render(h(RuleWorkbench,{rules:[...rules,{id:'three',name:'Three',shortcut:'F3',enabled:false}],kind:'shortcut',render}));assert.equal(view.container.querySelector('button[aria-current]').textContent.includes('Three'),true);}finally{await view.close();}
});

test('adding a shortcut clears its search filter before issuing the native add command',async()=>{
 const events=[];
 const view=await mount(h(ShortcutActionsPanel,{moduleId:'media-shortcuts',active:false,filterQuery:'missing',state:{kind:'shortcut-actions',capturing:false,actions:[],rules:[],pendingDisplay:false},onClearFilter:()=>events.push('clear'),command:action=>events.push(action)}));
 events.length=0; // StrictMode deliberately exercises the capture cleanup on mount.
 try{await click(view.container.querySelector('.automation-add'));assert.deepEqual(events,['clear','add']);}finally{await view.close();}
});
