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
    /// <summary>
    /// 응답이 파이프(게임과의 연결) 상태를 말하는가: `{"pipe":"connected"}` → true, `{"pipe":"disconnected",…}` → false, 그 밖(값·JSON 아님) → null.
    /// `status`는 연결이 끊겨도 프로세스는 정상 종료하므로 "응답이 왔다"만으로 연결됨으로 보면 안 된다.
    /// </summary>
    public static bool? PipeConnected(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout) || stdout.AsSpan().TrimStart() is not [{ } c, ..] || c != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("pipe", out var p) || p.ValueKind != JsonValueKind.String) return null;
            return string.Equals(p.GetString(), "connected", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// write_chat 응답이 거절이면 사용자에게 보일 문장을, 보내졌거나(accepted) 알 수 없으면 null.
    /// 명세: 실패는 `status`가 rejected·invalid_body이고 `body.error`가 user_declined | confirm_timeout | rate_limited(retryAfterSeconds) |
    /// inappropriate_text | blocked | not_enough_currency | unsupported_command 등. 오류 이름이 최상위에 오는 형식도 함께 읽는다.
    /// </summary>
    public static string? ChatRejection(string? stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout) || stdout.AsSpan().TrimStart() is not [{ } c, ..] || c != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var body = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.Object ? b : root;
            string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var status = Str(root, "status");
            var error = Str(body, "error") ?? Str(root, "error");
            if (error == null && status is not ("rejected" or "invalid_body")) return null;   // accepted이거나 알 수 없는 정상 응답
            error ??= status;
            var message = Str(body, "message") ?? Str(root, "message");
            var retry = body.TryGetProperty("retryAfterSeconds", out var r) && r.TryGetInt32(out var n) ? n : (int?)null;
            return error switch
            {
                "user_declined" => "게임에서 취소했습니다",
                "confirm_timeout" => "게임에서 확인하지 않아 취소되었습니다",
                "rate_limited" => retry is { } s ? $"너무 자주 보냈습니다. {s}초 뒤에 다시 보내 주세요" : "너무 자주 보냈습니다. 잠시 뒤에 다시 보내 주세요",
                "inappropriate_text" => "서버 문자 필터에 걸려 보내지 못했습니다. 말을 바꿔 다시 해 보세요",
                "blocked" => "게임에서 채팅이 막혀 있습니다",
                "not_enough_currency" => "채팅에 필요한 재화가 부족합니다",
                "message_too_long" => "메시지가 너무 깁니다",
                _ => string.IsNullOrWhiteSpace(message) ? $"게임 CLI 오류: {error}" : $"게임 CLI 오류({error}): {message}",
            };
        }
        catch (JsonException) { return null; }
    }

    /// <summary>
    /// 변경(실행) 명령의 응답이 실패·중단이면 사용자에게 보일 문장을, 성공이거나 알 수 없으면 null.
    /// 변경 명령은 프로세스가 정상 종료해도 게임이 거절·중단했을 수 있다. 응답 형식(명세): `{"status":"accepted|rejected|invalid_body","body":{…}}`,
    /// 실패는 `status`가 rejected·invalid_body이거나 `body.error`가 있다(accepted여도 blocked·timeout·canceled로 중단될 수 있다).
    /// 조회 응답처럼 최상위 `error`·연결 끊김(pipe)도 함께 읽는다. 이것을 보지 않으면 게임이 아무것도 안 했는데 앱은 "했습니다"라고 한다.
    /// </summary>
    public static string? ActionError(string? stdout)
    {
        if (TryReadError(stdout, out var top)) return top;
        if (string.IsNullOrWhiteSpace(stdout) || stdout.AsSpan().TrimStart() is not [{ } c, ..] || c != '{') return null;
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var body = root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.Object ? b : root;
            string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var status = Str(root, "status");
            var error = Str(body, "error");
            var message = Str(body, "message");
            if (error == null && Str(body, "result") == "stopped_by_user") return "게임에서 직접 정지해 중단되었습니다";
            if (error == null && status is not ("rejected" or "invalid_body")) return null;
            return DescribeAction(error ?? status!, message);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>변경 명령 오류 이름(명세)을 사용자 문장으로</summary>
    public static string DescribeAction(string error, string? message) => error switch
    {
        "blocked" => "게임 화면에 확인이 필요한 창이 떠 있습니다. 게임에서 해결한 뒤 다시 해 주세요",
        "overweight" => "가방이 가득 찼습니다",
        "not_in_field" => "필드에서만 할 수 있습니다",
        "not_completed_yet" => "아직 완료되지 않은 가공물입니다",
        "no_completed_work" or "no_completed_work_at_facility" or "no_altering" => "수거할 완료된 가공물이 없습니다",
        "not_found" => "게임에서 찾을 수 없는 항목입니다",
        "facility_not_found" => "가공 시설을 찾지 못했습니다",
        "no_route" => "그곳까지 갈 길이 없습니다",
        "timeout" => "게임에서 확인되지 않아 중단되었습니다. 게임에서 직접 확인해 주세요",
        "canceled" => "게임에서 취소되었습니다",
        "invalid_state" => "지금 멈출 수 있는 행동이 없습니다",
        "requires_user_interaction" => "이 작업은 게임에서 직접 해야 합니다",
        "invalid_body" => "요청 형식이 올바르지 않습니다",
        "rejected" => string.IsNullOrWhiteSpace(message) ? "게임이 요청을 거절했습니다" : $"게임이 요청을 거절했습니다: {message}",
        _ => string.IsNullOrWhiteSpace(message) ? $"게임 CLI 오류: {error}" : $"게임 CLI 오류({error}): {message}",
    };

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
