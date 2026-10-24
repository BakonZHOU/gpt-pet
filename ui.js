import {App} from '@modelcontextprotocol/ext-apps';
import {Drops,nextClick,percent,resetText,fitPet} from './core.mjs';
const $=id=>document.getElementById(id), A=window.PET_ASSETS, drops=new Drops();
const skins={whale:{body:'DSniang1-no-ahoge.png',hair:'DSniang1.png',bodyPath:'M0 0H610V610H0Z',rect:[237,10,170,60],pivot:[407,71],colors:['#536ba9','#203170','#9fb0d9','#ffffff']},
 mutsumi:{body:'Skins/Mutsumi.png',hair:'Skins/Mutsumi.png',bodyPath:'M0 110H610V603H0Z',rect:[0,0,610,110],pivot:[364,110],colors:['#556b57','#304b3a','#8a9b86','#f5f8ef']},
 dragon:{body:'Skins/WhiteDragon.png',hair:'Skins/WhiteDragon.png',bodyPath:'M0 0H610V603H0Z M232 0V110H403V0Z',rect:[232,0,171,110],pivot:[371,110],colors:['#77668f','#534264','#a59ab8','#f8f3fc']}};
let prefs={skin:'dragon',scale:.8,bubbles:true,animate:true,quoteChance:.15,breatheSeconds:4.4,breatheAmplitude:1,ahogeSeconds:2.8,ahogeAmplitude:1,sound:true,volume:.9,soundSet:'duck',left:null,top:null},quota=null,quotes=[],stage='hidden',weekly=false,closeTimer=null,alertTimer=null,alerts=[],hidden=false,fetching=false,callTool=null,app=null;
const clientId=crypto.randomUUID();
const sliderRows=[['scale','大小',.6,2.5,.1],['breatheSeconds','呼吸速度',.1,6,.1],['breatheAmplitude','呼吸幅度',0,4,.05],['ahogeSeconds','呆毛速度',.1,6,.1],['ahogeAmplitude','呆毛幅度',0,4,.05],['volume','音量',0,1,.05],['quoteChance','语录概率',0,1,.05]];
for(const [key,title,min,max,step] of sliderRows){const row=document.createElement('label');row.className='row';row.innerHTML=`<span>${title}</span><input id="${key}" type="range" min="${min}" max="${max}" step="${step}"><output id="${key}-value"></output>`;$('sliders').append(row);$(key).oninput=()=>{prefs[key]=+$(key).value;applyPrefs();};$(key).onchange=()=>save({[key]:prefs[key]});}
function applyPrefs(){
 const skin=skins[prefs.skin]??skins.whale;['ink','outline','hint','fill'].forEach((n,i)=>document.documentElement.style.setProperty('--'+n,skin.colors[i]));
 $('body-image').setAttribute('href',A[skin.body]);$('hair-image').setAttribute('href',A[skin.hair]);$('body-path').setAttribute('d',skin.bodyPath);
 ['x','y','width','height'].forEach((n,i)=>$('hair-rect').setAttribute(n,skin.rect[i]));$('hair').style.transformOrigin=skin.pivot.map(x=>x+'px').join(' ');
 const unit=fitPet($('board').clientWidth,$('board').clientHeight,prefs.scale,prefs.left,prefs.top).size;$('pet').style.width=$('pet').style.height=unit+'px';
 $('label').style.fontSize=unit*66/1026+'px';$('amount').style.fontSize=unit*(stage==='second'&&$('words').classList.contains('quote')&&!$('words').classList.contains('short')?66:128)/1026+'px';$('hint').style.fontSize=unit*56/1026+'px';
 for(const [key]of sliderRows){$(key).value=prefs[key];$(key+'-value').value=key.endsWith('Seconds')?prefs[key].toFixed(1)+'s':['quoteChance','volume','breatheAmplitude','ahogeAmplitude'].includes(key)?Math.round(prefs[key]*100)+'%':String(Math.round(prefs[key]*10)/10);}
 for(const key of ['skin','soundSet'])$(key).value=prefs[key];for(const key of ['bubbles','sound','animate'])$(key).checked=prefs[key];
 document.documentElement.style.setProperty('--breath',prefs.breatheSeconds+'s');document.documentElement.style.setProperty('--hair',prefs.ahogeSeconds+'s');document.documentElement.style.setProperty('--amp',prefs.breatheAmplitude);document.documentElement.style.setProperty('--hairamp',prefs.ahogeAmplitude);document.body.classList.toggle('still',!prefs.animate);place();if(!prefs.bubbles)hideBubble();
}
function place(){const board=$('board'),pet=$('pet'),box=fitPet(board.clientWidth,board.clientHeight,prefs.scale,prefs.left,prefs.top);pet.style.left=box.left+'px';pet.style.top=box.top+'px';pet.classList.toggle('mirror',box.left<board.clientWidth/4);if(!$('settings').hidden)positionMenu();}
function positionMenu(){const panel=$('settings'),anchor=$('character').getBoundingClientRect();panel.style.maxHeight=Math.max(60,innerHeight-16)+'px';panel.style.right='auto';panel.style.left=Math.max(8,Math.min(anchor.right-panel.offsetWidth,innerWidth-panel.offsetWidth-8))+'px';panel.style.top=Math.max(8,Math.min(anchor.top-panel.offsetHeight-8,innerHeight-panel.offsetHeight-8))+'px';}
function lines(label,amount,hint=''){for(const [id,v]of Object.entries({label,amount,hint})){$(id).textContent=v;$(id).hidden=!v;}}
function renderQuota(){if(stage!=='quota')return;lines(weekly?'每周剩余额度':'5小时剩余额度',percent(weekly?quota?.weekly.remaining:quota?.fiveHour.remaining),weekly?`5小时剩余 ${percent(quota?.fiveHour.remaining)}`:`每周剩余 ${percent(quota?.weekly.remaining)}`);}
function openBubble(){if(!prefs.bubbles)return;$('bubble').hidden=false;$('bubble').classList.remove('pop');void $('bubble').offsetWidth;if(prefs.animate)$('bubble').classList.add('pop');clearTimeout(closeTimer);closeTimer=setTimeout(hideBubble,5000);}
function hideBubble(){clearTimeout(closeTimer);stage='hidden';$('bubble').hidden=true;}
function stopAlerts(){clearTimeout(alertTimer);alertTimer=null;alerts=[];}
function quotaBubble(key='fiveHour'){stage='quota';weekly=key==='weekly';$('words').className='';renderQuota();applyPrefs();openBubble();}
function processAlerts(){if(alertTimer||!alerts.length||hidden||!prefs.bubbles)return;if(!$('settings').hidden||drag){alertTimer=setTimeout(()=>{alertTimer=null;processAlerts();},200);return;}quotaBubble(alerts.shift());alertTimer=setTimeout(()=>{alertTimer=null;processAlerts();},5500);}
function click(){stopAlerts();stage=nextClick(stage);if(stage==='hidden')return hideBubble();if(stage==='quota'){quotaBubble();refresh(true);return;}
 if(quotes.length&&Math.random()<prefs.quoteChance){const quote=quotes[Math.floor(Math.random()*quotes.length)];$('words').className='quote'+(quote.length<=9?' short':'');lines('',quote);}else{$('words').className='';lines('剩余重置次数',Number.isInteger(quota?.resetCount)?quota.resetCount+' 次':'--');}applyPrefs();openBubble();}
function accept(value){if(!value)return;const old=quota;quota=value;const changes=drops.observe(value);if(old&&['fiveHour','weekly'].some(k=>old[k]?.resetsAt!==value[k]?.resetsAt||value[k]?.remaining>old[k]?.remaining))stopAlerts();if(prefs.bubbles){alerts.push(...changes);processAlerts();}renderQuota();}
function consume(result,initial=false){if(result?.isError)throw Error(result.content?.find(x=>x.type==='text')?.text??'操作失败');const s=result?._meta?.pet??result?.structuredContent?.pet;if(!s)return;
 if(initial){prefs={...prefs,...s.prefs};quotes=s.quotes??[];$('quotes').value=quotes.join('\n');applyPrefs();}accept(s.quota);$('status').textContent=s.error?'离线 · 可重试':s.quota?'已更新 '+new Date(s.quota.fetchedAt).toLocaleTimeString('zh-CN',{hour:'2-digit',minute:'2-digit',second:'2-digit'}):'读取中…';$('reset-info').textContent='5小时：'+resetText(quota?.fiveHour)+'\n每周：'+resetText(quota?.weekly);}
async function refresh(force=false,initial=false){if(fetching||hidden||document.hidden||!callTool)return;fetching=true;try{consume(await callTool(force?'pet_refresh':'pet_state',{clientId}),initial);}catch(e){$('status').textContent='离线 · 点击刷新';}finally{fetching=false;}}
async function save(settings,updatedQuotes){if(!callTool)return;try{const args={};if(settings)args.settings=settings;if(updatedQuotes)args.quotes=updatedQuotes;consume(await callTool('pet_update',args));$('status').textContent='已保存';}catch{$('status').textContent='保存失败，请重试';}}
let drag=null,moved=false;
$('character').onpointerdown=e=>{if(e.button!==0)return;drag={x:e.clientX,y:e.clientY,left:parseFloat($('pet').style.left),top:parseFloat($('pet').style.top)};moved=false;$('character').setPointerCapture(e.pointerId);$('character').classList.add('pressed');play(true);};
$('character').onpointermove=e=>{if(!drag)return;const dx=e.clientX-drag.x,dy=e.clientY-drag.y;if(Math.hypot(dx,dy)>3)moved=true;if(moved){prefs.left=drag.left+dx;prefs.top=drag.top+dy;place();}};
$('character').onpointerup=()=>{if(!drag)return;drag=null;$('character').classList.remove('pressed');play(false);if(moved){const x=parseFloat($('pet').style.left),b=$('board');if(x<28)prefs.left=0;else if(b.clientWidth-x-$('pet').offsetWidth<28)prefs.left=b.clientWidth-$('pet').offsetWidth;place();save({left:prefs.left,top:prefs.top});}else click();};
$('character').onpointercancel=()=>{drag=null;$('character').classList.remove('pressed');};
function play(down){if(!prefs.sound||!prefs.volume)return;const file=prefs.soundSet==='fx1'?(down?'D1.mp3':'D2.mp3'):(down?'Ya1.mp3':'Ya2.mp3');const audio=new Audio(A[file]);audio.volume=prefs.volume;audio.play().catch(()=>{});}
$('character').onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();click();}};
$('bubble').onclick=click;$('bubble').onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();click();}};
$('pet').oncontextmenu=e=>{e.preventDefault();$('settings').hidden=false;positionMenu();};$('close-menu').onclick=()=>$('settings').hidden=true;
document.addEventListener('pointerdown',e=>{if(!$('settings').hidden&&!$('settings').contains(e.target))$('settings').hidden=true;});document.addEventListener('keydown',e=>{if(e.key==='Escape')$('settings').hidden=true;});
for(const key of ['skin','soundSet','bubbles','sound','animate'])$(key).onchange=()=>{prefs[key]=['bubbles','sound','animate'].includes(key)?$(key).checked:$(key).value;applyPrefs();save({[key]:prefs[key]});};
$('save-quotes').onclick=()=>{quotes=$('quotes').value.split(/\r?\n/).map(s=>s.trim()).filter(Boolean);save(null,quotes);};
$('refresh').onclick=()=>refresh(true);$('reset').onclick=()=>{stopAlerts();stage='second';$('words').className='quote';lines('下次重置',resetText(quota?.fiveHour),'每周 '+(quota?.weekly?.resetsAt?new Date(quota.weekly.resetsAt).toLocaleString('zh-CN'):'--'));applyPrefs();openBubble();$('settings').hidden=true;};
$('hide').onclick=()=>{hidden=true;hideBubble();stopAlerts();drops.reset();$('pet').hidden=true;$('empty').hidden=false;$('show').hidden=false;$('settings').hidden=true;callTool?.('pet_pause',{clientId}).catch(()=>{});};
$('show').onclick=()=>{hidden=false;$('pet').hidden=false;$('empty').hidden=true;$('show').hidden=true;refresh(true);};
document.addEventListener('visibilitychange',()=>{if(document.hidden){drops.reset();stopAlerts();callTool?.('pet_pause',{clientId}).catch(()=>{});}else refresh(false);});window.addEventListener('pagehide',()=>callTool?.('pet_pause',{clientId}).catch(()=>{}));new ResizeObserver(()=>applyPrefs()).observe($('board'));
async function connect(){
 if(window.PET_PREVIEW_BRIDGE){callTool=window.PET_PREVIEW_BRIDGE;}
 else if(window.parent!==window){try{app=new App({name:'quota-companion',version:'0.1.1'},{},{autoResize:false});app.ontoolresult=r=>consume(r);await app.connect();callTool=(name,args)=>app.callServerTool({name,arguments:args});const modes=app.getHostContext()?.availableDisplayModes;if(modes?.includes('pip')){$('pip').hidden=false;$('pip').onclick=()=>app.requestDisplayMode({mode:'pip'});}}catch{if(window.openai?.callTool)callTool=(n,a)=>window.openai.callTool(n,a);}}
 else if(window.openai?.callTool)callTool=(n,a)=>window.openai.callTool(n,a);
 if(!callTool){$('status').textContent='请从GPT的插件入口打开';return;}await refresh(false,true);setInterval(()=>refresh(false),3000);
}
applyPrefs();connect();
