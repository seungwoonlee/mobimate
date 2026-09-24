using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MobiMate;

public class GoogleSheetPayload
{
    [JsonPropertyName("backup")]
    public bool Backup { get; set; } = true;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("job")]
    public string Job { get; set; } = "";

    [JsonPropertyName("combatScore")]
    public long CombatScore { get; set; }

    [JsonPropertyName("livingScore")]
    public long LivingScore { get; set; }

    [JsonPropertyName("attractiveness")]
    public long Attractiveness { get; set; }

    [JsonPropertyName("arcaneResist")]
    public long ArcaneResist { get; set; }

    [JsonPropertyName("raidCavrak")]
    public bool RaidCavrak { get; set; }

    [JsonPropertyName("raidEirel")]
    public bool RaidEirel { get; set; }

    [JsonPropertyName("raidWhiteSuccubus")]
    public bool RaidWhiteSuccubus { get; set; }

    [JsonPropertyName("fieldBoss")]
    public bool FieldBoss { get; set; }

    [JsonPropertyName("vanguard")]
    public bool Vanguard { get; set; }
}

public class GoogleSheetResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("backupCreated")]
    public bool BackupCreated { get; set; }

    [JsonPropertyName("updatedRow")]
    public int UpdatedRow { get; set; }
}

public class GoogleSheetSyncService
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public GoogleSheetSyncService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
        }
    }

    public static GoogleSheetPayload BuildPayload(
        string characterName,
        CharacterInfo charInfo,
        HomeworkRepository? homeworkRepo,
        bool requestBackup = true)
    {
        var payload = new GoogleSheetPayload
        {
            Backup = requestBackup,
            Name = characterName.Trim(),
            Job = charInfo.JobName ?? "",
            CombatScore = charInfo.CombatScore?.Value ?? 0,
            LivingScore = charInfo.LivingScore?.Value ?? 0,
            Attractiveness = charInfo.AttractivenessScore?.Value ?? 0,
            ArcaneResist = charInfo.ArcaneResistance?.Value ?? 0
        };

        if (homeworkRepo != null)
        {
            var realm = string.IsNullOrEmpty(charInfo.RealmName) ? "에린" : charInfo.RealmName;
            var job = string.IsNullOrEmpty(charInfo.JobName) ? "밀레시안" : charInfo.JobName;
            var charKey = $"{realm}_{job}";
            var now = DateTime.Now;
            var record = homeworkRepo.GetOrCreateRecord(charKey, now);

            // 1. 카브락 레이드 (raid_cavrak)
            if (record.Items.TryGetValue("raid_cavrak", out var sCavrak))
            {
                payload.RaidCavrak = sCavrak.IsCompleted;
            }

            // 2. 에이렐 레이드 (raid_airel)
            if (record.Items.TryGetValue("raid_airel", out var sEirel))
            {
                payload.RaidEirel = sEirel.IsCompleted;
            }

            // 3. 화이트 서큐버스 레이드 (raid_white_succubus)
            if (record.Items.TryGetValue("raid_white_succubus", out var sSuccubus))
            {
                payload.RaidWhiteSuccubus = sSuccubus.IsCompleted;
            }

            // 4. 필드보스 주간 택1 공유풀 (field_boss_weekly 또는 fieldboss_로 시작하는 항목 완료 여부)
            payload.FieldBoss = record.Items.Values.Any(s =>
                s.Id.StartsWith("fieldboss_") && s.IsCompleted);

            // 5. 뱅가드 브리치 (weekly_vanguard_breach)
            if (record.Items.TryGetValue("weekly_vanguard_breach", out var sVanguard))
            {
                payload.Vanguard = sVanguard.IsCompleted;
            }
        }

        return payload;
    }

    public async Task<(bool success, string message, GoogleSheetResponse? responseData)> SyncAsync(
        string webhookUrl,
        GoogleSheetPayload payload,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return (false, "Google Apps Script 웹 앱 Webhook URL이 설정되지 않았습니다.", null);
        }

        if (string.IsNullOrWhiteSpace(payload.Name))
        {
            return (false, "시트에서 탐색할 캐릭터 이름이 비어 있습니다.", null);
        }

        try
        {
            var json = JsonSerializer.Serialize(payload, JsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(webhookUrl, content, ct);
            var responseString = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"HTTP 오류 ({response.StatusCode}): {responseString}", null);
            }

            GoogleSheetResponse? resData = null;
            try
            {
                resData = JsonSerializer.Deserialize<GoogleSheetResponse>(responseString, JsonOpts);
            }
            catch
            {
                // HTML 응답 등 예외적인 경우
            }

            if (resData != null)
            {
                if (resData.Status == "success")
                {
                    return (true, resData.Message, resData);
                }
                else
                {
                    return (false, resData.Message ?? "알 수 없는 오류가 발생했습니다.", resData);
                }
            }

            return (true, "동기화 요청이 완료되었습니다.", null);
        }
        catch (OperationCanceledException)
        {
            return (false, "요청 시간이 초과되었거나 취소되었습니다.", null);
        }
        catch (Exception ex)
        {
            return (false, $"동기화 통신 오류: {ex.Message}", null);
        }
    }
}
