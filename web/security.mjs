export function imageDimensions(bytes){
  const b=bytes instanceof Uint8Array?bytes:new Uint8Array(bytes),view=new DataView(b.buffer,b.byteOffset,b.byteLength);let width=0,height=0;
  if(b.length>=33&&[137,80,78,71,13,10,26,10].every((x,i)=>b[i]===x)&&view.getUint32(8)===13&&String.fromCharCode(...b.slice(12,16))==='IHDR'){
    width=view.getUint32(16);height=view.getUint32(20);
  }else if(b.length>=4&&b[0]===255&&b[1]===216){
    let p=2;while(p+4<=b.length){if(b[p++]!==255)throw Error('Invalid image header.');while(p<b.length&&b[p]===255)p++;const marker=b[p++];if(marker===217||marker===218)break;if(marker===1||marker>=208&&marker<=215)continue;if(p+2>b.length)break;const size=view.getUint16(p);if(size<2||p+size>b.length)break;
      if([192,193,194,195,197,198,199,201,202,203,205,206,207].includes(marker)){if(size<8)break;height=view.getUint16(p+3);width=view.getUint16(p+5);break;}p+=size;
    }
  }
  if(!width||!height||width>8192||height>8192||width*height>16777216)throw Error('Use a PNG/JPEG with sides up to 8192 pixels and at most 16 megapixels.');
  return {width,height};
}
export function messageContent(text){
  let c;try{c=JSON.parse(text);}catch{}
  if(!['illusion.message.v1','illusion.message.v2'].includes(c?.format)){if(text.length>12000)throw Error('Message text is too large.');return {text};}
  if(typeof c.text!=='string'||c.text.length>2000)throw Error('Invalid message text.');
  if(c.reply&&(typeof c.reply!=='object'||typeof c.reply.name!=='string'||c.reply.name.length>80||typeof c.reply.text!=='string'||c.reply.text.length>320))throw Error('Invalid reply.');
  let media=null;
  if(c.media){
    if(c.media.kind==='sealed'){
      const fields=['key','iv','macKey','mac','mime'];
      if(!fields.every(k=>typeof c.media[k]==='string'&&c.media[k].length>0&&c.media[k].length<200))throw Error('Invalid attachment.');
      if(!['image/jpeg','image/png','video/mp4','audio/wav','audio/x-wav','audio/wave','audio/mpeg','audio/mp3','text/plain','text/markdown','text/x-markdown'].includes(c.media.mime))throw Error('Invalid attachment.');
      media={kind:'sealed',mime:c.media.mime,key:c.media.key,iv:c.media.iv,macKey:c.media.macKey,mac:c.media.mac};
    }else{
      if(!['photo','video'].includes(c.media.kind)||typeof c.media.data!=='string'||c.media.data.length>5592408||!['image/jpeg','image/png','video/mp4'].includes(c.media.mime))throw Error('Invalid attachment.');
      media={kind:c.media.kind,mime:c.media.mime,data:c.media.data};
    }
  }
  return {format:c.format,text:c.text,reply:c.reply?{id:Number.isSafeInteger(c.reply.id)?c.reply.id:0,name:c.reply.name,text:c.reply.text}:null,media};
}
