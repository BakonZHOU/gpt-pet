// Decode bundled clips directly; no media URL or network request is needed.
export class SoundPlayer {
 constructor(assets,createContext=()=>new (window.AudioContext??window.webkitAudioContext)()){
  this.assets=assets;this.createContext=createContext;this.context=null;this.buffers=new Map();this.sources=new Set();this.epoch=0;
 }
 async play(file,volume){
  if(!(volume>0))return false;
  const epoch=this.epoch,ctx=this.context??=this.createContext();
  // Resume inside the pointer/key event before awaiting decoding.
  const resumed=ctx.state==='running'?Promise.resolve():ctx.resume();
  let decoded=this.buffers.get(file);
  if(!decoded){
   const asset=this.assets[file];if(!asset)throw Error('音效文件缺失');
   const bytes=Uint8Array.from(atob(asset.slice(asset.indexOf(',')+1)),c=>c.charCodeAt(0));
   decoded=ctx.decodeAudioData(bytes.buffer).catch(error=>{this.buffers.delete(file);throw error;});this.buffers.set(file,decoded);
  }
  const [buffer]=await Promise.all([decoded,resumed]);
  if(epoch!==this.epoch)return false;
  if(ctx.state!=='running')throw Error('宿主暂停了音频');
  const source=ctx.createBufferSource(),gain=ctx.createGain();source.buffer=buffer;gain.gain.value=Math.max(0,Math.min(1,volume));
  source.connect(gain);gain.connect(ctx.destination);this.sources.add(source);
  source.onended=()=>{source.disconnect();gain.disconnect();this.sources.delete(source);};source.start();return true;
 }
 stop(){this.epoch++;for(const source of this.sources){try{source.stop();}catch{}}}
}
