const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const {spawn} = require('node:child_process');
const {createInterface} = require('node:readline');
const ALLOWED = new Set(['initialize','initialized','account/rateLimits/read']);
const time = n => Number.isSafeInteger(n) && Math.abs(n) < 8640000000000 ? n * 1000 : null;
function bucketOf(r) { return r.rateLimitsByLimitId?.codex ?? (!r.rateLimits?.limitId || r.rateLimits.limitId === 'codex' ? r.rateLimits : null); }
function parse(r, previous = null, partial = false) {
  const bucket = bucketOf(r), windows = [bucket?.primary,bucket?.secondary];
  function window(minutes,key) {
    const w = windows.find(w => w?.windowDurationMins === minutes);
    if (!w && partial && previous) return previous[key];
    return {remaining:typeof w?.usedPercent === 'number' && Number.isFinite(w.usedPercent) ? Math.max(0,Math.min(100,100-w.usedPercent)) : null, resetsAt:time(w?.resetsAt)};
  }
  const credits = r.rateLimitResetCredits;
  const keep = partial && previous && !Object.hasOwn(r,'rateLimitResetCredits');
  const expiry = credits?.credits?.filter(c=>c.status==='available').map(c=>time(c.expiresAt)).filter(v=>v!==null) ?? [];
  return {fiveHour:window(300,'fiveHour'),weekly:window(10080,'weekly'),resetCount:keep ? previous.resetCount : Number.isInteger(credits?.availableCount) && credits.availableCount>=0 ? credits.availableCount : null,
    firstCreditExpiry:keep ? previous.firstCreditExpiry : expiry.length ? Math.min(...expiry) : null,fetchedAt:Date.now()};
}
function findCodex() {
  if (process.env.CODEX_PET_CODEX) { if(fs.existsSync(process.env.CODEX_PET_CODEX))return process.env.CODEX_PET_CODEX;throw Error('用量服务路径无效'); }
  const root = path.join(process.env.LOCALAPPDATA ?? path.join(os.homedir(),'AppData','Local'),'OpenAI','Codex','bin');
  const candidates = [];
  if(fs.existsSync(root)) for(const d of fs.readdirSync(root,{withFileTypes:true})) {
    const exe = path.join(root,d.name,'codex.exe'); if(d.isDirectory() && fs.existsSync(exe))candidates.push(exe);
  }
  candidates.sort((a,b)=>fs.statSync(b).mtimeMs-fs.statSync(a).mtimeMs);
  for(const d of (process.env.PATH??'').split(path.delimiter)){const exe=path.join(d.replace(/^"|"$/g,''),'codex.exe');if(fs.existsSync(exe))candidates.push(exe);}
  if(!candidates.length)throw Error('未找到Codex用量服务，请先安装并登录Codex');
  return candidates[0];
}
class QuotaClient {
  constructor(){this.child=null;this.pending=new Map();this.nextId=0;this.latest=null;this.reading=null;this.lastRead=0;this.methods=[];}
  send(message){if(!ALLOWED.has(message.method))throw Error('只允许读取额度元数据');this.methods.push(message.method);if(this.methods.length>20)this.methods.shift();this.child.stdin.write(JSON.stringify(message)+'\n');}
  request(method,params){const id=++this.nextId;return new Promise((resolve,reject)=>{const timer=setTimeout(()=>{this.pending.delete(id);reject(Error('额度查询超时'));this.stop();},30000);this.pending.set(id,{resolve,reject,timer});try{this.send({id,method,params});}catch(e){clearTimeout(timer);this.pending.delete(id);reject(e);}});}
  async start(){
    if(this.child && this.child.exitCode===null && !this.child.killed)return;
    this.stop();const child=spawn(findCodex(),['app-server','--listen','stdio://'],{windowsHide:true,stdio:['pipe','pipe','pipe']});this.child=child;
    child.stderr.on('data',()=>{});child.stdin.on('error',()=>{});
    const fail=()=>{if(this.child===child)this.stop();};child.on('error',fail);child.on('exit',fail);
    createInterface({input:child.stdout}).on('line',line=>{let m;try{m=JSON.parse(line);}catch{return;}
      const waiter=this.pending.get(m.id);if(waiter){clearTimeout(waiter.timer);this.pending.delete(m.id);m.error?waiter.reject(Error('Codex暂未提供额度，请检查登录状态')):waiter.resolve(m.result);}
      else if(m.method==='account/rateLimits/updated'){this.latest=parse(m.params??{},this.latest,true);this.onChange?.(this.latest);}
    });
    await this.request('initialize',{clientInfo:{name:'codex_pet_app',title:'Quota Companion',version:'0.2.1'}});this.send({method:'initialized'});
  }
  async read(force=false){
    if(this.reading)return this.reading;if(!force && this.latest && Date.now()-this.lastRead<3000)return this.latest;
    this.reading=(async()=>{await this.start();const value=parse(await this.request('account/rateLimits/read'));this.latest=value;this.lastRead=Date.now();return value;})();
    try{return await this.reading;}finally{this.reading=null;}
  }
  stop(){const c=this.child;this.child=null;for(const w of this.pending.values()){clearTimeout(w.timer);w.reject(Error('用量连接已暂停'));}this.pending.clear();if(c&&!c.killed)c.kill();}
}
module.exports={QuotaClient,parse,ALLOWED};
