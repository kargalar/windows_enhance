import { useEffect, useRef, useState, type ReactNode } from 'react';
import { copy } from './moduleDesigns';
import { Icon } from './Icon';

type Rule = { id:string;name:string;shortcut:string;enabled:boolean };
export function RuleWorkbench<T extends Rule>({ rules,render,kind,filterQuery='' }: {rules:T[];render:(rule:T)=>ReactNode;kind:'shortcut'|'automation'|'mapping'|'profile';filterQuery?:string}) {
  const [selected,setSelected]=useState('');
  const previous=useRef(new Set(rules.map(rule=>rule.id)));
  useEffect(()=>{const added=rules.find(rule=>!previous.current.has(rule.id));if(added)setSelected(added.id);previous.current=new Set(rules.map(rule=>rule.id));},[rules]);
  const filtered=rules.filter(rule=>`${rule.name} ${rule.shortcut}`.toLocaleLowerCase().includes(filterQuery.trim().toLocaleLowerCase()));
  const active=filtered.some(rule=>rule.id===selected)?selected:filtered[0]?.id;
  const name=kind==='automation'?copy(['Otomasyonlar','Automations']):kind==='profile'?copy(['Ekran profilleri','Display profiles']):kind==='mapping'?copy(['Eşleştirmeler','Mappings']):copy(['Kısayollar','Shortcuts']);
  if(!rules.length)return <div className="workbench-empty"><Icon name={kind==='automation'?'bolt':'keyboard'} size={34}/><h3>{copy(['Henüz kayıt yok','No entries yet'])}</h3><p>{copy(['Aşağıdaki ekleme düğmesiyle ilk kaydını oluştur.','Use the add button below to create your first entry.'])}</p></div>;
  return <div className={`rule-workbench workbench-${kind}`}>
    <nav className="rule-index" aria-label={name}><header><strong>{name}</strong><span>{rules.length}</span></header>
      {!filtered.length&&<p>{copy(['Eşleşen kayıt bulunamadı.','No matching entries.'])}</p>}
      {filtered.map(rule=><button type="button" key={rule.id} aria-current={active===rule.id?'true':undefined} onClick={()=>setSelected(rule.id)}>
        <span className={`rule-state-dot ${rule.enabled?'enabled':''}`}/><span><strong>{rule.name}</strong><kbd>{rule.shortcut||copy(['Kısayol atanmadı','No shortcut assigned'])}</kbd></span><Icon name="arrow" size={14}/>
      </button>)}
    </nav>
    <div className="rule-editor">{rules.map(rule=><div key={rule.id} hidden={rule.id!==active} data-rule-editor={rule.id}>{render(rule)}</div>)}</div>
  </div>;
}
