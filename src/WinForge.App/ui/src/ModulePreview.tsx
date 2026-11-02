import type { CSSProperties } from 'react';
import type { Module } from './types';
import { copy, moduleDesigns } from './moduleDesigns';
import { TaskbarPreview } from './TaskbarPreview';
import { Icon } from './Icon';

export function ModulePreview({ module }: { module: Module }) {
  const kind=moduleDesigns[module.id]?.preview;
  const value=(key:string) => { const s=module.settings.find(s=>s.key===key); return s?.value ?? s?.defaultValue; };
  if(!kind)return null;
  if(kind==='taskbar')return <TaskbarPreview module={module}/>;
  const number=(key:string,fallback:number,min:number,max:number)=>{const raw=value(key);const parsed=Number(raw);return Math.max(min,Math.min(max,raw!==null&&raw!==undefined&&Number.isFinite(parsed)?parsed:fallback));};
  const color=(key:string,fallback:string)=>/^#[\da-f]{6}$/i.test(String(value(key))) ? String(value(key)) : fallback;
  let content;
  if(kind==='zones') {
    const layout=String(value('zone-layout') ?? value('layout') ?? '');
    const custom=/özel|custom/i.test(layout);
    const columns=custom ? number('zone-columns',3,1,6) : /üç|three|sütunlar|columns/i.test(layout) ? 3 : 2;
    const rows=custom ? number('zone-rows',2,1,4) : /dört|four|ızgara|grid/i.test(layout) ? 2 : 1;
    const master=module.id==='auto-tiling'&&/ana|master/i.test(layout);
    content=<div className="zone-preview" style={{gridTemplateColumns:master?`${number('master-percent',60,20,80)}% minmax(0,1fr)`:`repeat(${columns},1fr)`,gridTemplateRows:master?'repeat(3,1fr)':`repeat(${rows},1fr)`,gap:`${number('gap',8,0,18)}px`}}>
      {Array.from({length:master?4:columns*rows},(_,i)=><div key={i} style={{gridRow:master&&i===0?'1 / -1':undefined,borderColor:color(i===0?'zone-active-color':'zone-inactive-color',i===0?'#4CC2FF':'#6B6B6B')}}>{value('zone-show-numbers')!==false ? i+1 : ''}</div>)}
    </div>;
  } else if(kind==='network')content=<div className={`network-preview ${value('layout')==='Alt alta' ? 'stacked':''}`} style={{fontSize:`${number('font-size',14,10,26)}px`}}>
    {value('show-download')!==false && <span>↓ 12.4 {value('unit')==='Mbit/s'?'Mbit/s':'MB/s'}</span>}{value('show-upload')!==false && <span>↑ 1.2 {value('unit')==='Mbit/s'?'Mbit/s':'MB/s'}</span>}
  </div>;
  else if(kind==='border')content=<div className="pinned-preview" style={{border:value('border-enabled')===false?'1px solid var(--border)':`${number('border-thickness',3,1,12)}px solid ${color('border-color','#4CC2FF')}`}}><Icon name="window" size={30}/><span>{copy(['Üste sabitlenen pencere','Pinned window'])}</span></div>;
  else if(kind==='mouse')content=<div className="mouse-preview"><div className="pointer-ring" style={{width:number('click-size',56,24,100),height:number('click-size',56,24,100)}}/><span className="pointer-arrow">↖</span><span>{copy(['Tıklama halkası','Click ring'])}</span></div>;
  else if(kind==='explorer')content=<div className="explorer-preview" style={{fontFamily:String(value('font-family')??'inherit'),fontSize:`${number('font-size',13,10,22)}px`,background:color('background','#f3f5f8'),color:color('text','#243247')}}>
    {['Home','Gallery','OneDrive','Libraries','Network'].filter((_,i)=>value(['hide-home','hide-gallery','hide-onedrive','hide-libraries','hide-network'][i])!==true).map((name,i)=><div key={name} style={{padding:`${number('padding-vertical',6,0,16)}px ${number('padding-horizontal',12,0,30)}px`,background:i===0?color('selected','#dbe8fb'):undefined,color:i===0?color('selected-text','#183754'):undefined,borderRadius:number('corner-radius',4,0,16)}}>{value('show-arrows')!==false&&<span>›</span>}{value('show-icons')!==false&&<Icon name="folder" size={number('icon-size',16,12,28)}/>}<span>{name}</span></div>)}
  </div>;
  else if(kind==='start')content=<div className="start-preview" style={{opacity:number('opacity',100,20,100)/100}}>
    {value('hide-search')!==true&&<div className="start-search"><Icon name="search" size={15}/>{copy(['Uygulama ara','Search applications'])}</div>}
    <div className="start-apps">{Array.from({length:8},(_,i)=><span key={i}><Icon name={i%2?'folder':'window'} size={22}/>{value('hide-labels')!==true&&<small>{copy(['Uygulama','App'])} {i+1}</small>}</span>)}</div>
    {value('hide-recent')!==true&&value('pinned-only')!==true&&<div className="start-recent">{copy(['Önerilenler','Recommended'])}<i/><i/></div>}
    <div className="start-footer">{value('hide-user')!==true&&<span>{copy(['Kullanıcı','User'])}</span>}{value('hide-power')!==true&&<Icon name="power" size={16}/>}</div>
  </div>;
  else if(kind==='theme')content=<div className="theme-preview"><div><Icon name="monitor" size={24}/><strong>{String(value('light-time')??'07:00')}</strong><span>{copy(['Açık tema','Light theme'])}</span></div><div><Icon name="monitor" size={24}/><strong>{String(value('dark-time')??'19:00')}</strong><span>{copy(['Koyu tema','Dark theme'])}</span></div></div>;
  else content=<div className="media-preview"><Icon name="volume" size={30}/><div><strong>{copy(['Medya paneli','Media panel'])}</strong><span>◀ &nbsp; ▷ &nbsp; ▶</span></div><span>{String(value('flyout-position')??'bottom-right')}</span></div>;
  return <figure className={`module-preview specialized-preview preview-${kind}`}><figcaption><strong>{copy(['Düzen önizlemesi','Layout preview'])}</strong><span>{copy(['Ayarların temsili gösterimi; gerçek Windows görünümü farklı olabilir.','A schematic of the settings; actual Windows rendering may vary.'])}</span></figcaption><div aria-hidden="true" className="preview-stage" style={{'--preview-accent':color('zone-active-color','#4CC2FF')} as CSSProperties}>{content}</div></figure>;
}
