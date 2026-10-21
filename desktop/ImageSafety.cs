using System;

namespace Illusion {
  public static class ImageSafety {
    static int U16(byte[] b,int p){return (b[p]<<8)|b[p+1];}
    static long U32(byte[] b,int p){return ((long)b[p]<<24)|((long)b[p+1]<<16)|((long)b[p+2]<<8)|b[p+3];}
    public static void Dimensions(byte[] b,out int width,out int height){
      long w=0,h=0;
      if(b.Length>=33 && b[0]==137 && b[1]==80 && b[2]==78 && b[3]==71 && b[4]==13 && b[5]==10 && b[6]==26 && b[7]==10 && U32(b,8)==13 && b[12]==73 && b[13]==72 && b[14]==68 && b[15]==82){w=U32(b,16);h=U32(b,20);}
      else if(b.Length>=4 && b[0]==255 && b[1]==216){
        int p=2;while(p+4<=b.Length){if(b[p++]!=255)throw new Exception("Invalid image header.");while(p<b.Length && b[p]==255)p++;if(p>=b.Length)break;int marker=b[p++];if(marker==217 || marker==218)break;if(marker==1 || (marker>=208 && marker<=215))continue;if(p+2>b.Length)break;int length=U16(b,p);if(length<2 || p+length>b.Length)break;
          if((marker>=192 && marker<=195)||(marker>=197 && marker<=199)||(marker>=201 && marker<=203)||(marker>=205 && marker<=207)){if(length<8)break;h=U16(b,p+3);w=U16(b,p+5);break;}p+=length;
        }
      }
      if(w<=0 || h<=0 || w>8192 || h>8192 || w*h>16777216)throw new Exception("Use a PNG/JPEG with sides up to 8192 pixels and at most 16 megapixels.");width=(int)w;height=(int)h;
    }
  }
}
