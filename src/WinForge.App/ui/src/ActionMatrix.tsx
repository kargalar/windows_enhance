import type { Setting } from './types';
import { copy } from './moduleDesigns';

export function ActionMatrix({ settings,onUpdate }: { settings:Setting[];onUpdate:(key:string,value:unknown)=>void }) {
  const buttons=['LMB','RMB','MMB','MB4','MB5','Scroll','HScroll'];
  const names=[['Sol tuş','Left button'],['Sağ tuş','Right button'],['Orta tuş','Middle button'],['Yan tuş 4','Side button 4'],['Yan tuş 5','Side button 5'],['Dikey tekerlek','Vertical wheel'],['Yatay tekerlek','Horizontal wheel']] as const;
  return <div className="action-matrix-scroll"><table className="action-matrix"><thead><tr><th>{copy(['Fare girdisi','Mouse input'])}</th>{[['Normal','Normal'],['Alternatif','Alternate'],['Başlık çubuğu','Title bar'],['Başlık · alternatif','Title bar · alternate']].map(([tr,en])=><th key={en}>{copy([tr,en])}</th>)}</tr></thead><tbody>
    {buttons.map((button,index)=><tr key={button}><th scope="row">{copy(names[index])}</th>{['','B','T','TB'].map(suffix=>{
      const setting=settings.find(s=>s.key===button+suffix);return <td key={suffix}>{setting&&<div className="setting-copy" data-setting-key={setting.key}><strong className="sr-only">{setting.label}</strong><select className="white-control" value={String(setting.value??setting.defaultValue??'')} aria-label={`${copy(names[index])}: ${suffix==='B'?copy(['Alternatif','Alternate']):suffix==='T'?copy(['Başlık çubuğu','Title bar']):suffix==='TB'?copy(['Başlık · alternatif','Title bar · alternate']):copy(['Normal','Normal'])}`} onChange={e=>onUpdate(setting.key,e.target.value)}>{setting.options.map(option=><option key={option} value={option}>{setting.optionLabels[option]??option}</option>)}</select></div>}</td>;
    })}</tr>)}
  </tbody></table></div>;
}
