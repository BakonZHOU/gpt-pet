const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),readline=require('node:readline');
const {QuotaClient}=require('./quota.cjs');
const ROOT=__dirname, URI='ui://codex-pet-app/main.html';
const DATA=process.env.CODEX_PET_APP_DATA ?? process.env.PLUGIN_DATA ?? path.join(process.env.LOCALAPPDATA ?? os.homedir(),'CodexPetApp');
const defaults={skin:'dragon',scale:.8,bubbles:true,topmost:true,animate:true,quoteChance:.15,breatheSeconds:4.4,breatheAmplitude:1,ahogeSeconds:2.8,ahogeAmplitude:1,sound:true,volume:.9,soundSet:'duck',left:null,top:null};
const ranges={scale:[.6,2.5],quoteChance:[0,1],breatheSeconds:[.1,6],breatheAmplitude:[0,4],ahogeSeconds:[.1,6],ahogeAmplitude:[0,4],volume:[0,1]};
function sanitize(input){const p={...defaults};for(const [k,v] of Object.entries(input??{})){if(Object.hasOwn(ranges,k) && typeof v==='number' && Number.isFinite(v))p[k]=Math.max(ranges[k][0],Math.min(ranges[k][1],v));else if(['bubbles','topmost','animate','sound'].includes(k)&&typeof v==='boolean')p[k]=v;else if(k==='skin'&&['whale','mutsumi','dragon'].includes(v))p[k]=v;else if(k==='soundSet'&&['duck','fx1'].includes(v))p[k]=v;else if(['left','top'].includes(k)&&(v===null||typeof v==='number'&&Number.isFinite(v)))p[k]=v;}return p;}
let prefs={...defaults},quotes=[];try{prefs=sanitize(JSON.parse(fs.readFileSync(path.join(DATA,'preferences.json'),'utf8')));}catch{}
try{quotes=JSON.parse(fs.readFileSync(path.join(DATA,'quotes.json'),'utf8'));}catch{quotes=fs.readFileSync(path.join(ROOT,'quotes.txt'),'utf8').split(/\r?\n/).map(s=>s.trim()).filter(s=>s&&!s.startsWith('#'));}
const client=new QuotaClient(),leases=new Map();let error=null,lastRead=0,watcher=null,debounce=null;
function save(file,value){fs.mkdirSync(DATA,{recursive:true});const target=path.join(DATA,file);fs.writeFileSync(target+'.tmp',JSON.stringify(value));fs.renameSync(target+'.tmp',target);}
function alive(){for(const [id,t] of leases)if(Date.now()-t>20000)leases.delete(id);return leases.size>0;}
async function refresh(force=false){if(!alive())return;try{await client.read(force);lastRead=Date.now();error=null;}catch(e){error=e.message;}}
function signal(){if(debounce||!alive())return;debounce=setTimeout(()=>{debounce=null;refresh(false);},Math.max(750,3000-(Date.now()-lastRead)));debounce.unref();}
function watch(){if(watcher)return;const dir=path.join(process.env.CODEX_HOME??path.join(os.homedir(),'.codex'),'sessions');if(!fs.existsSync(dir))return;try{watcher=fs.watch(dir,{recursive:true},(_,name)=>{if(name?.endsWith('.jsonl'))signal();});watcher.on('error',()=>{watcher?.close();watcher=null;});}catch{}}
function pause(){if(alive())return;client.stop();watcher?.close();watcher=null;if(debounce){clearTimeout(debounce);debounce=null;}}
const timer=setInterval(()=>{if(alive()){watch();refresh(false);}else pause();},10000);timer.unref();
function state(){return {prefs,quotes,quota:client.latest,error,lastRead,connection:client.child?'connected':'paused'};}
function html(){const assets={};for(const f of ['DSniang1.png','DSniang1-no-ahoge.png','Skins/Mutsumi.png','Skins/WhiteDragon.png','D1.mp3','D2.mp3','Ya1.mp3','Ya2.mp3'])assets[f]='data:'+(f.endsWith('.png')?'image/png':'audio/mpeg')+';base64,'+fs.readFileSync(path.join(ROOT,'assets',f)).toString('base64');return fs.readFileSync(path.join(ROOT,'ui.html'),'utf8').replace('/*__ASSETS__*/',()=>JSON.stringify(assets)).replace('/*__BUNDLE__*/',()=>fs.readFileSync(path.join(ROOT,'ui.bundle.js'),'utf8'));}
const tool=(name,title,description,inputSchema,ui=false)=>({name,title,description,inputSchema,annotations:{readOnlyHint:!['pet_update'].includes(name),destructiveHint:false,openWorldHint:false},_meta:ui?{ui:{resourceUri:URI},'openai/outputTemplate':URI,'openai/widgetAccessible':true,'openai/ui':{entrypoints:[{type:'global'},{type:'thread'}]}}:{ui:{visibility:['app']},'openai/widgetAccessible':true}});
const tools=[tool('pet_app','额度伙伴','在GPT中打开额度伙伴面板。皮肤、气泡和动画直接在面板内运行。',{type:'object',properties:{},additionalProperties:false},true),
tool('pet_state','读取伙伴状态','只读元数据，不调用模型。',{type:'object',properties:{clientId:{type:'string'}},required:['clientId'],additionalProperties:false}),
tool('pet_refresh','刷新额度','主动刷新5小时和每周额度，不消耗推理token。',{type:'object',properties:{clientId:{type:'string'}},required:['clientId'],additionalProperties:false}),
tool('pet_pause','暂停读取','面板隐藏后暂停读取。',{type:'object',properties:{clientId:{type:'string'}},required:['clientId'],additionalProperties:false}),
tool('pet_update','保存皮肤与语录','只保存本插件的本地偏好。',{type:'object',properties:{settings:{type:'object'},quotes:{type:'array',items:{type:'string'},maxItems:100}},additionalProperties:false})];
async function handle(method,params={}){
 if(method==='initialize')return{protocolVersion:params.protocolVersion??'2025-06-18',serverInfo:{name:'codex-pet-app',version:'0.2.2'},capabilities:{tools:{},resources:{}}};
 if(method==='ping')return{};if(method==='tools/list')return{tools};if(method==='resources/templates/list')return{resourceTemplates:[]};if(method==='resources/list')return{resources:[{uri:URI,name:'额度伙伴',mimeType:'text/html;profile=mcp-app'}]};
 if(method==='resources/read'&&params.uri===URI)return{contents:[{uri:URI,mimeType:'text/html;profile=mcp-app',text:html(),_meta:{ui:{prefersBorder:false,csp:{connectDomains:[],resourceDomains:[]}},'openai/ui':{availableDisplayModes:['inline','fullscreen','pip']}}}]};
 if(method!=='tools/call')throw Error('不支持的方法');const a=params.arguments??{};
 if(params.name==='pet_app')return{content:[{type:'text',text:'额度伙伴'}]};
 if(['pet_state','pet_refresh','pet_pause'].includes(params.name)){
   if(typeof a.clientId!=='string'||a.clientId.length<1||a.clientId.length>100)throw Error('面板标识无效');
   if(params.name==='pet_pause'){leases.delete(a.clientId);pause();}else{const first=!alive();leases.set(a.clientId,Date.now());watch();if(first||params.name==='pet_refresh'||!client.latest)await refresh(params.name==='pet_refresh');}
 }else if(params.name==='pet_update'){
   if(a.settings){prefs=sanitize({...prefs,...a.settings});save('preferences.json',prefs);}
   if(a.quotes){if(!Array.isArray(a.quotes)||a.quotes.length>100||a.quotes.some(s=>typeof s!=='string'||s.length>300))throw Error('语录格式无效');quotes=a.quotes.map(s=>s.trim()).filter(Boolean);save('quotes.json',quotes);}
 }else throw Error('未知工具');
 return{content:[{type:'text',text:'面板状态已更新'}],_meta:{pet:state()}};
}
function cleanup(){leases.clear();pause();clearInterval(timer);}
if(require.main===module){readline.createInterface({input:process.stdin}).on('line',async line=>{let m;try{m=JSON.parse(line);if(m.id==null)return;const result=await handle(m.method,m.params);process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:m.id,result})+'\n');}catch(e){if(m?.id!=null)process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:m.id,error:{code:-32603,message:e.message}})+'\n');}}).on('close',()=>{cleanup();process.exit(0);});process.on('SIGTERM',()=>{cleanup();process.exit(0);});}
module.exports={handle,html,cleanup,sanitize};
