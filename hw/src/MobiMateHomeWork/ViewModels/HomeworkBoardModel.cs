using MobiMateHomeWork.Services;

namespace MobiMateHomeWork.ViewModels;

/// <summary>칸 상태: 완료 / 미완료(이번 주기에 직접 확인) / 미확인(아직 확인 전, 미완료로 본다) / 입장 불가(게임이 알려 주는 잠금: 레벨·퀘스트 조건).</summary>
public enum CellState { Done, Todo, Unknown, Locked }

/// <summary>
/// 숙제 표의 칸 하나의 정의 (웹앱 homework.tsx의 HW_GROUPS와 같은 id·이름).
/// Short = 좁은/보통 단계의 묶음 머리에 쓰는 두 글자 약칭, Line1·Line2 = 넓은 단계에서 칸 안에 쓰는 두 줄 이름.
/// </summary>
public sealed record CellDef(string Id, string Label, string Short, string Line1, string Line2, bool IsBoss = false, int StepGoal = 0)
{
    /// <summary>여러 번 해야 끝나는 숙제의 수동 단계 카운터(소환의 결계 7단계·검은 구멍 14회): 누를 때마다 0 → 1 → … → 목표 → 0.</summary>
    public bool IsStep => StepGoal > 0 && !IsSeason;

    public bool IsSeason => Id is "weekly_nightmare_drill" or "weekly_nightmare_rank" or "weekly_memorial_saga";
}

public sealed record GroupDef(string Title, IReadOnlyList<CellDef> Cells);

/// <summary>필드 보스 하나 (카탈로그 field_boss_weekly 풀의 항목).</summary>
public sealed record BossDef(string Id, string Title);

/// <summary>한 캐릭터의 칸 하나가 지금 어떤 모습인가. 화면 모델이 이 값을 받아 그린다.</summary>
public sealed record CellData(
    CellDef Def, CellState State, int? Left, string? Evidence, string? Note, HomeworkSuggestion? Suggestion, bool InProgress, int? Value = null, int? Step = null);

/// <summary>숙제 표의 틀과 칸 만들기 규칙 (웹앱 homework.tsx의 HW_GROUPS·cellsOf·BOSSES와 같은 규칙).</summary>
public static class HomeworkBoardLayout
{
    /// <summary>필드 보스 칸의 id. 이 칸은 카탈로그 숙제가 아니라 풀(field_boss_weekly) 하나를 가리킨다.</summary>
    public const string FieldBossCellId = "field_boss";
    public const string FieldBossPool = "field_boss_weekly";

    public static readonly IReadOnlyList<GroupDef> Groups =
    [
        new("일일·주간",
        [
            new("daily_day_dungeon", "요일던전", "요일", "요일", "던전"),
            new("weekly_guild_regular", "길드의뢰", "길드", "길드", "의뢰"),
            new("weekly_vanguard_breach", "뱅가드", "뱅가", "뱅가드", ""),
            new("weekly_barrier_1_7", "소환의 결계", "결계", "소환의", "결계", StepGoal: 7),
            new("weekly_black_hole_1_7", "검은 구멍", "구멍", "검은", "구멍", StepGoal: 14),
        ]),
        new("레이드",
        [
            new("raid_scars", "적월의 스카스", "스카", "적월의", "스카스"),
            new("raid_cavrak", "카브락", "카브", "카브락", ""),
            new("raid_airel", "에이렐", "에이", "에이렐", ""),
            new("raid_white_succubus", "화이트 서큐버스", "화이", "화이트", "서큐버스"),
        ]),
        new("어비스",
        [
            new("abyss_illusory_anchorage", "허상의 정박지", "허상", "허상의", "정박지"),
            new("abyss_madness_cave", "광기의 동굴", "광기", "광기의", "동굴"),
            new("abyss_scattered_waterway", "흩어진 물길", "물길", "흩어진", "물길"),
        ]),
        new("필드", [new(FieldBossCellId, "필드보스", "보스", "필드", "보스", IsBoss: true)]),
    ];

    public static readonly IReadOnlyList<GroupDef> SeasonGroups =
    [
        new("시즌 기록 · 누르면 숫자 입력", [
            new("weekly_nightmare_drill", "나이트메어 드릴 구역", "구역", "드릴", "구역"),
            new("weekly_nightmare_rank", "나이트메어 드릴 랭킹", "순위", "드릴", "순위"),
            new("weekly_memorial_saga", "메모리얼 사가 층", "사가", "메모리얼", "사가"),
        ]),
    ];
    public static readonly IReadOnlyList<CellDef> SeasonCells = [.. SeasonGroups.SelectMany(g => g.Cells)];

    public static readonly IReadOnlyList<CellDef> Cells = [.. Groups.SelectMany(g => g.Cells)];

    /// <summary>필드 보스 여섯. 다른 캐릭터의 보스도 고쳐야 하므로 현재 캐릭터와 상관없이 고정 목록이다.</summary>
    public static readonly IReadOnlyList<BossDef> Bosses =
    [
        new("fieldboss_peri", "페리"), new("fieldboss_crabbach", "크라브바흐"), new("fieldboss_krama", "크라마"),
        new("fieldboss_drochenem", "드로흐에넴"), new("fieldboss_tormog", "토르모그"), new("fieldboss_angrbahan", "앙그르바한"),
    ];

    /// <summary>
    /// 한 캐릭터의 칸 열 개: 일반 숙제 아홉 + 필드 보스 하나(웹앱 cellsOf). 현황에 없는 칸은 미확인이다.
    /// 남은 횟수는 미완료일 때만 갖는다. 진행 중은 접속 중 캐릭터가 그 숙제의 지역에 있을 때(필드 보스는 여섯 중 어느 보스든).
    /// </summary>
    public static IReadOnlyList<CellData> CellsOf(CharacterView c)
    {
        var byId = c.Homework.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        var bossIds = Bosses.Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = new List<CellData>(Cells.Count);
        foreach (var def in Cells)
        {
            if (def.IsBoss)
            {
                var pool = c.FieldBoss;
                var state = StateOf(pool.State);
                var progress = c.InProgressIds.Any(bossIds.Contains) || c.InProgressIds.Contains(FieldBossCellId);
                var guess = pool.Guess && state != CellState.Done ? new HomeworkSuggestion("missionCleared", "필드 보스 토벌", 0, 1) : null;   // 미션은 올랐는데 어느 보스인지 모름
                list.Add(new CellData(def, state, null, null, state == CellState.Done ? pool.Item : null, guess, progress));
                continue;
            }
            byId.TryGetValue(def.Id, out var h);
            // 게임이 직접 알려 주는 잠금(성장 가이드의 UnlockHint, 예: "85레벨에 사용할 수 있습니다.")이 가장 확실하다
            if (!string.IsNullOrWhiteSpace(h?.Locked))
            {
                list.Add(new CellData(def, CellState.Locked, null, null, h!.Locked, null, false));
                continue;
            }
            var st = StateOf(h?.State);
            int? step = def.IsStep ? (st == CellState.Done ? def.StepGoal : st == CellState.Todo && h?.Remaining is { } r ? Math.Clamp(def.StepGoal - r, 0, def.StepGoal) : 0) : null;
            list.Add(new CellData(def, st, def.IsStep ? null : st == CellState.Todo && h?.Remaining > 1 ? h.Remaining : null, h?.Evidence, null,
                st == CellState.Done ? null : h?.Suggestion, c.InProgressIds.Contains(def.Id, StringComparer.OrdinalIgnoreCase), null, step));
        }
        return list;
    }

    public static IReadOnlyList<CellData> SeasonCellsOf(CharacterView c)
    {
        var byId = c.Homework.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        return SeasonCells.Select(def =>
        {
            byId.TryGetValue(def.Id, out var status);
            var value = status?.Value;
            return new CellData(def, value is > 0 ? CellState.Todo : CellState.Unknown, null,
                status?.Evidence, null, null, false, value);
        }).ToList();
    }

    private static CellState StateOf(string? s) => s switch
    {
        "done" => CellState.Done,
        "todo" => CellState.Todo,
        _ => CellState.Unknown,
    };

    /// <summary>칸 상태 글: 완료 / 미완료 / 남은 N회 / 미확인.</summary>
    public static string StatusText(CellData d) => d.State switch
    {
        CellState.Locked => "입장 불가",
        _ when d.Def.IsStep => $"{d.Step ?? 0}/{d.Def.StepGoal}",
        CellState.Done => "완료",
        CellState.Todo => d.Left is { } n ? $"남은 {n}회" : "미완료",
        _ => "미확인",
    };

    /// <summary>
    /// 칸 툴팁: 전체 이름 + 판정 근거(없으면 수동), 필드 보스는 넘긴 보스 이름. 완료 제안·진행 중과 누르면 일어나는 일도 덧붙인다.
    /// </summary>
    public static string TooltipOf(CellData d)
    {
        var lines = new List<string>();
        if (d.State == CellState.Locked)
        {
            lines.Add($"{d.Def.Label} · 입장 불가 — {d.Note}");
            return string.Join('\n', lines);
        }
        if (d.Def.IsStep)
        {
            lines.Add($"{d.Def.Label} · {d.Step ?? 0}/{d.Def.StepGoal}{(d.State == CellState.Unknown ? " (아직 확인 전)" : "")}");
            lines.Add("누르면 한 단계 올리고, 우클릭하면 한 단계 내립니다");
            return string.Join('\n', lines);
        }
        if (d.Def.IsBoss)
        {
            lines.Add(d.State switch
            {
                CellState.Done => $"{d.Def.Label} · 완료 — 넘긴 보스: {(string.IsNullOrWhiteSpace(d.Note) ? "알 수 없음" : d.Note)}",
                CellState.Todo => $"{d.Def.Label} · 미완료 (여섯 보스 중 하나, 주 1회)",
                _ => $"{d.Def.Label} · 아직 확인 전 — 미완료로 봅니다",
            });
        }
        else
        {
            lines.Add(d.State switch
            {
                CellState.Done => $"{d.Def.Label} · 완료 — {(string.IsNullOrWhiteSpace(d.Evidence) ? "수동" : d.Evidence)}",
                CellState.Todo => $"{d.Def.Label} · {StatusText(d)}{(string.IsNullOrWhiteSpace(d.Evidence) ? "" : " — " + d.Evidence)}",
                _ => $"{d.Def.Label} · 아직 확인 전 — 미완료로 봅니다",
            });
        }
        if (d.Suggestion is { } s) lines.Add("완료 제안: " + SuggestionText(s));
        if (d.InProgress) lines.Add("진행 중 — 지금 이 숙제의 지역에 있습니다");
        lines.Add(d.Def.IsBoss && d.State != CellState.Done ? "누르면 넘긴 보스를 고릅니다"
            : d.State == CellState.Done ? "누르면 미완료로 바꿉니다" : "누르면 완료로 바꿉니다");
        return string.Join('\n', lines);
    }

    public static string SuggestionText(HomeworkSuggestion s) => s.Code switch
    {
        "raidTokenIncreased" => $"{s.Item}이(가) {s.From:N0}에서 {s.To:N0}(으)로 늘었습니다",
        "raidMapEntered" => $"레이드 지역({s.Item})에 들어왔습니다",
        "bossCleared" => $"보스 처치를 확인했습니다 ({s.Item})",
        "missionCleared" => $"주간 미션 '{s.Item}'이 올랐습니다 — 어느 것을 깼는지 눌러서 확인하세요(클리어 추정)",
        _ => string.IsNullOrWhiteSpace(s.Item) ? s.Code : s.Item,
    };

    /// <summary>캐릭터 한 명의 접근성 이름: "닉네임 숙제 7/10 완료".</summary>
    public static string RowAutomationName(string name, int done, int total) => $"{name} 숙제 {done}/{total} 완료";
}
