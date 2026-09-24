using System.Text.Json;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

public sealed record GatherJob(string JobId, string Item, int? Count, string DeviceId, DateTimeOffset StartedAt)
{
    public string State { get; set; } = "running";
    public int Gained { get; set; }
    public string? Message { get; set; }
}

public enum StartGatherResult { Started, Conflict, UnknownItem, CliMissing }

/// <summary>
/// 조작 API (§7): 채집 작업(FR-DT-08, 장시간 레인), 긴급 정지(우선 경로), 가공물 수거.
/// 조작 뒤에는 관련 조회 캐시를 비운다 (상세설계 §3.4 무효화 표).
/// </summary>
public sealed class GameActions(GameQueries q, SseHub hub, HomeworkWatcher homework, ILogger<GameActions> log)
{
    private readonly object _lock = new();
    private GatherJob? _current;

    public GatherJob? Current { get { lock (_lock) return _current is { State: "running" } ? _current : null; } }

    public async Task<(StartGatherResult Result, GatherJob? Job)> StartGatherAsync(string item, int? count, string deviceId, CancellationToken ct)
    {
        if (!q.Cli.IsAvailable) return (StartGatherResult.CliMissing, null);
        var list = await q.Get<GatherableResponse>("get_gatherable_items", ct);
        if (list.Value?.Items?.Any(g => g.DisplayName == item) != true) return (StartGatherResult.UnknownItem, null);

        GatherJob job;
        lock (_lock)
        {
            if (_current is { State: "running" }) return (StartGatherResult.Conflict, _current);
            job = _current = new GatherJob(Guid.NewGuid().ToString("N")[..10], item, count is > 0 ? count : null, deviceId, DateTimeOffset.UtcNow);
        }
        _ = Task.Run(() => RunGatherAsync(job));
        return (StartGatherResult.Started, job);
    }

    private async Task RunGatherAsync(GatherJob job)
    {
        object body = job.Count is { } c ? new { displayName = job.Item, count = c } : new { displayName = job.Item };
        hub.Broadcast("gather", new { jobId = job.JobId, state = "running", item = job.Item, gained = 0, message = (string?)null });
        // 어떤 예외가 나도 작업은 반드시 종료 상태가 된다. "running"에 묶이면 재시작 전까지 모든 채집이 409가 된다.
        try
        {
            var r = await q.Cli.RunAsync(new CliCommand("execute_gathering", Body: body));
            lock (_lock)
            {
                if (r.Ok)
                {
                    var (result, gained) = ParseGather(r.Stdout);
                    job.State = result;
                    job.Gained = gained;
                }
                else
                {
                    job.State = "failed";
                    job.Message = r.Error;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                job.State = "failed";
                job.Message = ex.Message;
            }
            log.LogWarning(ex, "채집 작업 {JobId} 예외", job.JobId);
        }
        q.Invalidate("get_items", "get_my_info", "get_currencies", "get_activity");
        hub.Broadcast("gather", new { jobId = job.JobId, state = job.State, item = job.Item, gained = job.Gained, message = job.Message });
        hub.Broadcast("toast", job.State switch
        {
            "completed" => new { level = "ok", message = $"{job.Item} 채집 완료 · +{job.Gained}개" },
            "stopped" => new { level = "warn", message = $"{job.Item} 채집을 멈췄습니다 · +{job.Gained}개" },
            "started" => new { level = "ok", message = $"{job.Item} 자동 채집이 시작되었습니다" },
            _ => new { level = "danger", message = $"{job.Item} 채집 실패: {job.Message}" },
        });
        log.LogInformation("채집 작업 {JobId} 종료: {State}", job.JobId, job.State);
    }

    internal static (string Result, int Gained) ParseGather(string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ("completed", 0);
            var result = root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.String ? res.GetString() ?? "completed" : "completed";
            var gained = root.TryGetProperty("gained", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetInt32(out var n) ? n : 0;
            return (result, gained);
        }
        catch (JsonException)
        {
            return ("completed", 0);
        }
    }

    /// <summary>긴급 정지 (FR-AC-01). stop_action은 CLI 우선 경로로 대기 없이 실행된다.</summary>
    public async Task<CliResult> StopAsync(CancellationToken ct)
    {
        var r = await q.Cli.RunAsync(new CliCommand("stop_action"), ct);
        q.Invalidate("get_activity");
        hub.Broadcast("toast", r.Ok
            ? new { level = "danger", message = "행동 정지를 요청했습니다" }
            : new { level = "warn", message = $"행동 정지 실패: {r.Error}" });
        return r;
    }

    /// <summary>가공물 수거. 이름이 없으면 완료된 첫 작업. 성공하면 숙제 "가공시설 수거"를 판정한다 (FR-HW-05).</summary>
    public async Task<(bool Ok, string? Name, string? Error)> CollectAsync(string? name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            var works = await q.Get<AlteringWorksResponse>("get_altering_works", ct);
            name = works.Value?.Works?.FirstOrDefault(w => w.IsCompleted || w.RemainingSeconds <= 0)?.DisplayName;
            if (name == null) return (false, null, "완료된 가공물이 없습니다.");
        }
        var r = await q.Cli.RunAsync(new CliCommand("complete_altering_work", Body: new { displayName = name }), ct);
        q.Invalidate("get_altering_works", "get_items", "get_my_info");
        if (!r.Ok) return (false, name, r.Error);
        // 캐릭터를 모르면 판정하지 않는다. 기본 키("에린_밀레시안")에 잘못 기록하지 않기 위해서다.
        var me = await q.Get<CharacterInfo>("get_my_info", ct);
        if (me.Value is { } ch && !string.IsNullOrWhiteSpace(ch.RealmName) && !string.IsNullOrWhiteSpace(ch.JobName))
            homework.Evaluate(GameViews.CharacterKey(ch), new HomeworkObservation(CollectedByApp: true));
        return (true, name, null);
    }
}

/// <summary>
/// 숙제 판정 연결부: 조회 결과를 모아 HomeworkService에 넘기고, 바뀌면 SSE "homework.changed"로 알린다.
/// 조회에 실패한 항목은 null로 넘겨 판정 근거에서 뺀다.
/// </summary>
public sealed class HomeworkWatcher(HomeworkService homework, GameQueries q, SseHub hub)
{
    public HomeworkService Service => homework;

    /// <summary>
    /// 미션·퀘스트와 같은 묶음에서 get_my_info를 읽어 그 결과로 캐릭터 키를 정한다.
    /// 캐시된 헤더의 캐릭터를 쓰면 캐릭터를 바꾼 직후 새 캐릭터의 미션이 이전 캐릭터에 기록될 수 있다.
    /// 캐릭터를 확인하지 못하면 판정하지 않고 null을 돌려준다.
    /// </summary>
    public async Task<string?> ObserveAsync(CancellationToken ct)
    {
        var tMe = q.Get<CharacterInfo>("get_my_info", ct);
        var tD = q.Get<List<MissionItem>>("get_daily_missions", ct);
        var tW = q.Get<List<MissionItem>>("get_weekly_missions", ct);
        var tQ = q.Get<List<QuestItem>>("get_quests", ct);
        var tA = q.Get<ActivityInfo>("get_activity", ct);
        var tE = q.Get<EnvironmentInfo>("get_current_environment", ct);
        var tW2 = q.Get<AlteringWorksResponse>("get_altering_works", ct);
        await Task.WhenAll(tMe, tD, tW, tQ, tA, tE, tW2);

        if (tMe.Result.Value is not { } ch || string.IsNullOrWhiteSpace(ch.RealmName) || string.IsNullOrWhiteSpace(ch.JobName)) return null;
        var key = GameViews.CharacterKey(ch);
        Evaluate(key, new HomeworkObservation(
            tD.Result.Value, tW.Result.Value, tQ.Result.Value, tA.Result.Value, tE.Result.Value, tW2.Result.Value));
        return key;
    }

    public void Evaluate(string characterKey, HomeworkObservation obs)
    {
        var change = homework.Evaluate(characterKey, obs);
        if (change != null) Notify(change);
    }

    public void Notify(HomeworkChange change) =>
        hub.Broadcast("homework.changed", new { characterKey = change.CharacterKey, ids = change.Ids, reason = change.Reason });
}
