export class Drops {
  constructor(){this.reset();}
  reset(){this.windows={};}
  observe(q){const out=[];for(const key of ['fiveHour','weekly']){const w=q?.[key],old=this.windows[key];if(typeof w?.remaining!=='number'){delete this.windows[key];continue;}
    if(!old||w.remaining>old.last+1e-6||w.resetsAt!==old.reset){this.windows[key]={mark:w.remaining,last:w.remaining,reset:w.resetsAt};continue;}
    const n=Math.max(0,Math.min(100,Math.floor(old.mark-w.remaining+1e-6)));for(let i=0;i<n;i++)out.push(key);
    this.windows[key]={mark:old.mark-n,last:w.remaining,reset:w.resetsAt};}return out;}
}
export function nextClick(stage){return stage==='hidden'?'quota':stage==='quota'?'second':'hidden';}
export function fitPet(width,height,scale,left,top){const size=Math.max(1,Math.min(250*scale,width-12,height-12));return {size,left:Math.max(0,Math.min(left??width-size-16,width-size)),top:Math.max(0,Math.min(top??height-size-8,height-size))};}
export const percent=v=>typeof v==='number'?`${Math.round(v*10)/10}%`:'--';
export function resetText(w){if(!w?.resetsAt)return'重置时间未提供';const delta=w.resetsAt-Date.now();if(delta<=0)return'已到重置时间，等待更新';const mins=Math.ceil(delta/60000),h=Math.floor(mins/60),d=Math.floor(h/24);return `${d?d+'天 '+h%24+'小时':h?h+'小时 '+mins%60+'分':mins+'分钟'}后重置 · ${new Date(w.resetsAt).toLocaleString('zh-CN',{month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'})}`;}
