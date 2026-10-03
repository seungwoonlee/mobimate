@echo off
chcp 65001 >nul
setlocal
rem MobiMate Web: 폰·태블릿 접속이 윈도우 방화벽에 막힐 때 포트 허용 규칙을 넣는다.
rem
rem 증상: 폰에서 QR로 열면 "응답하는 데 시간이 너무 오래 걸립니다"가 뜬다.
rem 원인: 윈도우 방화벽이 TCP 17800(웹)과 UDP 5353(이름 주소)을 조용히 차단한다.
rem       앱 경로 기준 허용 규칙이 적용되지 않는 PC가 있어서 포트 기준으로 허용한다. 개인(Private) 네트워크에서만 열린다.
rem
rem 사용법: 더블클릭하면 관리자 승인 창이 뜬다. "예"를 누르면 끝.
rem   allow-lan-firewall.bat            규칙 추가 (이미 있으면 다시 만든다)
rem   allow-lan-firewall.bat 17801      앱 포트를 바꿨을 때
rem   allow-lan-firewall.bat remove     규칙 삭제

set "PORT=17800"
set "MODE=add"
if /i "%~1"=="remove" (set "MODE=remove") else if not "%~1"=="" set "PORT=%~1"
set "ARG=%PORT%"
if "%MODE%"=="remove" set "ARG=remove"

net session >nul 2>&1
if errorlevel 1 (
  echo 관리자 권한이 필요합니다. 승인 창이 뜨면 "예"를 눌러 주세요...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -ArgumentList '%ARG%' -Verb RunAs"
  exit /b
)

netsh advfirewall firewall delete rule name="MobiMateWeb LAN %PORT%" >nul 2>&1
netsh advfirewall firewall delete rule name="MobiMateWeb mDNS 5353" >nul 2>&1

if "%MODE%"=="remove" (
  echo 삭제했습니다: MobiMateWeb LAN %PORT% / MobiMateWeb mDNS 5353
  goto :done
)

netsh advfirewall firewall add rule name="MobiMateWeb LAN %PORT%" dir=in action=allow protocol=TCP localport=%PORT% profile=private >nul
if errorlevel 1 goto :fail
netsh advfirewall firewall add rule name="MobiMateWeb mDNS 5353" dir=in action=allow protocol=UDP localport=5353 profile=private >nul
if errorlevel 1 goto :fail
echo 추가했습니다 (개인 네트워크 전용): TCP %PORT%, UDP 5353
echo 폰에서 QR을 다시 찍어 보세요.
goto :done

:fail
echo 규칙을 추가하지 못했습니다. 관리자 권한으로 실행했는지 확인해 주세요.

:done
echo.
pause
