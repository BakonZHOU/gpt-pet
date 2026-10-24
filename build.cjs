const fs=require('node:fs'),path=require('node:path'),esbuild=require('esbuild');
(async()=>{
 if(process.platform!=='win32'||process.arch!=='x64')throw Error('This package bundles a Windows x64 runtime; build it on Windows x64.');
 await esbuild.build({entryPoints:['ui.js'],bundle:true,format:'iife',platform:'browser',target:'es2022',outfile:'ui.bundle.js',minify:true,legalComments:'eof'});
 fs.mkdirSync('runtime',{recursive:true});fs.copyFileSync(process.execPath,path.join('runtime','node.exe'));
 const response=await fetch(`https://raw.githubusercontent.com/nodejs/node/${process.version}/LICENSE`);if(!response.ok)throw Error('Node license download failed');
 fs.writeFileSync(path.join('runtime','LICENSE.txt'),await response.text());console.log('Bundled MCP App and Node runtime.');
})().catch(e=>{console.error(e.message);process.exitCode=1;});