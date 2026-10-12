namespace MobiMateHomeWork.Hosting;

/// <summary>
/// 앱 설정 (구성 섹션 "MobiMate"). 환경변수 <c>MobiMate__StorageDir</c> 같은 형태나 실행 인자(<c>--storage</c>, <c>--cli</c>)로 덮어쓴다.
/// 주기는 초 단위 숫자이고 0이면 해당 동작을 끈다(검증·테스트용). 시간 값을 TimeSpan 문자열로 받지 않는 이유:
/// "10"이 10일로 해석되는 실수를 막기 위해서다.
/// </summary>
public sealed class AppOptions
{
    public const string Section = "MobiMate";

    /// <summary>저장 폴더. 기본 %APPDATA%\MobiMateWeb (웹앱과 공유). 검증할 때는 반드시 임시 폴더로 바꾼다.</summary>
    public string StorageDir { get; set; } = SnapshotManager.DefaultStorageDirectory;

    /// <summary>WPF판 저장 폴더(가져오기 원본, 읽기 전용). 없는 폴더면 가져오지 않는다.</summary>
    public string? WpfStorageDir { get; set; } = SnapshotManager.WpfStorageDirectory;

    /// <summary>게임 CLI 경로. 비우면 homework_app.json → web_settings.json(읽기만) → 환경변수 → 기본 경로 순으로 정한다.</summary>
    public string? CliPath { get; set; }

    /// <summary>단일 인스턴스. 호스트를 만들기 전에 판단하므로 환경변수·실행 인자로만 끈다(테스트).</summary>
    public bool SingleInstance { get; set; } = true;

    /// <summary>시작할 때 옛 웹앱(공유 저장 패치 이전)이 떠 있는지 확인한다. 테스트에서 끈다.</summary>
    public bool LegacyWebCheck { get; set; } = true;

    /// <summary>게임 상태(status) 확인 주기. 0이면 끈다.</summary>
    public double StatusIntervalSeconds { get; set; } = 10;

    /// <summary>전투력이 낮게 읽혔을 때 다시 읽기까지의 간격. 최대 두 번 다시 읽는다.</summary>
    public double CombatRecheckDelaySeconds { get; set; } = 5;

    /// <summary>숙제 관찰(퀘스트·미션·증표 읽기, CLI 9회) 주기. 접속 중일 때만 돈다. 0이면 주기 관찰을 끈다(맵 이동 트리거는 따로).</summary>
    public double HomeworkObserveIntervalSeconds { get; set; } = 20;

    /// <summary>맵 이동·로딩 복귀를 알아챈 뒤 숙제를 관찰하는 시점(초). 0이면 그 시점은 건너뛴다.</summary>
    public double HomeworkTriggerFirstDelaySeconds { get; set; } = 2;
    public double HomeworkTriggerSecondDelaySeconds { get; set; } = 8;

    /// <summary>접속 중인 캐릭터를 읽어 기록하는 간격(숙제 관찰은 따로 돈다). 0이면 끈다.</summary>
    public double RecordIntervalSeconds { get; set; } = 60;

    /// <summary>캐릭터 전환 감시 간격. 0이면 끈다.</summary>
    public double WatchIntervalSeconds { get; set; } = 4;

    /// <summary>같은 저장 폴더를 쓰는 다른 프로그램(웹앱)의 변경을 감지한다. 끄면 감시와 폴링을 모두 하지 않는다.</summary>
    public bool ExternalWatch { get; set; } = true;

    /// <summary>외부 변경 이벤트를 묶는 시간(초). 파일이 여러 번 바뀌어도 마지막 이벤트 뒤 한 번만 처리한다.</summary>
    public double ExternalWatchDebounceSeconds { get; set; } = 0.3;

    /// <summary>폴더 감시가 끊겨도 변경을 따라가기 위한 폴링 간격. 0이면 폴링을 끈다.</summary>
    public double ExternalWatchPollSeconds { get; set; } = 10;

    /// <summary>파일 로그 최소 수준. 기본 Information, 환경변수 MobiMate__LogLevel=Debug 로 Debug까지 쓴다.</summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>없는 직업 이미지를 첫 실행 때 내려받는다(공유 저장 폴더 class-images). 테스트에서 끈다.</summary>
    public bool DownloadImages { get; set; } = true;

    /// <summary>스냅숏 전용: 이 폴더의 class-images 하위 폴더 파일을 (임시) 저장 폴더로 복사해 쓴다. 원본 폴더는 읽기만 한다.</summary>
    public string? ClassImagesSource { get; set; }

    /// <summary>조회 캐시 유지 시간(초).</summary>
    public double QueryCacheTtlSeconds { get; set; } = 3;

    /// <summary>숨은 옵션 <c>--render-snapshots &lt;폴더&gt;</c>: 창을 크기별로 그려 PNG로 저장하고 끝난다(화면 확인용). <c>--storage</c>를 반드시 함께 준다.</summary>
    public string? RenderSnapshots { get; set; }

    public Microsoft.Extensions.Logging.LogLevel MinLogLevel =>
        Enum.TryParse<Microsoft.Extensions.Logging.LogLevel>(LogLevel, true, out var l) && l is >= Microsoft.Extensions.Logging.LogLevel.Trace and <= Microsoft.Extensions.Logging.LogLevel.Error
            ? l : Microsoft.Extensions.Logging.LogLevel.Information;

    public TimeSpan StatusInterval => Span(StatusIntervalSeconds);
    public TimeSpan CombatRecheckDelay => Span(CombatRecheckDelaySeconds);
    public TimeSpan HomeworkObserveInterval => Span(HomeworkObserveIntervalSeconds);
    public TimeSpan HomeworkTriggerFirstDelay => Span(HomeworkTriggerFirstDelaySeconds);
    public TimeSpan HomeworkTriggerSecondDelay => Span(HomeworkTriggerSecondDelaySeconds);
    public TimeSpan RecordInterval => Span(RecordIntervalSeconds);
    public TimeSpan WatchInterval => Span(WatchIntervalSeconds);
    public TimeSpan ExternalWatchDebounce => Span(ExternalWatchDebounceSeconds);
    public TimeSpan ExternalWatchPoll => Span(ExternalWatchPollSeconds);
    public TimeSpan QueryCacheTtl => Span(QueryCacheTtlSeconds);

    private static TimeSpan Span(double seconds) => seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;

    /// <summary>실행 인자 → 구성 키 대응 (<c>--storage &lt;dir&gt;</c>, <c>--cli &lt;path&gt;</c> 등).</summary>
    public static readonly IReadOnlyDictionary<string, string> SwitchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["--storage"] = $"{Section}:{nameof(StorageDir)}",
        ["--cli"] = $"{Section}:{nameof(CliPath)}",
        ["--single-instance"] = $"{Section}:{nameof(SingleInstance)}",
        ["--status-interval"] = $"{Section}:{nameof(StatusIntervalSeconds)}",
        ["--record-interval"] = $"{Section}:{nameof(RecordIntervalSeconds)}",
        ["--homework-interval"] = $"{Section}:{nameof(HomeworkObserveIntervalSeconds)}",
        ["--watch-interval"] = $"{Section}:{nameof(WatchIntervalSeconds)}",
        ["--class-images-source"] = $"{Section}:{nameof(ClassImagesSource)}",
        ["--download-images"] = $"{Section}:{nameof(DownloadImages)}",
        ["--render-snapshots"] = $"{Section}:{nameof(RenderSnapshots)}",
        ["--wpf-storage"] = $"{Section}:{nameof(WpfStorageDir)}",
    };
}
