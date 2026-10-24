const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),os=require('node:os');
const ROOT=path.resolve(__dirname,'..');
process.env.CODEX_PET_APP_DATA=fs.mkdtempSync(path.join(os.tmpdir(),'codex-pet-app-tests-'));
process.env.CODEX_PET_APP_NO_IMPORT='1';
const {parse,ALLOWED,QuotaClient}=require(path.join(ROOT,'quota.cjs'));
let passed=0;function check(value,label){assert(value,label);console.log('PASS '+label);passed++;}
(async()=>{
 const {Drops,nextClick,fitPet}=await import(require('node:url').pathToFileURL(path.join(ROOT,'core.mjs')).href);
 for(const [w,h] of [[1280,800],[568,310],[280,200],[140,95]]){const b=fitPet(w,h,2.5,1200,700);check(b.left>=0&&b.top>=0&&b.left+b.size<=w&&b.top+b.size<=h,'Responsive pet stays completely inside '+w+'x'+h+' viewport');}
 const input={rateLimitsByLimitId:{codex:{primary:{usedPercent:26,windowDurationMins:10080,resetsAt:1793087603},secondary:{usedPercent:24,windowDurationMins:300,resetsAt:1792594903}}},rateLimitResetCredits:{availableCount:2}};
 const q=parse(input);check(q.fiveHour.remaining===76&&q.weekly.remaining===74&&q.resetCount===2,'Same quota pool, duration mapping and reset counts as desktop version');
 const partial=parse({rateLimits:{limitId:'codex',primary:{usedPercent:25,windowDurationMins:300,resetsAt:1792594903}}},q,true);
 check(partial.fiveHour.remaining===75&&partial.weekly.remaining===74&&partial.resetCount===2,'Partial pushes preserve weekly quota and reset credits');
 check(parse({rateLimits:{limitId:'other',primary:{usedPercent:0,windowDurationMins:300}}}).fiveHour.remaining===null,'Other pools cannot fabricate quota');
 check(!ALLOWED.has('turn/start')&&!ALLOWED.has('thread/start')&&!ALLOWED.has('account/rateLimitResetCredit/consume'),'Transport only allows metadata reads');
 const d=new Drops();check(d.observe(q).length===0&&d.observe(partial).length===1&&d.observe(partial).length===0,'First sample suppressed, each point triggers once, duplicates ignored');
 check(d.observe({...partial,fiveHour:{...partial.fiveHour,remaining:72}}).length===3,'Multiple percentage points queue separate reminders');
 check(d.observe(q).length===0,'Quota refill rebases without notifications');
 check(nextClick('hidden')==='quota'&&nextClick('quota')==='second'&&nextClick('second')==='hidden','Three-click quota / reset-or-quote / hidden cycle');
 const server=require(path.join(ROOT,'server.cjs'));
 try{
  const tools=(await server.handle('tools/list')).tools;
  const speeds=server.sanitize({breatheSeconds:0,ahogeSeconds:99});check(speeds.breatheSeconds===.1&&speeds.ahogeSeconds===6,'Breathing and ahoge sliders accept exactly 0.1–6 seconds');
  check(tools.length===5&&tools[0]._meta['openai/ui'].entrypoints.some(x=>x.type==='global')&&tools.slice(1).every(t=>t._meta.ui.visibility[0]==='app'),'Official global/thread app entrypoints; metadata tools visible only to app');
  await server.handle('tools/call',{name:'pet_update',arguments:{settings:{skin:'mutsumi',quoteChance:.4,scale:99,exe:'bad'},quotes:['自定义语录']}});
  const update=await server.handle('tools/call',{name:'pet_update',arguments:{settings:{skin:'dragon'}}});
  check(update._meta.pet.prefs.skin==='dragon'&&update._meta.pet.prefs.scale===2.5&&!('exe'in update._meta.pet.prefs)&&update._meta.pet.quotes[0]==='自定义语录','Only validated preferences are persisted; editable quotes preserved');
  const resource=(await server.handle('resources/read',{uri:'ui://codex-pet-app/main.html'})).contents[0];
  check(resource.text.includes('data:image/png;base64,')&&resource.text.includes('GPT娘 · 白龙')&&!resource.text.includes('CodexPet.exe'),'App embeds three skins and runs without the standalone pet executable');
  check(!resource.text.includes('sendFollowUpMessage')&&!resource.text.includes('turn/start'),'UI does not submit model chats or inference');
  const ui=fs.readFileSync(path.join(ROOT,'ui.js'),'utf8'),page=fs.readFileSync(path.join(ROOT,'ui.html'),'utf8');
  check(ui.includes("$('character').oncontextmenu")&&!page.includes('<header')&&!page.includes('id="refresh"'),'Only character right-click opens settings; no separate control toolbar');
  check(['edit-quotes','hide','reset','exit'].every(id=>page.includes('id="'+id+'"'))&&page.includes('id="quote-editor"'),'Original settings actions and separate quote editor retained');
  const manifest=JSON.parse(fs.readFileSync(path.join(ROOT,'plugin.json'),'utf8')),transport=fs.readFileSync(path.join(ROOT,'mcp.json'),'utf8');
  check(manifest.extensions['com.openai'].requires_local_executor===true&&transport.includes('${PLUGIN_ROOT}')&&!transport.includes('7449'),'Portable official local-executor declaration without developer HTTP port');
  const launch=JSON.parse(transport).mcpServers.petapp;
  check(launch.command.startsWith('./')&&!launch.extensions,'Launch config is accepted by the installed portable stdio parser');
  const backend=fs.readFileSync(path.join(ROOT,'server.cjs'),'utf8');
  check(!backend.includes('CodexWhalePet')&&!backend.includes("'CodexPet','settings.json'")&&!backend.includes('child_process'),'Plugin settings and runtime do not read or control the desktop pet');
  const mock=path.resolve(__dirname,'bin/Release/net10.0/CodexPet.Tests.exe');
  if(fs.existsSync(mock)){
    process.env.CODEX_PET_CODEX=mock;const client=new QuotaClient();
    const first=await client.read(true);const pid=client.child.pid;await new Promise(r=>setTimeout(r,500));
    check(first.fiveHour.remaining===75&&client.latest.fiveHour.remaining===74&&client.latest.weekly.remaining===74,'Persistent idle connection receives partial updates');
    await client.read(true);check(client.child.pid===pid,'Polling reuses the same read-only process');client.stop();delete process.env.CODEX_PET_CODEX;
  }
  if(process.argv.includes('--live')){const client=new QuotaClient();try{const live=await client.read(true);check(live.fiveHour.remaining>=0&&live.weekly.remaining>=0,'Actual installed Codex returns both quota windows without inference');}finally{client.stop();}}
 }finally{server.cleanup();}
 console.log(passed+' checks passed.');
})().catch(e=>{console.error(e.stack);process.exitCode=1;});
