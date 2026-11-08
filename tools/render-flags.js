
const fs=require('fs'), path=require('path'), sharp=require('sharp');
const dir='vendor/flag-icons/flags/4x3';
const out='build/flags';
fs.mkdirSync(out,{recursive:true});
const files=fs.readdirSync(dir).filter(f=>f.endsWith('.svg')).sort();
(async()=>{
  let n=0;
  for (const f of files) {
    const cc=path.basename(f,'.svg').toUpperCase();
    const png = await sharp(path.join(dir,f)).resize(40,30,{fit:'fill'}).png({compressionLevel:9,palette:true,colors:64}).toBuffer();
    fs.writeFileSync(path.join(out, cc+'.png'), png);
    n++;
  }
  console.log('rendered',n);
})();
