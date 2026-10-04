using System.Text.Json;

namespace MobiMate;

/// <summary>숙제 항목 하나의 이번 주기 상태.</summary>
public sealed class HomeworkItemState
{
    public bool Completed { get; set; }
    public int Count { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public bool Auto { get; set; }

    /// <summary>근거가 된 실제 목표 수(미션의 GoalCount 등). 없으면 카탈로그 목표 수를 쓴다.</summary>
    public int? Goal { get; set; }

    /// <summary>사용자가 직접 설정했다. 설정되면 이번 주기 동안 자동 판정이 덮어쓰지 않는다 (FR-HW-08). 리셋 때 해제.</summary>
    public bool ManualOverride { get; set; }

    /// <summary>수동으로 설정한 시각. 수동 미완료 뒤에 새로 생긴 확실한 증거(레이드 지역 새 진입 등)만 수동 설정을 이긴다. 옛 기록은 null.</summary>
    public DateTimeOffset? ManualAtUtc { get; set; }

    /// <summary>자동 완료 근거 (FR-HW-13).</summary>
    public string? Evidence { get; set; }

    /// <summary>퀘스트에서 읽은 남은 횟수 (ShowRemaining 항목, 예: 뱅가드 브리치 3 → 2 → 1). 모르면 null. 완료·리셋 때 사라진다.</summary>
    public int? RemainingCount { get; set; }

    /// <summary>완료 제안 (FR-HW-17, 예: 레이드 증표 증가). 완료로 세지 않는다. 완료·수동 설정·리셋 때 사라진다.</summary>
    public HomeworkSuggestion? Suggestion { get; set; }
}

/// <summary>완료 제안. Code = "raidTokenIncreased", Item = 근거 재화 이름, From→To = 관찰한 수량 변화. 문구는 화면이 만든다.</summary>
public sealed record HomeworkSuggestion(string Code, string Item, long From, long To);

/// <summary>캐릭터 하나(또는 계정 공통)의 숙제 장부.</summary>
public sealed class HomeworkLedger
{
    public DateTimeOffset LastDailyResetUtc { get; set; }
    public DateTimeOffset LastWeeklyResetUtc { get; set; }
    public Dictionary<string, HomeworkItemState> Items { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>가공 수거 관찰용: 직전 성공 조회의 (시설|작업명)별 완료 작업 수 (FR-HW-05). 캐릭터 장부에만 쓴다.</summary>
    public Dictionary<string, int>? AlteringDoneCounts { get; set; }
    public DateTimeOffset? AlteringObservedUtc { get; set; }

    /// <summary>레이드 증표 관찰용: 직전 성공 조회의 (정규화한 재화 이름)별 수량 (FR-HW-17). 재기동해도 이어지도록 저장한다.</summary>
    public Dictionary<string, long>? RaidTokens { get; set; }
    public DateTimeOffset? RaidTokensObservedUtc { get; set; }

    /// <summary>퀘스트를 트래커에서 본 시각(숙제 ID별). QuestVanish에서 "이번 주기에 보였다가 사라짐"을 알아내는 근거다. 리셋 때 지운다.</summary>
    public Dictionary<string, DateTimeOffset>? QuestSightings { get; set; }

    /// <summary>레이드 지역에 들어와 있는 동안 처음 본 시각(숙제 ID별). 지역을 벗어나면 지운다. 수동 미완료 뒤의 "새 진입"인지 가려내는 근거다.</summary>
    public Dictionary<string, DateTimeOffset>? RaidAreaSince { get; set; }

    /// <summary>
    /// 끊김 없는 관찰이 필요한 항목(Continuous) 중, 목격한 뒤 다른 캐릭터를 관찰해 끊긴 것의 ID. 목격 기록(미완료 확인·남은 횟수)은 그대로 두고
    /// "사라짐 = 완료" 판정만 막는다. 퀘스트가 다시 보이거나 리셋되면 해제된다.
    /// </summary>
    public HashSet<string>? QuestContinuityBroken { get; set; }

    /// <summary>JSON에서 읽으면 대소문자 무시 비교자가 사라지므로 읽은 직후 다시 씌운다.</summary>
    internal void RestoreComparers()
    {
        Items = new Dictionary<string, HomeworkItemState>(Items ?? new(), StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>homework_records.json 전체 (FR-HW-14). 계정 공통 장부 + 캐릭터별 장부 (H-8).</summary>
public sealed class HomeworkFile
{
    public int Version { get; set; } = 1;
    public HomeworkLedger Account { get; set; } = new();

    /// <summary>마지막으로 판정한 캐릭터 키. 재기동해도 "사이에 다른 캐릭터 관찰"을 알 수 있게 저장한다 (FR-HW-17).</summary>
    public string? LastObservedCharacter { get; set; }
    public Dictionary<string, HomeworkLedger> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>숙제 기록 저장소. JsonFileStore(고유 임시 파일 + 원자적 교체)를 쓴다 (H-9).</summary>
public sealed class HomeworkStore
{
    public const string FileName = "homework_records.json";

    private readonly JsonFileStore _store = new();
    public string FilePath { get; }

    public HomeworkStore(string storageDir) => FilePath = Path.Combine(storageDir, FileName);

    /// <summary>파일을 읽는다. 읽기에 일시적으로 실패하면 null(빈 기록으로 덮어쓰지 않게 호출자가 건너뛴다).</summary>
    public HomeworkFile? TryLoad()
    {
        var status = _store.TryLoad<HomeworkFile>(FilePath, out var file);
        if (status == LoadStatus.Failed) return null;
        file ??= new HomeworkFile();
        file.Account ??= new HomeworkLedger();
        file.Account.RestoreComparers();
        file.Characters = new Dictionary<string, HomeworkLedger>(file.Characters ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (var l in file.Characters.Values) l.RestoreComparers();
        return file;
    }

    public HomeworkFile Load() => TryLoad() ?? throw new IOException($"숙제 기록을 읽을 수 없습니다: {FilePath}");

    public bool Save(HomeworkFile file) => _store.Save(FilePath, file);

    /// <summary>
    /// 웹앱 쪽 파일이 없을 때만 WPF판 homework_records.json을 변환해서 가져온다 (NFR-13, 파일별 규칙). 원본에는 쓰지 않는다.
    /// - 캐릭터를 알 수 없는 "Default_Player"는 버린다.
    /// - 계정 공통 항목은 가장 최근 완료 값을 계정 장부로 옮긴다.
    /// - 이번 주기(현재 KST 기준 마지막 리셋 이후)에 완료된 것만 남긴다.
    /// - 사용자가 직접 체크한 완료만 옮긴다. WPF판 자동 판정에는 오탐 경로(§4.3)가 있으므로 자동 항목은 웹앱 규칙으로 다시 판정한다.
    /// </summary>
    /// <returns>가져온 캐릭터 수. 가져오지 않았으면 0.</returns>
    public int ImportFromWpfIfMissing(string wpfDir, HomeworkCatalog catalog, DateTimeOffset nowUtc)
    {
        if (File.Exists(FilePath)) return 0;
        var src = Path.Combine(wpfDir, FileName);
        if (!File.Exists(src)) return 0;

        Dictionary<string, WpfRecord>? wpf;
        try
        {
            wpf = JsonSerializer.Deserialize<Dictionary<string, WpfRecord>>(File.ReadAllText(src));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        if (wpf == null) return 0;

        var file = new HomeworkFile();
        var lastDaily = KstClock.LastDailyReset(nowUtc);
        var lastWeekly = KstClock.LastWeeklyReset(nowUtc);
        file.Account.LastDailyResetUtc = lastDaily;
        file.Account.LastWeeklyResetUtc = lastWeekly;
        var imported = 0;

        foreach (var (key, rec) in wpf)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Equals("Default_Player", StringComparison.OrdinalIgnoreCase) || rec?.Items == null) continue;

            var ledger = new HomeworkLedger { LastDailyResetUtc = lastDaily, LastWeeklyResetUtc = lastWeekly };
            foreach (var (id, st) in rec.Items)
            {
                var def = catalog.Find(id);
                if (def == null || st == null || !st.IsCompleted || st.IsAutoDetected || st.CompletedAt == null) continue;

                // WPF는 PC 로컬 시각(DateTime)으로 저장했다
                var completedUtc = new DateTimeOffset(DateTime.SpecifyKind(st.CompletedAt.Value, DateTimeKind.Local)).ToUniversalTime();
                if (completedUtc < KstClock.LastReset(def.Period, nowUtc)) continue;

                var target = def.Share == HomeworkShare.Account ? file.Account : ledger;
                var existing = target.Items.GetValueOrDefault(id);
                if (existing?.CompletedAtUtc >= completedUtc) continue;

                target.Items[id] = new HomeworkItemState
                {
                    Completed = true,
                    Count = Math.Max(st.CurrentCount, def.Goal),
                    CompletedAtUtc = completedUtc,
                    Auto = false,
                    ManualOverride = true,
                };
            }
            file.Characters[key] = ledger;
            imported++;
        }

        return Save(file) ? imported : 0;
    }

    private sealed class WpfRecord
    {
        public Dictionary<string, WpfItem>? Items { get; set; }
    }

    private sealed class WpfItem
    {
        public bool IsCompleted { get; set; }
        public int CurrentCount { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool IsAutoDetected { get; set; }
    }
}
