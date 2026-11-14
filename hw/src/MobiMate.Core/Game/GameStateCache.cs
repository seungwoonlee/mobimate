namespace MobiMate;

/// <summary>
/// 조회 결과의 최신값을 모아 둔다 (상세설계 §2.7).
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

    /// <summary>지금 캐릭터의 기록 이름 ("서버_직업" 또는 같은 서버·직업의 다른 캐릭터면 "서버_직업#2"). 모르면 null.</summary>
    public string? CurrentKey { get; private set; }
    public void SetCurrentKey(string? key) => CurrentKey = key;

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
        if (a.IsReviving) return "부활 중";
        if (a.IsInCombat) return a.IsAutoPlaying ? "자동 사냥 · 전투 중" : "전투 중";
        // 게임 업데이트(2026-10-15) 뒤 채집·가공·제작 중 플래그가 없어졌다: 정지 버튼이 보이는 상태는 새 신호(Mode.MainButtonState)로 알린다
        if (a.IsGathering) return "채집 중";
        if (a.IsAltering) return "가공 중";
        if (a.IsCrafting) return "제작 중";
        if (a.IsPlayingMusic()) return "연주 중";
        if (a.IsAutoTraveling) return "자동 이동 중";
        if (a.IsAutoPlaying) return "자동 사냥 중";
        if (a.IsSittingOnChair()) return "휴식 중";
        if (a.Interaction is { LastRunningInteractionType: "Gathering" }) return "채집 중";
        if (a.CanStop()) return "작업 중";
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
}
