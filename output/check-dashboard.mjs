import { chromium } from 'file:///C:/Users/kargalar/AppData/Local/npm-cache/_npx/420ff84f11983ee5/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
const browser = await chromium.launch({headless:true});
try {
 const page = await browser.newPage({viewport:{width:1586,height:944}});
 const errors=[]; page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:5173');
 await page.locator('.tool-card').first().waitFor();
 assert.equal(await page.locator('.tool-card').count(),3);
 await page.getByRole('button',{name:'Görünüm',exact:true}).last().click();
 assert.equal(await page.locator('.tool-card').count(),1);
 await page.getByRole('button',{name:'Tümü',exact:true}).click();
 await page.locator('.tool-favorite').first().click();
 await page.getByRole('button',{name:'Favoriler',exact:true}).click();
 assert.equal(await page.locator('.tool-card').count(),1);
 await page.reload(); await page.locator('.tool-card').first().waitFor();
 assert.equal(await page.locator('.tool-favorite.selected').count(),1);
 await page.getByRole('button',{name:'Genel bakış',exact:true}).click();
 await page.locator('.tool-settings-button').first().click();
 assert.equal(await page.locator('#module-details h2').textContent(),'Görev çubuğu araçları');
 await page.getByRole('button',{name:'Modüllere dön'}).click();
 await page.getByRole('textbox',{name:'Araç veya ayar ara'}).fill('parlaklık');
 assert.equal(await page.locator('.tool-card').count(),1);
 await page.getByRole('textbox',{name:'Araç veya ayar ara'}).fill('');
 await page.keyboard.press('Control+f');
 assert.equal(await page.locator('#module-search').evaluate(e=>e===document.activeElement),true);
 await page.screenshot({path:'output/dashboard-light.png'});
 for(const width of [1280,980,600]) {
  await page.setViewportSize({width,height:860});
  assert.equal(await page.locator('.dashboard-main').evaluate(e=>e.scrollWidth<=e.clientWidth),true);
 }
 assert.deepEqual(errors,[]);
 console.log('PASS: categories, favorites and persistence, settings navigation, search, Ctrl+F, responsive widths; no runtime errors.');
} finally { await browser.close(); }

