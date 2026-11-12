using System.Text.Json;
using MobiMate.Web.Infrastructure;

namespace MobiMate.Web.Services;

/// <summary>
/// 채집 작업. 게임의 execute_gathering은 한 번에 최대 100개(<see cref="GameActions.BatchSize"/>)만 채집하고 멈추므로,
/// 목표가 100을 넘으면 서버가 사람 개입 없이 100개씩 이어서 호출한다. Count는 이번에 채집할 개수(null이면 100).
/// </summary>
public sealed record GatherJob(string JobId, string Item, int? Count, string DeviceId, DateTimeOffset StartedAt)
{
    public string State { get; set; } = "running";
    public int Gained { get; set; }
    public int Batch { get; set; }
    public string? Message { get; set; }
    public volatile bool StopRequested;

    public int Target => Count ?? GameActions.BatchSize;
    public int Batches => (Target + GameActions.BatchSize - 1) / GameActions.BatchSize;

    /// <summary>이 작업 뒤에 이어서 채집할 항목(순서대로, 항목마다 목표 개수). 큐가 아닌 단일 채집이면 비어 있다.</summary>
    public (string Item, int Count)[] Queued { get; set; } = [];

    public object Snapshot() => new { jobId = JobId, state = State, item = Item, gained = Gained, target = Target, batch = Batch, batches = Batches, message = Message, queued = Queued.Select(x => new { item = x.Item, count = x.Count }).ToArray() };
}

public enum StartGatherResult { Started, Conflict, UnknownItem, CliMissing }

/// <summary>한 번의 execute_gathering 응답. Error가 있으면 게임이 시작 전 거절했거나 도중에 끊긴 것이다.</summary>
public sealed record GatherReply(string Result, int Gained, int Target, string? Error, string? Message);

/// <summary>
/// 조작 API (§7): 채집 작업(FR-DT-08, 장시간 레인), 긴급 정지(우선 경로), 가공물 수거.
/// 조작 뒤에는 관련 조회 캐시를 비운다 (상세설계 §3.4 무효화 표).
/// </summary>
public sealed class GameActions(GameQueries q, SseHub hub, HomeworkWatcher homework, ILogger<GameActions> log)
{
    /// <summary>게임이 execute_gathering 한 번에 채집하는 최대 개수 (CLI 스키마: "One call gathers up to 100").</summary>
    public const int BatchSize = 100;
    /// <summary>한 작업의 최대 목표. 호출마다 날개 5개가 들기 때문에 실수로 너무 크게 잡지 않게 막는다.</summary>
    public const int MaxTarget = 5000;
    /// <summary>게임은 9분에 스스로 멈춘다. 그 응답을 받을 수 있게 조금 더 기다린다.</summary>
    internal static readonly TimeSpan BatchTimeout = TimeSpan.FromMinutes(10);

    private readonly object _lock = new();
    private GatherJob? _current;
    private GatherJob? _last;
    /// <summary>현재 작업이 정상 완료되면 이어서 채집할 항목. 실패·정지하면 비운다.</summary>
    private readonly Queue<(string Item, int Count)> _pending = new();

    public GatherJob? Current { get { lock (_lock) return _current is { State: "running" } ? _current : null; } }

    /// <summary>진행 중인 작업, 없으면 가장 최근에 끝난 작업. 새로 연결한 화면이 상태를 그리는 데 쓴다.</summary>
    public object? Snapshot() { lock (_lock) return (_current ?? _last)?.Snapshot(); }

    public async Task<(StartGatherResult Result, GatherJob? Job)> StartGatherAsync(string item, int? count, string deviceId, CancellationToken ct)
        => await StartQueueAsync([(item, count ?? 0)], deviceId, ct);

    /// <summary>
    /// 채집 큐: 첫 항목부터 차례로 목표 수량만큼 채집하고, 하나가 정상 완료되면 다음 항목으로 넘어간다.
    /// 중간에 실패·정지하면 남은 항목은 취소한다(사람이 풀어야 하는 문제일 가능성이 높다).
    /// </summary>
    public async Task<(StartGatherResult Result, GatherJob? Job)> StartQueueAsync(IReadOnlyList<(string Item, int Count)> items, string deviceId, CancellationToken ct)
    {
        if (!q.Cli.IsAvailable) return (StartGatherResult.CliMissing, null);
        var list = await q.Get<GatherableResponse>("get_gatherable_items", ct);
        foreach (var (item, _) in items)
            if (list.Value?.Items?.Any(g => g.DisplayName == item) != true) return (StartGatherResult.UnknownItem, null);

        GatherJob job;
        lock (_lock)
        {
            if (_current is { State: "running" } || (_pending.Count > 0 && _current != null)) return (StartGatherResult.Conflict, _current);
            _pending.Clear();
            foreach (var x in items.Skip(1)) _pending.Enqueue(x);
            job = _current = NewJob(items[0].Item, items[0].Count, deviceId);
        }
        _ = Task.Run(() => RunQueueAsync(job));
        return (StartGatherResult.Started, job);
    }

    private GatherJob NewJob(string item, int count, string deviceId) =>
        new(Guid.NewGuid().ToString("N")[..10], item, count > 0 ? count : null, deviceId, DateTimeOffset.UtcNow) { Queued = _pending.ToArray() };

    private async Task RunQueueAsync(GatherJob job)
    {
        while (true)
        {
            await RunGatherAsync(job);
            GatherJob? next = null;
            lock (_lock)
            {
                if (job.State == "completed" && _pending.Count > 0)
                {
                    var (item, count) = _pending.Dequeue();
                    next = _current = NewJob(item, count, job.DeviceId);
                }
                else _pending.Clear();
            }
            if (next == null) return;
            job = next;
        }
    }

    private async Task RunGatherAsync(GatherJob job)
    {
        hub.Broadcast("gather", job.Snapshot());
        // 어떤 예외가 나도 작업은 반드시 종료 상태가 된다. "running"에 묶이면 재시작 전까지 모든 채집이 409가 된다.
        try
        {
            // 목표를 채울 때까지 100개씩 이어서 호출한다. 한 개도 못 캔 호출이 나오면 멈춘다(날개만 소모하는 헛바퀴 방지).
            var maxCalls = job.Batches + 3;   // 게임이 100보다 적게 돌려주는 경우를 위한 여유
            for (var call = 0; call < maxCalls && job.Gained < job.Target; call++)
            {
                if (job.StopRequested) { Finish(job, "stopped", null); break; }
                lock (_lock) job.Batch = Math.Min(job.Batches, call + 1);
                hub.Broadcast("gather", job.Snapshot());

                var r = await q.Cli.RunAsync(new CliCommand("execute_gathering", Body: new { displayName = job.Item }, Timeout: BatchTimeout));
                q.Invalidate("get_items", "get_my_info", "get_currencies", "get_activity");
                if (!r.Ok) { Finish(job, job.StopRequested ? "stopped" : "failed", r.Error); break; }

                var reply = ParseGather(r.Stdout);
                lock (_lock) job.Gained += Math.Max(0, reply.Gained);
                log.LogInformation("채집 작업 {JobId} 배치 {Call}: {Result}{Error} +{Gained} (누적 {Total}/{Target})", job.JobId, call + 1, reply.Result, reply.Error, reply.Gained, job.Gained, job.Target);

                if (reply.Error is { } err)
                {
                    // 게임이 9분 제한으로 끊은 경우만 이어 간다. 그 외(막힘·무게·도구·날개·사용자 취소)는 사람이 풀어야 하므로 멈춘다.
                    if (err == "timeout" && reply.Gained > 0 && !job.StopRequested) { hub.Broadcast("gather", job.Snapshot()); continue; }
                    Finish(job, err == "canceled" || job.StopRequested ? "stopped" : "failed", Describe(err, reply.Message));
                    break;
                }
                if (reply.Result == "started") { Finish(job, "started", null); break; }           // 낚시: 목표가 없다
                if (reply.Result == "stopped") { Finish(job, "stopped", reply.Message); break; }  // 긴급 정지 등
                if (reply.Gained <= 0) { Finish(job, "failed", "한 개도 채집하지 못해 멈췄습니다."); break; }
                hub.Broadcast("gather", job.Snapshot());
            }
            if (job.State == "running")
            {
                var reached = job.Gained >= job.Target;
                Finish(job, reached ? "completed" : "stopped", reached ? null : "더 이상 채집이 진행되지 않아 멈췄습니다.");
            }
        }
        catch (Exception ex)
        {
            Finish(job, "failed", ex.Message);
            log.LogWarning(ex, "채집 작업 {JobId} 예외", job.JobId);
        }
        lock (_lock) _last = job;
        q.Invalidate("get_items", "get_my_info", "get_currencies", "get_activity");
        hub.Broadcast("gather", job.Snapshot());
        var got = job.Batches > 1 || job.Gained > 0 ? $" · +{job.Gained}개{(job.Batches > 1 ? $" / 목표 {job.Target}개" : "")}" : "";
        hub.Broadcast("toast", job.State switch
        {
            "completed" => new { level = "ok", message = $"{job.Item} 채집 완료{got}" },
            "stopped" => new { level = "warn", message = $"{job.Item} 채집을 멈췄습니다{got}" },
            "started" => new { level = "ok", message = $"{job.Item} 자동 채집이 시작되었습니다" },
            _ => new { level = "danger", message = $"{job.Item} 채집 실패: {job.Message}{got}" },
        });
        log.LogInformation("채집 작업 {JobId} 종료: {State}", job.JobId, job.State);
    }

    private void Finish(GatherJob job, string state, string? message)
    {
        lock (_lock) { job.State = state; job.Message = message; }
    }

    private static string Describe(string error, string? message) => error switch
    {
        "blocked" => "화면에 확인이 필요한 창이 떠 있습니다. 게임에서 해결해 주세요.",
        "overweight" => "가방이 가득 찼습니다.",
        "tool_broken" or "tool_missing" => "채집 도구가 없거나 망가졌습니다.",
        "not_enough_currency" => "정령의 날개가 부족합니다.",
        "insufficient_living_skill_level" => "생활 스킬 레벨이 부족합니다.",
        "no_route" => "채집 장소까지 갈 길이 없습니다.",
        "not_in_field" => "필드에서만 채집할 수 있습니다.",
        "required_consumable_missing" => "채집에 필요한 소모품이 없습니다.",
        "timeout" => "시간 제한(9분)으로 멈췄습니다.",
        "cli_error" => string.IsNullOrWhiteSpace(message) ? "게임 CLI 오류" : message!,
        "rejected" or "invalid_body" => CliEnvelope.DescribeAction(error, message),
        _ => string.IsNullOrWhiteSpace(message) ? error : $"{error}: {message}",
    };

    internal static GatherReply ParseGather(string stdout)
    {
        try
        {
            if (CliEnvelope.PipeConnected(stdout) == false && CliEnvelope.TryReadError(stdout, out var topMsg)) return new GatherReply("error", 0, 0, "cli_error", topMsg);   // 연결 끊김 (최상위 error는 아래에서 평평한 응답으로 읽는다)
            using var doc = JsonDocument.Parse(stdout);
            var envelope = doc.RootElement;
            if (envelope.ValueKind != JsonValueKind.Object) return new GatherReply("completed", 0, 0, null, null);
            // 명세 형식 {"status":…,"body":{…}}이면 body를, 평평한 응답이면 그대로 읽는다
            var root = envelope.TryGetProperty("body", out var bd) && bd.ValueKind == JsonValueKind.Object ? bd : envelope;
            if (envelope.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String && st.GetString() is "rejected" or "invalid_body" && !root.TryGetProperty("error", out _))
                return new GatherReply("error", 0, 0, st.GetString(), null);
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            int Int(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
            return new GatherReply(Str("result") ?? "completed", Int("gained"), Int("target"), Str("error"), Str("message"));
        }
        catch (JsonException)
        {
            return new GatherReply("completed", 0, 0, null, null);
        }
    }

    /// <summary>긴급 정지 (FR-AC-01). stop_action은 CLI 우선 경로로 대기 없이 실행된다.</summary>
    public async Task<CliResult> StopAsync(CancellationToken ct)
    {
        // 100개 단위 반복 중이면 지금 배치만 끊는 게 아니라 다음 배치도 시작하지 않아야 한다.
        lock (_lock) { _pending.Clear(); if (_current is { State: "running" } job) { job.StopRequested = true; job.Queued = []; } }
        var r = await q.Cli.RunAsync(new CliCommand("stop_action"), ct);
        q.Invalidate("get_activity");
        if (r.Ok && CliEnvelope.ActionError(r.Stdout) is { } why) r = r with { Ok = false, Error = why };   // 게임이 거절한 정지(invalid_state 등)를 "요청했습니다"로 알리지 않는다
        // 정지가 배치와 배치 사이(막 시작하려는 순간)에 끼면 stop_action이 멈출 대상을 못 찾는다. 잠시 뒤에도 같은 작업이 돌고 있으면 한 번 더 보낸다.
        GatherJob? running;
        lock (_lock) running = _current is { State: "running" } j && j.StopRequested ? j : null;
        if (running != null) _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            bool still;
            lock (_lock) still = running.State == "running";
            if (still) await q.Cli.RunAsync(new CliCommand("stop_action"));
        });
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
        // 프로세스가 정상 종료해도 게임이 거절·중단했을 수 있다(blocked·timeout·not_completed_yet·연결 끊김 …). 이것을 성공으로 알리면 게임은 가만있는데 앱만 "수거했습니다"가 된다.
        var head = r.Stdout.Length > 300 ? r.Stdout[..300] : r.Stdout;
        if (CliEnvelope.ActionError(r.Stdout) is { } why)
        {
            log.LogWarning("가공물 수거 실패 [{Name}]: {Why} (응답: {Head})", name, why, head);
            return (false, name, why);
        }
        log.LogInformation("가공물 수거 응답 [{Name}]: {Head}", name, head);
        // 캐릭터를 모르면 판정하지 않는다. 기본 키("에린_밀레시안")에 잘못 기록하지 않기 위해서다.
        var me = await q.Get<CharacterInfo>("get_my_info", ct);
        if (me.Value is { } ch && !string.IsNullOrWhiteSpace(ch.RealmName) && !string.IsNullOrWhiteSpace(ch.JobName))
            homework.Evaluate(homework.KeyOf(ch), new HomeworkObservation(CollectedByApp: true));
        return (true, name, null);
    }
}

/// <summary>
/// 숙제 판정 연결부: 조회 결과를 모아 HomeworkService에 넘기고, 바뀌면 SSE "homework.changed"로 알린다.
/// 조회에 실패한 항목은 null로 넘겨 판정 근거에서 뺀다.
/// </summary>
public sealed class HomeworkWatcher(HomeworkService homework, GameQueries q, SseHub hub, SnapshotManager snapshots, GameStateCache state, CurrencyDeltaLog currencyLog)
{
    /// <summary>캐릭터의 기록 이름. 헤더가 이미 정한 이름이 있으면 그것을 쓰고, 아니면 재화로 같은 서버·직업의 다른 캐릭터와 구분해 정한다.</summary>
    public string KeyOf(CharacterInfo ch, IEnumerable<CurrencyItem>? currencies = null) =>
        state.CurrentKey is { } k && CharacterIdentity.SameReading(state.Character, ch) ? k
            : snapshots.DetectJobSwap(state.CurrentKey, ch, currencies ?? state.Currencies, state.Currencies) ?? snapshots.ResolveKey(ch, currencies ?? state.Currencies);

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
        var tC = q.Get<List<CurrencyItem>>("get_currencies", ct);   // 레이드 증표 관찰 (FR-HW-17)
        var tI = q.Get<List<ItemData>>("get_items", ct);            // 레이드 고유 보상 아이템 (에이렐 하프 조각·서큐버스 거울 조각)
        await Task.WhenAll(tMe, tD, tW, tQ, tA, tE, tW2, tC, tI);

        if (tMe.Result.Value is not { } ch || string.IsNullOrWhiteSpace(ch.RealmName) || string.IsNullOrWhiteSpace(ch.JobName)) return null;
        var key = KeyOf(ch, tC.Result.Value);
        var currencies = tC.Result.Value;
        var items = tI.Result.Value;
        // 캐릭터가 바뀌었으면 재화·아이템 캐시가 이전 캐릭터 것일 수 있다: 새로 읽은 값으로 판정한다(레이드 클리어 비교는 캐릭터별 기준값과 하므로 틀린 캐릭터 값이 섞이면 안 된다).
        // 퀘스트는 판정기가 이번 관찰을 건너뛰고, 다음 조회는 새로 받게 한다.
        if (homework.LastObservedCharacter is { } last && !last.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            q.Invalidate("get_currencies", "get_items", "get_quests", "get_weekly_missions", "get_daily_missions");   // 미션도 캐릭터마다 다르다(2026-10-22 실측)
            var tC2 = q.Get<List<CurrencyItem>>("get_currencies", ct);
            var tI2 = q.Get<List<ItemData>>("get_items", ct);
            await Task.WhenAll(tC2, tI2);
            currencies = tC2.Result.Value;
            items = tI2.Result.Value;
        }
        currencyLog.Observe(key, currencies, tE.Result.Value?.GameSpaceDisplayName);   // 레이드 보상 실측용 (재화 변화 기록)
        Evaluate(key, new HomeworkObservation(
            tD.Result.Value, tW.Result.Value, tQ.Result.Value, tA.Result.Value, tE.Result.Value, tW2.Result.Value,
            Currencies: currencies, Items: items));
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
