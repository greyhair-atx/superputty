const {chromium} = require('playwright');
const {spawn} = require('node:child_process');
const path = require('node:path');
const fs = require('node:fs');
const assert = require('node:assert/strict');
(async () => {
  const server = spawn(process.execPath, [path.join(__dirname, 'Preview-Site.cjs')], {env:{...process.env,PORT:'18080'},stdio:['ignore','pipe','inherit'],windowsHide:true});
  let browser;
  try {
    await new Promise((resolve,reject) => {server.stdout.once('data',resolve);server.once('error',reject);server.once('exit',code=>reject(new Error('Preview exited '+code)));});
    browser=await chromium.launch({channel:'msedge'});
    const out=path.resolve(__dirname,'../artifacts/launch-review');fs.mkdirSync(out,{recursive:true});
    for(const [name,width,colorScheme] of [['desktop',1280,'light'],['mobile',390,'light'],['dark',1280,'dark']]){
      const page=await browser.newPage({viewport:{width,height:900},colorScheme});
      const errors=[];page.on('pageerror',error=>errors.push(error.message));
      page.on('request',request=>{if(!request.url().startsWith('http://127.0.0.1:18080/')) errors.push('External request: '+request.url());});
      await page.goto('http://127.0.0.1:18080/',{waitUntil:'networkidle'});
      await page.evaluate(()=>{for(const image of document.images)image.loading='eager';});
      await page.locator('footer').scrollIntoViewIfNeeded();
      await page.evaluate(()=>Promise.all([...document.images].map(i=>i.decode())));
      assert(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Horizontal overflow');
      assert(await page.locator('h1').isVisible());
      assert.equal(await page.locator('a.button').getAttribute('href'),'https://github.com/greyhair-atx/superputty/releases/latest');
      assert.deepEqual(errors,[]);
      await page.evaluate(()=>scrollTo(0,0));
      await page.screenshot({path:path.join(out,'site-'+name+'.png'),fullPage:true});
      console.log(name+': images loaded, no overflow, no external requests or browser errors');await page.close();
    }
  } finally {if(browser)await browser.close();server.kill();}
})().catch(error=>{console.error(error);process.exitCode=1;});
