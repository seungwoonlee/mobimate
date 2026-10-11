namespace MobiMateHomeWork.Services;

// 화면용 조회 결과. 웹앱은 익명 객체를 JSON으로 보냈지만 여기서는 형식 있는 레코드를 ViewModel이 바로 쓴다.

/// <summary>가방 무게 단계: 95%↑ 주의, 100%↑ 위험.</summary>
public enum WeightLevel { Ok, Warn, Danger }

/// <summary>가방 무게. 최대를 모르면 Max·Percent·Level이 null이다(현재 무게만 보여 준다).</summary>
public sealed record WeightView(double Current, double? Max, double? Percent, WeightLevel? Level)
{
    public static WeightLevel LevelOf(double pct) => pct >= 100 ? WeightLevel.Danger : pct >= 95 ? WeightLevel.Warn : WeightLevel.Ok;

    /// <param name="max">최대 무게. 0 이하면 모르는 것으로 본다.</param>
    public static WeightView Of(double current, double? max)
    {
        var cur = Math.Round(current, 1);
        if (max is not > 0) return new WeightView(cur, null, null, null);
        var pct = Math.Round(current / max.Value * 100, 1);
        return new WeightView(cur, max, pct, LevelOf(pct));
    }
}

/// <summary>은동전·마족 공물 (CoinForecast 결과): 보유 / 예상 / 상한 / 퍼센트(0~1) / 단계 / 가득까지 분.</summary>
public sealed record CoinView(long Held, long Expected, long Cap, double Percent, CoinLevel Level, int MinutesToFull)
{
    public static CoinView From(CoinState c) => new(c.Held, c.Expected, c.Cap, Math.Round(c.Percent, 4), c.Level, (int)Math.Round(c.MinutesToFull));
}

/// <summary>지금 있는 지역이 레이드·어비스·요일 던전·필드 보스 지역일 때의 "진행 중" 정보. Id는 숙제 칸 id.</summary>
public sealed record InProgressInfo(string Id, string Kind, string Title);

/// <summary>헤더(접속 중 캐릭터의 현재 모습). Selecting이면 캐릭터 선택창이라 아래 값은 마지막으로 본 캐릭터의 것이다.</summary>
public sealed record HeaderView(
    string CharacterKey, bool Selecting, string? Realm, string? Job, int Level, string? Title, string? Nickname,
    long Combat, long CombatDelta, long Mdef, long MdefDelta, long Living, long Attract,
    InProgressInfo? InProgress, string? Map, WeightView? Weight);

public sealed record MembershipView(DateTimeOffset? ExpiresAt, bool Active);
public sealed record CoinCaps(long Silver, long Tribute);

/// <summary>캐릭터 한 명의 카드/표 한 줄에 필요한 모든 값.</summary>
public sealed record CharacterView(
    string Key, int Variant, string Realm, string Job, string? Nickname, bool IsCurrent,
    int Level, string Title,
    // 현재 맵. 접속 중이면 실시간, 아니면 숙제 앱이 마지막으로 본 맵(없으면 null).
    string? Map,
    long Combat, long Mdef, long Living, long Attract,
    CoinView Silver, CoinView Tribute,
    // 가방 무게. 기록이 전혀 없으면 null.
    WeightView? Weight,
    DateTimeOffset LastSeen,
    // 데카·M캐시가 계정의 최신 값과 다르다: 마지막 접속 이후 동기화되지 않았다.
    bool Stale,
    // 자동 판정되는 숙제의 현황 (done / todo / unknown).
    IReadOnlyList<HomeworkAutoStatus> Homework,
    HomeworkPoolStatus FieldBoss,
    // 진행 중인 숙제 칸 id (접속 중 캐릭터가 숙제 지역에 있을 때만).
    IReadOnlyList<string> InProgressIds,
    // 은동전·공물 중 가득 차기에 더 가까운 쪽의 퍼센트(0~1) — 경고 정렬용.
    double Urgency);

public sealed record AccountView(
    string Id, string Name, bool Solo, MembershipView Membership, CoinCaps Caps,
    bool HasCurrent, int ManualRank, long TopCombat, IReadOnlyList<CharacterView> Members);

/// <summary>내 캐릭터 전체 현황. Accounts는 접속 중 계정이 맨 위, 나머지는 계정 안 최고 전투력 순.</summary>
public sealed record CharactersSnapshot(DateTimeOffset NowUtc, string? CurrentAccountId, IReadOnlyList<AccountView> Accounts);
