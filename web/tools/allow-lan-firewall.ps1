<#
  MobiMate Web: 폰·태블릿 접속이 막힐 때 윈도우 방화벽에 포트 허용 규칙을 넣는다.

  증상: 폰에서 QR로 열면 "응답하는 데 시간이 너무 오래 걸립니다"가 뜬다.
  원인: 윈도우 방화벽이 앱 경로 기준 허용 규칙을 이 실행에 적용하지 않고 TCP 17800(웹)과 UDP 5353(mDNS 이름 주소)을 조용히 차단한다
        (2026-10-03 pfirewall.log로 확인: DROP TCP <폰> 192.168.0.4 17800).
  해결: 앱 경로가 아니라 포트 기준으로 허용한다. 개인(Private) 네트워크에서만 열리므로 공용 네트워크에서는 열리지 않는다.

  사용법 (관리자 권한이 필요하다. 아니면 승격 창이 뜬다):
    .\allow-lan-firewall.ps1            # 규칙 추가(이미 있으면 다시 만든다)
    .\allow-lan-firewall.ps1 -Remove    # 규칙 삭제
    .\allow-lan-firewall.ps1 -Port 17801  # 앱 포트를 바꿨을 때

  막히는지 확인하려면 관리자 PowerShell에서:
    Get-Content C:\Windows\System32\LogFiles\Firewall\pfirewall.log -Tail 2000 | Select-String ' DROP ' | Select-String '17800|5353'
  (개인 프로필의 "차단된 연결 로그"가 켜져 있어야 한다: netsh advfirewall set privateprofile logging droppedconnections enable)
#>
param(
    [int]$Port = 17800,
    [switch]$Remove
)

$tcpName = "MobiMateWeb LAN $Port"
$udpName = 'MobiMateWeb mDNS 5353'

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) {
    Write-Host '관리자 권한이 필요합니다. 승격 창을 띄웁니다...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Port', $Port)
    if ($Remove) { $argList += '-Remove' }
    Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $argList -Verb RunAs -Wait
    exit
}

# 같은 이름의 규칙이 있으면 지우고 다시 만든다 (몇 번 실행해도 규칙이 겹쳐 쌓이지 않는다)
Get-NetFirewallRule -DisplayName $tcpName, $udpName -ErrorAction SilentlyContinue | Remove-NetFirewallRule

if ($Remove) {
    Write-Host "삭제했습니다: $tcpName / $udpName"
} else {
    New-NetFirewallRule -DisplayName $tcpName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Private | Out-Null
    New-NetFirewallRule -DisplayName $udpName -Direction Inbound -Action Allow -Protocol UDP -LocalPort 5353 -Profile Private | Out-Null
    Write-Host "추가했습니다 (개인 네트워크 전용): TCP $Port, UDP 5353"
}
Get-NetFirewallRule -DisplayName $tcpName, $udpName -ErrorAction SilentlyContinue | Select-Object DisplayName, Enabled, Direction, Action, Profile | Format-Table -AutoSize
