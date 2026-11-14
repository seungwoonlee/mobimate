using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

/// <summary>제목줄 게임 연결 점의 설명 (연결 초록 / 미연결 주황 / CLI 없음 빨강 / 모름 회색).</summary>
public static class ConnectionInfo
{
    public static string Label(GameConnection state) => state switch
    {
        GameConnection.Connected => "연결됨",
        GameConnection.Disconnected => "연결 안 됨",
        GameConnection.CliMissing => "CLI 없음",
        _ => "확인 중",
    };

    public static string Tooltip(GameConnection state, string? cliPath) => state switch
    {
        GameConnection.Connected => "게임에 연결됨",
        GameConnection.Disconnected => "게임에 연결되지 않음 — 게임이 켜져 있는지 확인하세요. 기록된 값은 계속 보입니다.",
        GameConnection.CliMissing => $"게임 CLI를 찾을 수 없음 — 설정(⚙)에서 경로를 지정하세요.{(string.IsNullOrWhiteSpace(cliPath) ? "" : "\n" + cliPath)}",
        _ => "게임 연결 확인 중",
    };
}
