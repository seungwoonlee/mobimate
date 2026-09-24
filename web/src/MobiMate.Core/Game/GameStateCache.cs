namespace MobiMate;

/// <summary>
/// 조회 결과의 최신값을 모아 두고 AI 컨텍스트·아무말 컨텍스트를 한곳에서 만든다 (상세설계 §2.7).
/// 스레드 안전: 갱신은 참조 교체만 한다.
/// </summary>
public sealed class GameStateCache
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly Func<DateTimeOffset> _now;

    public GameStateCache(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    public CharacterInfo? Character { get; private set; }
    public ActivityInfo? Activity { get; private set; }
    public EnvironmentInfo? Environment { get; private set; }
    public IReadOnlyList<CurrencyItem>? Currencies { get; private set; }
    public IReadOnlyList<MissionItem>? DailyMissions { get; private set; }
    public IReadOnlyList<GatherableItem>? Gatherables { get; private set; }
    public DateTimeOffset? HeaderFetchedAt { get; private set; }

    public bool IsHeaderStale => HeaderFetchedAt is null || _now() - HeaderFetchedAt > StaleAfter;

    public void UpdateHeader(CharacterInfo? ch, ActivityInfo? act, EnvironmentInfo? env)
    {
        if (ch != null) Character = ch;
        if (act != null) Activity = act;
        if (env != null) Environment = env;
        HeaderFetchedAt = _now();
    }

    public void UpdateCurrencies(IReadOnlyList<CurrencyItem>? list) { if (list != null) Currencies = list; }

    /// <summary>캐릭터가 바뀌면 캐릭터별 값(재화·일일 미션)을 비운다. 이전 캐릭터 값이 새 캐릭터의 세션 기준값이 되지 않게 한다.</summary>
    public void ClearPerCharacter()
    {
        Currencies = null;
        DailyMissions = null;
    }
    public void UpdateDailyMissions(IReadOnlyList<MissionItem>? list) { if (list != null) DailyMissions = list; }
    public void UpdateGatherables(IReadOnlyList<GatherableItem>? list) { if (list != null) Gatherables = list; }

    public static string DescribeActivity(ActivityInfo? a)
    {
        if (a == null) return "알 수 없음";
        if (a.IsDead) return "쓰러짐";
        if (a.IsInCombat) return a.IsAutoPlaying ? "자동 사냥 · 전투 중" : "전투 중";
        if (a.IsGathering) return "채집 중";
        if (a.IsAltering) return "가공 중";
        if (a.IsCrafting) return "제작 중";
        if (a.IsPlayingInstrument) return "연주 중";
        if (a.IsAutoTraveling) return "자동 이동 중";
        if (a.IsAutoPlaying) return "자동 사냥 중";
        if (a.IsSitting) return "휴식 중";
        return "대기 중";
    }

    public static string WeightSummary(VitalsInfo? v)
    {
        if (v == null || v.WeightMax <= 0) return "알 수 없음";
        var pct = v.WeightCurrent / v.WeightMax * 100.0;
        return $"{v.WeightCurrent:F1} / {v.WeightMax:F1} ({pct:F1}%)";
    }

    public string LocationSummary()
    {
        var ch = Environment?.ChannelName;
        return string.IsNullOrWhiteSpace(ch) ? "알 수 없음" : ch;
    }

    /// <summary>AI 시스템 컨텍스트 (WPF ProcessAiQueryAsync와 같은 내용).</summary>
    public string BuildAiContext()
    {
        var ch = Character;
        var realm = string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch!.RealmName;
        var job = string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch!.JobName;
        var level = ch?.Level ?? 1;
        var combat = ch?.CombatScore?.Value ?? 0;

        return "당신은 넥슨 '마비노기 모바일'의 든든한 플레이 동반자 AI 도우미(MobiMate)입니다.\n" +
               "[현재 플레이어 실시간 게임 상태]\n" +
               $"- 서버: {realm} / 직업: {job} (Lv.{level})\n" +
               $"- 전투력: {combat:N0}점\n" +
               $"- 현재 상태: {DescribeActivity(Activity)} / 위치: {LocationSummary()}\n" +
               $"- 가방 무게 현황: {WeightSummary(ch?.Vitals)}\n\n" +
               "플레이어의 질문에 대해 마비노기 모바일 게임 공략과 현재 캐릭터 상태에 맞추어 친절하고 간결하게 2~3문장 이내의 한국어로 답변해주세요.";
    }

    /// <summary>아무말 대잔치 컨텍스트 (WPF GetCurrentChatterContext와 같은 내용).</summary>
    public ChatterContext BuildChatterContext()
    {
        var ch = Character;
        var gold = Currencies?.FirstOrDefault(c => c.DisplayName == "골드")?.Amount;
        return new ChatterContext(
            Realm: string.IsNullOrWhiteSpace(ch?.RealmName) ? "에린" : ch!.RealmName,
            Job: string.IsNullOrWhiteSpace(ch?.JobName) ? "밀레시안" : ch!.JobName,
            Level: ch?.Level ?? 1,
            CombatScore: ch?.CombatScore?.Value ?? 0,
            Activity: DescribeActivity(Activity),
            Location: LocationSummary(),
            WeightSummary: WeightSummary(ch?.Vitals),
            GoldSummary: gold.HasValue ? $"{gold.Value:N0} 골드" : "알 수 없음",
            ErinnTime: Environment?.ErinnNow ?? "");
    }
}
