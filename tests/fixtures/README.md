`video.mp4` is an original one-second blue rectangle with silent audio, generated locally for native encrypted video playback verification. No downloaded or copyrighted footage is used.

Generation command (FFmpeg):

```
ffmpeg -f lavfi -i color=c=0x467aff:s=160x90:d=1:r=24 -f lavfi -i anullsrc=r=44100:cl=stereo -t 1 -c:v libx264 -pix_fmt yuv420p -c:a aac -movflags +faststart video.mp4
```
