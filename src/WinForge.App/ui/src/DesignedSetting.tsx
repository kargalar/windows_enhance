import { useId, useState } from 'react';
import type { Setting } from './types';
import { copy, type ControlDesign } from './moduleDesigns';
import { useAutoSaveDraft } from './useAutoSaveDraft';
import { Icon } from './Icon';

type Props = { setting: Setting; design: ControlDesign; moduleId: string; onUpdate: (key: string, value: unknown) => void };
const label = (tr: string, en: string) => copy([tr,en]);

function ListEditor({ setting, moduleId, onUpdate, design }: Props) {
  const delimiter = moduleId === 'window-control' ? ',' : ';';
  const [draft, update] = useAutoSaveDraft(String(setting.value ?? setting.defaultValue ?? ''), value => onUpdate(setting.key, value));
  const [newValue, setNewValue] = useState('');
  const entries = draft.split(delimiter).map(s => s.trim()).filter(Boolean);
  const save = (values: string[]) => update(values.join(delimiter), true);
  const add = () => { const value = newValue.trim(); if (value && !value.includes(delimiter)) { save([...entries,value]); setNewValue(''); } };
  if (design === 'list') return <div className="structured-list">
    <div className="entry-chips">{entries.map((entry,index) => <span key={`${index}-${entry}`}><span>{entry}</span><button type="button" aria-label={`${entry}: ${label('Kaldır','Remove')}`} onClick={() => save(entries.filter((_,i) => i !== index))}><Icon name="close" size={13} /></button></span>)}</div>
    <div className="entry-add"><input className="white-control" value={newValue} onChange={e => setNewValue(e.target.value)} aria-label={`${setting.label}: ${label('Yeni öğe','New item')}`} placeholder={label('Yeni öğe yaz…','Enter a new item…')} onKeyDown={e => { if (e.key === 'Enter') { e.preventDefault(); add(); } }} />
      <button type="button" className="secondary-button" disabled={!newValue.trim() || newValue.includes(delimiter)} onClick={add}>{label('Ekle','Add')}</button></div>
    {!entries.length && <small>{label('Liste boş. İlk öğeyi yukarıdan ekleyebilirsin.','The list is empty. Add your first item above.')}</small>}
  </div>;
  return null;
}

function PairEditor({setting,onUpdate,design}:Props) {
  const separator=design==='commands'?'|':'=';
  const parse=(text:string)=>text.split(';').filter(row=>row.trim()).map(row=>{const i=row.indexOf(separator);return i<0?[row,'']:[row.slice(0,i).trim(),row.slice(i+1).trim()];});
  const validRow=(row:string[])=>row[0].trim() && row[1].trim() && !row.some(s=>s.includes(';')) && !row[0].includes(separator)
    && (design!=='grid-profiles'||/^[a-z0-9_-]{1,32}$/i.test(row[0].trim())&&/^[1-6]\s*[xX×]\s*[1-4]$/.test(row[1].trim()));
  const [rows,rowSave]=useAutoSaveDraft(parse(String(setting.value??setting.defaultValue??'')),rows=>{
    if(rows.every(validRow))onUpdate(setting.key,rows.map(row=>row.map((s,i)=>design==='grid-profiles'&&i===1?s.trim().replace(/[X×]/g,'x'):s.trim()).join(separator)).join(';'));
  });
  const invalid=rows.some(row=>!validRow(row));
  return <div className="mapping-editor">
    <div className="mapping-head"><span>{design === 'commands' ? label('Görünen ad','Display name') : design === 'grid-profiles' ? label('Ekran kimliği','Display identifier') : label('Kısaltma','Abbreviation')}</span><span>{design === 'commands' ? label('Komut','Command') : design === 'grid-profiles' ? label('Sütun × satır','Columns × rows') : label('Karşılığı','Expansion')}</span><span /></div>
    {rows.map((row,index) => <div className="mapping-row" key={index}>{row.map((value,col) => <input key={col} className="white-control" value={value} aria-label={`${setting.label}: ${index+1}. ${col === 0 ? label('kaynak','source') : label('hedef','target')}`}
      aria-invalid={!validRow(row)} placeholder={design === 'grid-profiles' ? col ? '3x2' : 'DISPLAY1' : undefined} onChange={event => rowSave(rows.map((r,i) => i === index ? r.map((v,j) => j === col ? event.target.value : v) : r))} />)}
      <button className="icon-button" type="button" aria-label={`${setting.label}: ${index+1}. ${label('satırı kaldır','remove row')}`} onClick={() => rowSave(rows.filter((_,i) => i !== index),true)}><Icon name="close" size={15} /></button></div>)}
    <button type="button" className="secondary-button" onClick={() => rowSave([...rows,['','']])}>{label('Satır ekle','Add row')}</button>
    {invalid&&<p className="draft-validation" role="status">{design==='grid-profiles'?label('Her ekran için 1–6 sütun ve 1–4 satır gir. Eksik satırlar tamamlanana kadar kaydedilmez.','Enter 1–6 columns and 1–4 rows per display. Incomplete rows are not saved.'):label('Her satırın iki alanını da doldur. Eksik satırlar tamamlanana kadar kaydedilmez.','Complete both fields in each row. Incomplete rows are not saved.')}</p>}
  </div>;
}

function Workflow({ setting,onUpdate }: Props) {
  const value = String(setting.value ?? setting.defaultValue ?? '');
  const parts = value.split('-');
  const names: Record<string,string> = {save:label('Kaydet','Save'),copy:label('Kopyala','Copy'),edit:label('Düzenle','Edit'),upload:label('Yükle','Upload')};
  const move = (index: number, direction: number) => { const next = [...parts]; [next[index],next[index+direction]]=[next[index+direction],next[index]]; const key=next.join('-'); if(setting.options.includes(key))onUpdate(setting.key,key); };
  return <ol className="workflow-editor">{parts.map((part,index) => <li key={part}><span className="workflow-number">{index+1}</span><strong>{names[part] ?? part}</strong>
    <button type="button" className="icon-button" disabled={index===0} aria-label={`${names[part]}: ${label('Öne taşı','Move earlier')}`} onClick={() => move(index,-1)}>↑</button>
    <button type="button" className="icon-button" disabled={index===parts.length-1} aria-label={`${names[part]}: ${label('Sona taşı','Move later')}`} onClick={() => move(index,1)}>↓</button></li>)}</ol>;
}

function JsonEditor({ setting,onUpdate }: Props) {
  const validRules=(text:string)=>{
    if(!text.trim())return true;
    try {
      const rules:unknown=JSON.parse(text);
      return Array.isArray(rules)&&rules.length<=100&&rules.every(rule=>{
        if(!rule||typeof rule!=='object')return false;
        const record=Object.fromEntries(Object.entries(rule).map(([k,v])=>[k.toLowerCase(),v]));
        return typeof record.selector==='string'&&record.selector.trim()&&record.properties&&typeof record.properties==='object'&&!Array.isArray(record.properties)
          &&Object.keys(record.properties).length>0&&Object.entries(record.properties).every(([key,value])=>key.trim()&&typeof value==='string');
      });
    }catch{return false;}
  };
  const [text,update] = useAutoSaveDraft(String(setting.value ?? setting.defaultValue ?? ''),value => { if(validRules(value))onUpdate(setting.key,value); });
  const valid=validRules(text);
  return <div className="json-editor"><textarea className="white-control" rows={10} value={text} spellCheck={false} aria-label={setting.label} aria-invalid={!valid} onChange={e => update(e.target.value)} />
    {!valid && <p role="status">{label('Geçerli bir kural listesi gir: her kuralda selector ve metin değerlerinden oluşan properties bulunmalı. En fazla 100 kural. Düzeltene kadar bu taslak uygulanmaz.','Enter a valid rule list: every rule needs selector and properties with string values. Maximum 100 rules. This draft is not applied until corrected.')}</p>}</div>;
}

function ClockInput({ setting,onUpdate }: Props) {
  const [value,update] = useAutoSaveDraft(String(setting.value ?? setting.defaultValue ?? ''), value => { if(/^([01]\d|2[0-3]):[0-5]\d$/.test(value))onUpdate(setting.key,value); });
  return <input type="time" className="white-control time-control" aria-label={setting.label} value={value} onChange={e => update(e.target.value,true)} />;
}

export function DesignedSetting(props: Props) {
  const { setting,design,onUpdate }=props;
  const id=useId();
  const value=String(setting.value ?? setting.defaultValue ?? '');
  const structuredHelp=design==='pairs'?label('Her satırda bir kısaltma ve yazılacak karşılığını düzenle.','Edit an abbreviation and its expansion in each row.')
    :design==='commands'?label('Ad menüde görünür. Komutta seçili dosya veya klasör için %1 kullanılabilir.','The name appears in the menu. Use %1 in a command for the selected file or folder.')
    :design==='grid-profiles'?label('Ekran kimliğini ve sütun × satır sayısını gir. Tanımlanmayan ekranlar genel düzeni kullanır.','Enter a display identifier and columns × rows. Unlisted displays use the general layout.')
    :design==='mouse-buttons'?label('Fareyle etkinleştirmek için basılı tutulacak orta veya yan tuşları seç.','Choose middle or side buttons to hold for mouse activation.')
    :design==='list'?setting.key==='Windows'?label('Başlık|sınıf kalıbı ekle. * ve ? jokerleri kullanılabilir.','Add a title|class pattern. The * and ? wildcards are supported.')
      :label('Her öğeyi ayrı ekle; yanındaki × düğmesiyle listeden kaldır.','Add each item separately; use its × button to remove it.')
    :setting.description;
  const heading=<div className="setting-copy"><strong>{setting.label}</strong>{structuredHelp && <p id={`${id}-help`}>{structuredHelp}</p>}</div>;
  let control;
  if(design==='time')control=<ClockInput {...props} />;
  else if(design==='workflow')control=<Workflow {...props} />;
  else if(design==='json')control=<JsonEditor {...props} />;
  else if(design==='list')control=<ListEditor {...props} />;
  else if(['pairs','commands','grid-profiles'].includes(design))control=<PairEditor {...props} />;
  else if(design==='mouse-buttons') {
    const selected=value.split(/\s+/).filter(Boolean);
    control=<div className="choice-tiles" role="group" aria-label={setting.label}>{[['04',label('Orta tuş','Middle button')],['05',label('Yan tuş 1','Side button 1')],['06',label('Yan tuş 2','Side button 2')]].map(([key,name]) => <button key={key} type="button" aria-pressed={selected.includes(key)} onClick={() => onUpdate(setting.key,(selected.includes(key) ? selected.filter(k => k!==key) : [...selected,key]).join(' '))}>{name}</button>)}</div>;
  } else {
    const positions:Record<string,number>={'Sağ alt':8,'Sol alt':6,'Sağ üst':2,'Sol üst':0,'Üst orta':1,'Alt orta':7,'Merkez':4,'bottom-right':8,'bottom-left':6,'top-right':2,'top-left':0,'top-center':1,'bottom-center':7,center:4};
    control=<div className={design==='position' ? 'position-picker' : 'choice-tiles'} role="group" aria-label={setting.label} aria-describedby={setting.description ? `${id}-help` : undefined}>
      {setting.options.map((option,index) => <button type="button" className={value===option ? 'selected' : ''} key={option} aria-pressed={value===option}
        style={design==='position' ? {gridArea:`${Math.floor((positions[option] ?? index)/3)+1} / ${(positions[option] ?? index)%3+1}`} : undefined}
        onClick={() => onUpdate(setting.key,option)}>
        {design==='tiles' && /layout/.test(setting.key) && <span className={`layout-mini layout-option-${index}`} aria-hidden="true"><i/><i/><i/><i/></span>}
        <span>{setting.optionLabels[option] ?? option}</span>{value===option && <Icon name="check" size={15} />}
      </button>)}
    </div>;
  }
  return <div className={`setting-row designed-setting designed-${design}`} data-setting-key={setting.key}>{heading}<div className="designed-control">{control}</div></div>;
}
