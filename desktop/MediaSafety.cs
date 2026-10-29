using System;
using System.Text;

namespace Illusion {
  public static class MediaSafety {
    public static void ValidateViewerMedia(string kind,byte[] bytes) {
      if(bytes==null||bytes.Length==0||bytes.Length>4*1024*1024)throw new Exception("Attachment is too large.");
      if(kind=="photo"){
        bool png=bytes.Length>=8&&bytes[0]==137&&bytes[1]==80&&bytes[2]==78&&bytes[3]==71&&bytes[4]==13&&bytes[5]==10&&bytes[6]==26&&bytes[7]==10;
        bool jpeg=bytes.Length>=3&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255;
        if(!png&&!jpeg)throw new Exception("Invalid photo attachment.");
        int width,height;ImageSafety.Dimensions(bytes,out width,out height);
      }else if(kind=="video"){
        if(bytes.Length<12||Encoding.ASCII.GetString(bytes,4,4)!="ftyp")throw new Exception("Invalid MP4 attachment.");
      }else if(kind=="audio"){
        bool wav=bytes.Length>=12&&Encoding.ASCII.GetString(bytes,0,4)=="RIFF"&&Encoding.ASCII.GetString(bytes,8,4)=="WAVE";
        bool mp3=(bytes.Length>=3&&Encoding.ASCII.GetString(bytes,0,3)=="ID3")||(bytes.Length>=2&&bytes[0]==0xff&&(bytes[1]&0xe0)==0xe0);
        if(!wav&&!mp3)throw new Exception("Invalid audio attachment.");
      }
    }
  }
}
