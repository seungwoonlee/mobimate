using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MobiMate.Web.Services;

/// <summary>모비라이프(mabimobi.life) 랭킹 한 줄. 순위는 서버 안 순위다. 모르는 값은 null.</summary>
public sealed record MobiRankRow(string Name, string Server, long? Combat, long? Charm, long? Life, long? Total,
    int? ServerCombatRank, int? ServerCharmRank, int? ServerLifeRank, int? ServerTotalRank);

public enum MobiStatus { Ok, Blocked, Failed }
public sealed record MobiSearchResult(MobiStatus Status, IReadOnlyList<MobiRankRow> Rows, string? Detail = null);

/// <summary>
/// 모비라이프의 캐릭터 검색 한 번(2026-10-22, 승운 결정: 버튼을 누를 때만, 자동 조회 없음). 그 사이트가 화면에서 쓰는 주소를 그대로 부른다:
/// GET /d/api/v1/search/rankings/v2?character_name=이름. 로그인·쿠키 없이 이름만 보낸다.
/// 사이트의 robots.txt는 일반 봇을 막아 두었으므로(검색엔진만 허용) 이 클래스는 반복 호출·재시도를 하지 않는다: 호출하는 쪽이
/// "사람이 누른 한 번"의 범위(캐릭터 수만큼, 간격을 두고)로만 쓴다. 403·429를 받으면 바로 멈춘다.
/// </summary>
public sealed class MobiRankClient
{
    public const string SiteUrl = "https://mabimobi.life/ranking";
    private const string ApiBase = "https://mabimobi.life/d/api/v1/search/rankings/v2";
    private readonly Func<HttpClient> _http;

    public MobiRankClient(Func<HttpClient>? http = null)
    {
        _http = http ?? (() => new HttpClient { Timeout = TimeSpan.FromSeconds(12) });
    }

    public async Task<MobiSearchResult> SearchAsync(string characterName, CancellationToken ct)
    {
        try
        {
            using var http = _http();
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}?character_name={Uri.EscapeDataString(characterName)}");
            // 누구인지 숨기지 않는다: 개인용 앱이 사용자가 누를 때만 보내는 조회임을 밝힌다
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("MobiMate", "1.0"));
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("(personal-use app; one lookup per character, only when the user presses a button)"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var res = await http.SendAsync(req, ct);
            if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return new MobiSearchResult(MobiStatus.Blocked, Array.Empty<MobiRankRow>(), $"HTTP {(int)res.StatusCode}");
            if (!res.IsSuccessStatusCode)
                return new MobiSearchResult(MobiStatus.Failed, Array.Empty<MobiRankRow>(), $"HTTP {(int)res.StatusCode}");
            var text = await res.Content.ReadAsStringAsync(ct);
            var rows = Parse(text);
            return rows == null ? new MobiSearchResult(MobiStatus.Failed, Array.Empty<MobiRankRow>(), "응답 형식을 읽지 못함") : new MobiSearchResult(MobiStatus.Ok, rows);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new MobiSearchResult(MobiStatus.Failed, Array.Empty<MobiRankRow>(), e.GetType().Name);
        }
    }

    /// <summary>응답(배열)을 읽는다. 배열이 아니면 null. 이름·서버가 없는 줄은 건너뛴다.</summary>
    public static List<MobiRankRow>? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var rows = new List<MobiRankRow>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var name = Str(e, "character_name");
                var server = Str(e, "server");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(server)) continue;
                rows.Add(new MobiRankRow(name, server, Num(e, "combat"), Num(e, "charm"), Num(e, "life_skill"), Num(e, "total"),
                    (int?)Num(e, "server_combat_rank"), (int?)Num(e, "server_charm_rank"), (int?)Num(e, "server_life_skill_rank"), (int?)Num(e, "server_total_rank")));
            }
            return rows;
        }
        catch (JsonException) { return null; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ValueKind == JsonValueKind.Number ? v.GetRawText() : null : null;

    private static long? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
}
