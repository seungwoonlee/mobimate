using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MobiMate;

public enum HomeworkPeriod { Daily, Weekly }
public enum HomeworkShare { Character, Account }

/// <summary>숙제 판정 방식. 항목마다 하나만 적용한다 (§4.3 H-10).</summary>
public enum HomeworkMode
{
    Manual,              // 수동 체크만 (진행 신호는 붙을 수 있음)
    ManualStepCounter,   // 수동 단계 카운터 0~goal
    DirectMission,       // 지정한 미션 제목과 정확히 일치하는 미션의 진행도 (FR-HW-04 ①)
    MissionTotal,        // 일일·주간 미션 전체 완료 개수
    QuestAllObjectives,  // 지정한 퀘스트의 모든 목표 완료 (FR-HW-04 ②)
    AlteringCollected,   // 가공물 수거 관찰 (FR-HW-05)
    QuestVanish,         // 퀘스트 트래커에 보이면 미완료, 이번 주기에 보였다가 사라지면 완료 (요일 던전)
    QuestSuffix          // 퀘스트 이름 뒤 "(N)"이 붙으면 이번 주기에 N번 클리어 (주간 목표 정기 의뢰)
}

public sealed class HomeworkDefinition
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public HomeworkPeriod Period { get; init; }
    public HomeworkShare Share { get; init; }
    public HomeworkMode Mode { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string Icon { get; init; } = "";
    public int Goal { get; init; } = 1;
    public string? Reward { get; init; }
    public string? Pool { get; init; }
    public List<string> MissionTitles { get; init; } = new();
    public List<string> QuestTitles { get; init; } = new();
    public List<string> BossNames { get; init; } = new();
    public List<string> SpaceNames { get; init; } = new();

    /// <summary>지역 이름이 이 말로 시작하면 이 콘텐츠의 지역으로 본다 (예: "찬란한 유적 V"의 로마 숫자는 단계라서 바뀐다). 정규화 후 비교.</summary>
    public List<string> SpaceNamePrefixes { get; init; } = new();

    /// <summary>요일별 퀘스트 이름 (키: mon~sun, 게임의 하루는 06:00에 바뀐다). QuestVanish에서 오늘 보고 있어야 할 퀘스트를 정한다.</summary>
    public Dictionary<string, List<string>> DayQuestTitles { get; init; } = new();

    /// <summary>false면 지금 열려 있지 않은 콘텐츠: 목록·진행 통계·판정에서 빠진다 (예: 지금 열려 있지 않은 레이드).</summary>
    public bool Active { get; init; } = true;

    /// <summary>재화 수량이 늘면 제안이 아니라 곧바로 자동 완료로 본다 (TokenCurrency와 함께).</summary>
    public bool TokenAuto { get; init; }

    /// <summary>자동으로 판정되는 숙제: 전체 탭의 캐릭터 카드에 완료 여부를 보여 준다.</summary>
    public bool AutoCheck { get; init; }

    /// <summary>퀘스트 이름을 완전 일치가 아니라 "포함"으로 찾는다 (이름 앞뒤에 [긴급 의뢰] 같은 말이 붙는 퀘스트용). 공백·태그 정규화 후 비교.</summary>
    public bool TitleContains { get; init; }

    /// <summary>
    /// QuestVanish에서 "보였다가 사라짐"을 같은 캐릭터를 끊김 없이 지켜본 경우에만 인정한다:
    /// 다른 캐릭터를 관찰하면 이전 캐릭터의 목격 기록을 지운다 (로그아웃·캐릭터 변경 사이에 사라진 것은 완료의 근거가 아니다).
    /// </summary>
    public bool Continuous { get; init; }

    /// <summary>퀘스트가 보일 때 남은 횟수를 읽어 둔다 (목표의 Count/Goal, 없으면 이름의 N/M). 카드 칩에 이름(N)으로 보인다.</summary>
    public bool ShowRemaining { get; init; }

    /// <summary>캐릭터 카드 칩에 쓰는 짧은 이름. 없으면 Title.</summary>
    public string? CardLabel { get; init; }

    /// <summary>
    /// 레이드 지역(GameSpaceDisplayName) 이름. 이 지역에 들어와 있으면 그 레이드를 클리어한 것으로 본다(가장 우선인 근거).
    /// 실패한 경우는 숙제 화면에서 수동으로 되돌린다. 공백·태그 정규화 후 완전 일치.
    /// </summary>
    public List<string> EntrySpaceNames { get; init; } = new();

    /// <summary>
    /// 보상 재화가 늘어도 SpaceNames의 지역 안에서(또는 방금까지 있었던 경우에만) 클리어로 본다 (어비스: 지역 진입 뒤 마물 퇴치 증표 획득).
    /// 다른 곳에서 같은 재화를 얻어도(카브락 +270 등) 클리어로 세지 않는다.
    /// </summary>
    public bool TokenRequiresSpace { get; init; }

    /// <summary>
    /// 이 퀘스트가 트래커에 보이면 클리어한 것이다 (필드 보스: 처치 직후 "토벌" 퀘스트가 "영역 나가기"로 바뀐다, 2026-10-05 실측).
    /// 목록이 잠깐 비어도(처치 순간) 판단하지 않고, 비어 있지 않은 목록에서 안 보일 때만 "없음"으로 본다. 정규화 후 완전 일치.
    /// </summary>
    public List<string> ClearQuestTitles { get; init; } = new();

    /// <summary>클리어 보상 재화 이름 (FR-HW-17). 수량이 늘면 "클리어 추정" 제안만 한다. 공백 정규화 후 완전 일치로 찾는다.</summary>
    public string? TokenCurrency { get; init; }

    /// <summary>이 수량 이상 늘었을 때만 클리어로 본다 (기본 1). 카브락 증거는 다른 레이드를 주말에 클리어해도 +2가 들어와서(실측 2026-10-03) 카브락 입문 최소치(18)로 거른다.</summary>
    public int TokenMinIncrease { get; init; } = 1;

    /// <summary>TokenCurrency가 재화가 아니라 가방 아이템 이름이다 (에이렐 하프 조각·서큐버스 거울 조각 같은 레이드 고유 보상). 아이템은 0개가 되면 목록에서 사라지므로, 목록을 받았는데 없으면 0개로 본다.</summary>
    public bool TokenIsItem { get; init; }

    /// <summary>실측이 필요한 사항. 값이 있으면 자동 완료 규칙을 쓰지 않고 수동으로 동작한다 (TST-10).</summary>
    public string? NeedsMeasurement { get; init; }

    /// <summary>실제로 적용되는 판정 방식. 실측 전이면 자동 방식도 수동으로 낮춘다.</summary>
    [JsonIgnore]
    public HomeworkMode EffectiveMode =>
        NeedsMeasurement != null && Mode is HomeworkMode.DirectMission or HomeworkMode.QuestAllObjectives or HomeworkMode.MissionTotal
            ? HomeworkMode.Manual
            : Mode;

    [JsonIgnore]
    public bool HasProgressSignal => BossNames.Count > 0 || SpaceNames.Count > 0;
}

public sealed class HomeworkPoolDefinition
{
    public string Title { get; init; } = "";
}

public sealed class HomeworkCatalog
{
    public int Version { get; init; }
    /// <summary>
    /// 지금 있는 지역(GameSpaceDisplayName)이 어느 콘텐츠(레이드·어비스·요일 던전)의 지역인지 찾는다. 공백·태그 정규화 후 완전 일치. 없으면 null.
    /// 헤더의 "진행 중" 표시에 쓴다.
    /// </summary>
    public HomeworkDefinition? FindByArea(string? space)
    {
        var n = HomeworkText.Normalize(space);
        if (n.Length == 0) return null;
        return Items.FirstOrDefault(d => d.Active
            && (d.EntrySpaceNames.Concat(d.SpaceNames).Any(a => HomeworkText.Normalize(a) == n)
                || d.SpaceNamePrefixes.Any(p => n.StartsWith(HomeworkText.Normalize(p), StringComparison.Ordinal))));
    }

    public Dictionary<string, HomeworkPoolDefinition> SharedPools { get; init; } = new();
    public List<HomeworkDefinition> Items { get; init; } = new();

    private Dictionary<string, HomeworkDefinition>? _byId;

    public HomeworkDefinition? Find(string id)
    {
        _byId ??= Items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        return _byId.TryGetValue(id, out var d) ? d : null;
    }

    public static readonly Dictionary<string, string> CategoryTitles = new()
    {
        ["daily"] = "일일 달성",
        ["weekly"] = "주간 달성",
        ["fieldBoss"] = "필드 보스",
        ["raid"] = "레이드",
        ["abyss"] = "어비스",
        ["account"] = "계정 미션",
        ["goal"] = "주간 목표",
        ["guild"] = "길드",
        ["life"] = "생활",
        ["shop"] = "상점·교환",
    };

    /// <summary>여러 콘텐츠에 겹치는 일반어. 매칭 키로 단독 사용 금지 (FR-HW-07).</summary>
    public static readonly string[] GenericWords = { "던전", "동굴", "채집", "요일", "광기", "물길", "허상", "흩어진", "검은구멍", "결계", "성수", "데카", "기부", "길드", "가공", "미션" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static HomeworkCatalog LoadEmbedded()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("MobiMate.Homework.homework_catalog.json")
            ?? throw new InvalidOperationException("내장 숙제 카탈로그를 찾지 못했습니다.");
        return Parse(s);
    }

    public static HomeworkCatalog Parse(Stream json) =>
        JsonSerializer.Deserialize<HomeworkCatalog>(json, Json) ?? throw new InvalidDataException("숙제 카탈로그가 비어 있습니다.");

    public static HomeworkCatalog Parse(string json) =>
        JsonSerializer.Deserialize<HomeworkCatalog>(json, Json) ?? throw new InvalidDataException("숙제 카탈로그가 비어 있습니다.");

    /// <summary>카탈로그 검증 (TST-10). 문제 목록을 돌려준다(비어 있으면 정상).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var dup in Items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"ID 중복: {dup.Key}");

        foreach (var i in Items)
        {
            if (string.IsNullOrWhiteSpace(i.Id) || string.IsNullOrWhiteSpace(i.Title)) errors.Add($"ID·제목 누락: {i.Id}");
            if (!CategoryTitles.ContainsKey(i.Category)) errors.Add($"알 수 없는 분류: {i.Id} → {i.Category}");
            if (i.Goal < 1) errors.Add($"목표 수가 1 미만: {i.Id}");
            if (i.Pool != null && !SharedPools.ContainsKey(i.Pool)) errors.Add($"정의되지 않은 공유 풀: {i.Id} → {i.Pool}");

            if (i.Mode == HomeworkMode.DirectMission && i.MissionTitles.Count == 0) errors.Add($"1:1 미션 제목 없음: {i.Id}");
            if (i.Mode is HomeworkMode.QuestAllObjectives or HomeworkMode.QuestSuffix && i.QuestTitles.Count == 0) errors.Add($"퀘스트 제목 없음: {i.Id}");
            if (i.Mode == HomeworkMode.QuestVanish && i.QuestTitles.Count == 0 && i.DayQuestTitles.Count == 0) errors.Add($"퀘스트 제목 없음: {i.Id}");
            if (i.TokenAuto && string.IsNullOrWhiteSpace(i.TokenCurrency)) errors.Add($"증표 재화 없는 TokenAuto: {i.Id}");

            // 매칭 키는 정규화 후 일반어 단독이면 안 된다
            foreach (var key in i.MissionTitles.Concat(i.QuestTitles).Concat(i.DayQuestTitles.Values.SelectMany(v => v)).Concat(i.BossNames).Concat(i.SpaceNames).Concat(i.SpaceNamePrefixes).Concat(i.ClearQuestTitles))
            {
                var n = HomeworkText.Normalize(key);
                if (n.Length < 2 || GenericWords.Contains(n)) errors.Add($"일반어 단독 매칭 키: {i.Id} → \"{key}\"");
            }
        }
        return errors;
    }
}

/// <summary>게임 문구 정규화 (K-04): 유니티 리치텍스트 태그 제거 → 공백 제거 → 소문자.</summary>
public static partial class HomeworkText
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    public static string StripTags(string? text) => string.IsNullOrEmpty(text) ? "" : Tag().Replace(text, "").Trim();

    public static string Normalize(string? text) =>
        string.IsNullOrEmpty(text) ? "" : Space().Replace(Tag().Replace(text, ""), "").ToLowerInvariant();
}
