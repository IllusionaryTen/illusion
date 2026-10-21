@echo off
setlocal
cd /d "%~dp0"
set "PUBLIC_TUNNEL_PROTOCOL=http2"
echo Starting Illusion through Cloudflare using TCP / HTTP2.
echo This avoids the QUIC connection that disconnected in the last test.
echo Happ can stay connected if Cloudflare tunnel endpoints use Direct routing.
echo See HAPP-CLOUDFLARE.md for the proposed routing test. Keep this window open.
echo.
call "%~dp0Start-Public.cmd"
endlocal
