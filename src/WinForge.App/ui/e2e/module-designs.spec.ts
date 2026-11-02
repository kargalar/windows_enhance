import { test,expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import catalog from './fixtures/module-catalog.json' with {type:'json'};
import { moduleDesigns,resolveDesign } from '../src/moduleDesigns';
import type { AppState,Module } from '../src/types';
import { nativeHarness } from './nativeHarness';

const modules=catalog.modules as Module[];
test('all discovered modules have a complete purpose-built design with every native key assigned exactly once',()=>{
  expect(modules.length).toBe(47);
  for(const module of modules){
    const {design,pages,unassigned}=resolveDesign(module);
    expect(design, module.id).toBeTruthy();
    expect(design!.purpose[0].length,module.id).toBeGreaterThan(20);
    expect(design!.steps.length,module.id).toBeGreaterThan(0);
    expect(unassigned.map(s=>s.key),module.id).toEqual([]);
    const keys=pages.flatMap(p=>p.groups.flatMap(g=>g.settings.map(s=>s.key)));
    expect(keys.slice().sort(),module.id).toEqual(module.settings.map(s=>s.key).sort());
    expect(new Set(keys).size,module.id).toBe(keys.length);
    expect(Object.keys(moduleDesigns)).toContain(module.id);
  }
});

for(const module of modules) test(`${module.id}: every designed page and control is usable`,async({page},testInfo)=>{
  test.setTimeout(60000);
  const errors:string[]=[];page.on('pageerror',error=>errors.push(error.message));
  await nativeHarness(page,{...catalog,modules:[module]} as AppState);
  await page.goto('/');
  await page.locator('.tool-card-open').click();
  await expect(page.locator('[data-module-design]')).toHaveAttribute('data-module-design',module.id);
  const {pages}=resolveDesign(module);
  for(const section of pages){
    if(pages.length>1)await page.locator('.settings-navigation button').filter({hasText:section.title[0]}).click();
    const content=page.locator(`.page-${section.id}`);await expect(content).toBeVisible();
    if(section.management){
      await expect(page.locator('.panel-loading')).toHaveCount(0);
      if(module.id==='automation')await expect(content.locator('.automation-master')).toBeVisible();
      if(module.panel&&'kind' in module.panel&&module.panel.kind==='shortcut-actions'){
        await expect(content.locator('.shortcut-workspace')).toBeVisible();
        await expect(content.locator('.rule-index button')).toHaveCount(module.panel.rules.length);
      }
      if(module.id==='context-menu')await expect(content.locator('.workspace-tabs')).toBeVisible();
      if(module.id==='default-paths')await expect(content.locator('.paths-workspace')).toBeVisible();
      if(module.id==='default-apps')await expect(content.locator('.apps-workspace')).toBeVisible();
      const rules=content.locator('.rule-index button');
      for(let i=0;i<await rules.count();i++){
        await rules.nth(i).click();
        await expect(content.locator('[data-rule-editor]:visible')).toHaveCount(1);
        expect(await content.locator('[data-rule-editor]:visible input,[data-rule-editor]:visible select').count()).toBeGreaterThan(0);
        const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();expect(scan.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>n.target)}))).toEqual([]);
      }
      if(await rules.count())await rules.first().click();
      if(await rules.count()){
        const query=page.locator('.details-toolbar input[type=search]');
        const firstName=await rules.first().locator('strong').textContent();
        await query.fill(firstName!);
        await expect(rules).toHaveCount(1);
        await expect(content.locator('[data-rule-editor]:visible')).toHaveCount(1);
        await query.fill('');
      }
      if(module.id==='context-menu'){
        const tabs=content.locator('.workspace-tabs button');
        for(let i=0;i<await tabs.count();i++){await tabs.nth(i).click();const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();expect(scan.violations.map(v=>v.id)).toEqual([]);}
        await tabs.first().click();
      }
    }
    for(const setting of section.groups.flatMap(g=>g.settings)){
      const control=content.locator(`[data-setting-key="${setting.key}"]`);await expect(control).toHaveCount(1);
      await control.scrollIntoViewIfNeeded();await expect(control).toBeVisible();
      expect(await control.locator('input,select,button,textarea').count(),`${module.id}/${setting.key}`).toBeGreaterThan(0);
    }
    // Each page is scanned, including initially off-screen content.
    const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();
    expect(scan.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})),`${module.id}/${section.id}`).toEqual([]);
    await page.locator('.details-scroll').evaluate(el=>el.scrollTop=0);
    await page.screenshot({path:`../../../artifacts/module-design-review/${module.id}-${section.id}.png`});
  }
  if(!pages.length){await expect(page.locator('.module-operation-card')).toBeVisible();await page.screenshot({path:`../../../artifacts/module-design-review/${module.id}.png`});}
  await page.locator('.dashboard-actions .secondary-button').click();
  await expect(page.locator('.dark-dashboard')).toBeVisible();
  for(const section of pages){
    if(pages.length>1)await page.locator('.settings-navigation button').filter({hasText:section.title[0]}).click();
    const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();
    expect(scan.violations.map(v=>({id:v.id,nodes:v.nodes.map(n=>({target:n.target,summary:n.failureSummary}))})),`${module.id}/${section.id}/dark`).toEqual([]);
  }
  if(!pages.length){const scan=await new AxeBuilder({page}).withTags(['wcag2a','wcag2aa','wcag21aa']).analyze();expect(scan.violations.map(v=>v.id)).toEqual([]);}
  for(const width of [980,560]){
    await page.setViewportSize({width,height:800});
    expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`${module.id}/${width}`).toBe(true);
    if(pages.length){
      const scroll=page.locator('.details-scroll');
      expect(await scroll.evaluate(el=>el.scrollWidth<=el.clientWidth+2),`${module.id}/${width} content overflow`).toBe(true);
    }
  }
  expect(errors).toEqual([]);
  await testInfo.attach('design-coverage',{body:JSON.stringify({module:module.id,pages:pages.map(p=>({id:p.id,keys:p.groups.flatMap(g=>g.settings.map(s=>s.key))}))},null,2),contentType:'application/json'});
});
