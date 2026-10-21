using System.Text.Json;

namespace MobiMate;

/// <summary>
/// 게임 CLI의 오류 응답 알아보기(2026-10-22 CLI 점검). 조회 명령은 성공하면 값(배열·객체)을 주지만, 실패하면 같은 자리에 오류 객체를 준다:
/// `{"error":"unknown_command","message":"…"}`(종료 코드 4), `{"pipe":"disconnected","reason":"game_off","message":"…"}`(게임이 꺼짐),
/// 캐릭터 선택 중에는 배열 명령이 객체를 준다. 이를 값으로 읽으면 배열 명령은 PARSE_ERROR가 되고 객체 명령은 "빈 값의 성공"이 되어 조용히 틀린다.
/// 조회 응답의 맨 위에 `error` 문자열이 있거나 `pipe`가 연결 끊김이면 오류로 본다(정상 조회 응답에는 이런 키가 없다).
/// </summary>
public static class CliEnvelope
{
    /// <summary>오류 응답이면 사람이 읽을 메시지를 돌려준다. 정상 값이거나 JSON이 아니면 false.</summary>
    public static bool TryReadError(string? stdout, out string message)
    {
        message = "";
        if (string.IsNullOrWhiteSpace(stdout)) return false;
        var s = stdout.AsSpan().TrimStart();
        if (s.Length == 0 || s[0] != '{') return false;   // 배열이거나 JSON이 아니다
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var error = Str("error");
            var msg = Str("message");
            if (error != null)
            {
                message = string.IsNullOrWhiteSpace(msg) ? $"게임 CLI 오류: {error}" : $"게임 CLI 오류({error}): {msg}";
                return true;
            }
            if (string.Equals(Str("pipe"), "disconnected", StringComparison.OrdinalIgnoreCase))
            {
                var reason = Str("reason");
                message = reason switch
                {
                    "game_off" => "게임이 꺼져 있거나 AI 커넥터가 꺼져 있습니다. 게임을 켜고 로그인한 뒤 [설정 > 게임]의 'Mabinogi Mobile AI Connector'를 확인하세요.",
                    _ => string.IsNullOrWhiteSpace(msg) ? $"게임과 연결되어 있지 않습니다({reason})." : msg!,
                };
                return true;
            }
            return false;
        }
        catch (JsonException) { return false; }
    }
}
