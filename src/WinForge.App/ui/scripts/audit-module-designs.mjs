import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createServer } from 'vite';

const uiRoot=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const workspace=path.resolve(uiRoot,'../../..');
const source=process.argv[2]?path.resolve(process.argv[2]):path.join(uiRoot,'e2e/fixtures/module-catalog.json');
const output=path.join(workspace,'artifacts/module-design-review');
const server=await createServer({root:uiRoot,cacheDir:path.join(uiRoot,'node_modules/.vite-design-audit'),optimizeDeps:{noDiscovery:true,include:[]},server:{middlewareMode:true,hmr:false,ws:false},appType:'custom'});
try{
 const {resolveDesign,controlDesign}=await server.ssrLoadModule('/src/moduleDesigns.ts');
 const catalog=JSON.parse((await fs.readFile(source,'utf8')).replace(/^\uFEFF/,''));
 const reports=[];
 for(const module of catalog.modules.filter(m=>m.id!=='fixture')){
  const {design,pages,unassigned}=resolveDesign(module);
  if(!design||unassigned.length)throw new Error(`Missing design coverage: ${module.id}: ${unassigned.map(s=>s.key).join(', ')}`);
  const keys=pages.flatMap(page=>page.groups.flatMap(g=>g.settings.map(s=>s.key)));
  if(keys.length!==module.settings.length||new Set(keys).size!==keys.length)throw new Error(`Settings coverage mismatch: ${module.id}`);
  reports.push({id:module.id,name:module.name,purpose:design.purpose[0],settings:keys.length,pages:pages.map(page=>({id:page.id,title:page.title[0],description:page.description[0],management:!!page.management,groups:page.groups.map(g=>({id:g.id,title:g.title[0],layout:g.layout??'rows',settings:g.settings.map(s=>({key:s.key,kind:s.kind,control:controlDesign(module.id,s)}))})),screenshot:`${module.id}-${page.id}.png`})),...(!pages.length?{screenshot:`${module.id}.png`}:{})});
 }
 await fs.mkdir(output,{recursive:true});
 const escaped=value=>String(value).replace(/[&<>"']/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
 const pageCount=reports.reduce((n,r)=>n+r.pages.length,0);
 await fs.writeFile(path.join(output,'coverage.json'),JSON.stringify({source,modules:reports.length,pages:pageCount,settings:reports.reduce((n,r)=>n+r.settings,0),reports},null,2));
 for(const report of reports)for(const screenshot of report.pages.length?report.pages.map(p=>p.screenshot):[report.screenshot])await fs.access(path.join(output,screenshot));
 await fs.writeFile(path.join(output,'index.html'),`<!doctype html><html lang="tr"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>WinForge · Modül tasarım incelemesi</title><style>body{margin:0;padding:36px;background:#f5f7fa;color:#192232;font:15px/1.6 'Segoe UI',sans-serif}main{max-width:1280px;margin:auto}h1{font-size:30px}header p{color:#536176}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:20px}article{padding:20px;background:white;border:1px solid #dce3ec;border-radius:12px}h2{font-size:19px;margin:0 0 8px}p{font-size:13px;color:#536176}img{display:block;width:100%;border:1px solid #e1e6ed;border-radius:7px;margin:14px 0}a{color:#1556c0}ul{padding-left:20px;font-size:13px}small{color:#536176}</style><main><header><h1>WinForge · Modül tasarım incelemesi</h1><p>${reports.length} modül · ${pageCount} tasarlanmış sayfa · ${reports.reduce((n,r)=>n+r.settings,0)} ayar. Görseller üretim paketi üzerinde tarayıcı testleriyle alındı. Yerel ve donanıma bağlı örnekler test verileriyle temsil edilir.</p></header><div class="grid">${reports.map(r=>`<article id="${escaped(r.id)}"><h2>${escaped(r.name)}</h2><small>${escaped(r.id)} · ${r.settings} ayar</small><p>${escaped(r.purpose)}</p><a href="${escaped(r.pages[0]?.screenshot??r.screenshot)}"><img loading="lazy" src="${escaped(r.pages[0]?.screenshot??r.screenshot)}" alt="${escaped(r.name)} detay sayfası"></a><ul>${r.pages.map(p=>`<li><a href="${escaped(p.screenshot)}">${escaped(p.title)}</a> · ${p.groups.reduce((n,g)=>n+g.settings.length,0)} ayar${p.management?' · özel yönetim paneli':''}</li>`).join('')}</ul></article>`).join('')}</div></main></html>`,'utf8');
 console.log(`PASS: ${reports.length} module designs, ${pageCount} pages, ${reports.reduce((n,r)=>n+r.settings,0)} settings; all review screenshots exist. ${path.join(output,'index.html')}`);
}finally{await server.close();}
