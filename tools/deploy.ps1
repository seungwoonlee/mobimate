# 최종 빌드본을 V:\workspace\MobiMate 바로 아래에 둔다 (항상 덮어쓰기). 실행 중이면 종료하고, 배포 뒤 다시 실행한다.
#   사용: powershell -File tools\deploy.ps1 [-Only web|hw] [-NoLaunch]
param([ValidateSet('all','web','hw')][string]$Only = 'all', [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$out  = 'V:\workspace\MobiMate'
$tmp  = Join-Path $env:TEMP ('mm-publish-' + [guid]::NewGuid().ToString('N'))

function Publish($name, $proj, $files) {
    Get-Process $name -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $dir = Join-Path $tmp $name
    dotnet publish (Join-Path $repo $proj) -c Release -o $dir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "$name 게시 실패" }
    foreach ($f in $files) { Copy-Item (Join-Path $dir $f) (Join-Path $out $f) -Force }
    Write-Host "$name -> $out\$($files[0])"
}

if ($Only -in 'all','web') { Publish 'MobiMateWeb' 'web\src\MobiMate.Web' @('MobiMateWeb.exe','allow-lan-firewall.bat') }
if ($Only -in 'all','hw')  { Publish 'MobiMateHomeWork' 'hw\src\MobiMateHomeWork' @('MobiMateHomeWork.exe') }
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

if (-not $NoLaunch) {
    if ($Only -in 'all','web') { Start-Process (Join-Path $out 'MobiMateWeb.exe') -WorkingDirectory $out }
    if ($Only -in 'all','hw')  { Start-Process (Join-Path $out 'MobiMateHomeWork.exe') -WorkingDirectory $out }
}
