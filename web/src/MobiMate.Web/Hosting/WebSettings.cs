namespace MobiMate.Web.Hosting;

/// <summary>web_settings.json (FR-ST-02). 서버 공통 설정. 기기별 설정(테마·글자 크기)은 브라우저에 둔다.</summary>
public sealed class WebSettings
{
    public string? AiEngineId { get; set; }
    public int MaxRefreshSec { get; set; } = 30;
    /// <summary>
    /// 자동 이모티콘(문장 끝 이모지 + 행동 명령) 기본값. **기본 끔** (2026-11-06 승운): 지금의 규칙은 키워드에 따라 같은 이모지(😊)를 붙일 뿐이라
    /// 맥락에 맞지 않고 의미가 없다. 게임이 채팅마다 확인창을 띄우는데 행동 명령은 확인창을 한 번 더 만든다.
    /// 맥락에 맞는 이모티콘 추천은 음성 입력 시험이 끝난 뒤에 추가한다.
    /// </summary>
    public bool AutoEmoteDefault { get; set; }

    /// <summary>
    /// 맥락 이모티콘 (승운 2026-11-07): 자동 이모티콘이 켜져 있을 때 키워드 규칙 대신 AI가 문장의 감정·맥락에 맞는 이모지를 고른다.
    /// 감정이 뚜렷하지 않은 문장(모집·공략·지시)에는 이모지를 붙이지 않는다. 기본 끔.
    /// </summary>
    public bool ChatSmartEmote { get; set; }

    /// <summary>페르소나 말투 (승운 2026-11-07): 보내기 전에 현재 아무말 페르소나의 말투로 문장을 바꾼다. 뜻이 바뀌면 원문을 보낸다. 기본 끔.</summary>
    public bool ChatPersonaTone { get; set; }

    /// <summary>말투·이모지 AI 모델. 비우면 음성 다듬기 모델(기본 gemma4:e4b)을 쓴다</summary>
    public string? ChatStyleModel { get; set; }

    /// <summary>말투·이모지가 이 시간(ms) 안에 끝나지 않으면 포기하고 원문을 보낸다</summary>
    public int ChatStyleMs { get; set; } = 3000;
    public string? CliPath { get; set; }
    public bool LanEnabled { get; set; }

    /// <summary>현재 아무말 페르소나: 기본 5종 이름 또는 "custom:&lt;id&gt;" (FR-MB-15, 기기 사이 공유)</summary>
    public string ChatterPersona { get; set; } = "Villainess";

    // ── 음성 입력 (FR-VC) ──
    /// <summary>확정 즉시 게임 채팅으로 보낸다 (기본 켬 — 승운 2026-11-06: 게임의 채팅 확인창이 최종 문장을 보여 주고 Space로 보내거나 ESC로 취소하므로 그 확인창이 안전장치다)</summary>
    public bool SttAutoSend { get; set; } = true;

    /// <summary>한 번 듣는 최대 시간(초)</summary>
    public int SttMaxSeconds { get; set; } = 15;

    /// <summary>마이크 고유 ID. 없으면 시스템 기본 마이크</summary>
    public string? SttDeviceId { get; set; }

    /// <summary>인식 스레드 수 (기본 1: 게임 부하를 줄인다, FR-VC-06)</summary>
    public int SttThreads { get; set; } = 1;

    /// <summary>전역 단축키 (예: "Ctrl+Alt+V", "F9"). 없으면 단축키 없음</summary>
    public string? SttHotkey { get; set; } = "LCtrl+Space";   // 승운 2026-11-06 기본: 왼쪽 Ctrl+Space

    /// <summary>"hold"(누르는 동안 듣기, 기본) 또는 "toggle"(눌러서 켜고 끄기)</summary>
    public string SttHotkeyMode { get; set; } = "toggle";

    /// <summary>듣기 시작·문장 확정 신호음 (게임 전체 화면에서 소리로 알린다)</summary>
    public bool SttCue { get; set; } = true;

    /// <summary>실험: 자주 쓰는 낱말(3회 이상)을 인식기의 핫워드로 넣는다. 기본 끔 — P0 측정에서 일부 낱말만 고쳐졌고 점수를 올리면 문장이 깨졌다. 인식기를 만들 때 한 번 읽으므로 앱을 다시 켜야 적용된다</summary>
    public bool SttHotwords { get; set; } = true;

    /// <summary>인식 글자에 띄어쓰기를 넣어 본다 (FR-VC-09). 기본 끔 — 처음 보는 문장에서는 정확도가 높지 않아(경계 정밀도 약 83%) 선택 기능이다</summary>
    public bool SttSpacing { get; set; } = true;
    /// <summary>지난 음성 기록 보관 (FR-VC-16): 켜면 인식한 문장을 최근 100개·7일만 이 PC에 둔다</summary>
    public bool SttHistory { get; set; } = true;

    /// <summary>
    /// 띄어쓰기 강도 0~2 (0 보수적 · 1 보통 · 2 적극적, 기본 2). 높을수록 공백을 더 많이 넣는다 — 놓치는 공백은 줄고 틀린 공백은 조금 는다.
    /// 게임이 채팅마다 확인창에 최종 문장을 보여 주므로 사용자가 보고 취소할 수 있어, 놓치는 쪽을 줄이는 쪽을 기본으로 한다 (2026-11-06 승운).
    /// 측정(보류 5겹 교차): 보수적 정밀도 85.1%·재현율 55.8%, 보통 83.3%·59.7%, 적극적 80.1%·62.4%.
    /// </summary>
    public int SttSpacingLevel { get; set; } = 2;

    /// <summary>
    /// AI로 다듬기 (승운 2026-11-07): 인식 결과를 보내기 전에 이 PC의 Ollama가 잘못 알아들은 낱말과 띄어쓰기를 고친다.
    /// 기본 켬 — Ollama가 없거나 느리면 알아서 건너뛰고 사전 방식으로 보낸다. 게임 확인창이 마지막 안전장치다.
    /// </summary>
    public bool SttRefine { get; set; } = true;

    /// <summary>교정 모델. 비우면 gemma4:e4b (측정: 내용 29/30, 중앙값 0.17초). 이름이 cloud로 끝나면 말한 내용이 Ollama 클라우드로 나간다.</summary>
    public string? SttRefineModel { get; set; }

    /// <summary>교정이 이 시간(ms) 안에 끝나지 않으면 포기하고 사전 방식으로 보낸다</summary>
    public int SttRefineMs { get; set; } = 2500;
}

public sealed class WebSettingsStore
{
    public const string FileName = "web_settings.json";
    private readonly JsonFileStore _store = new();
    private readonly string _path;
    private readonly object _lock = new();
    private WebSettings _current;

    public WebSettingsStore(string storageDir)
    {
        _path = Path.Combine(storageDir, FileName);
        _current = _store.Load<WebSettings>(_path) ?? new WebSettings();
    }

    public WebSettings Current
    {
        get { lock (_lock) return Clone(_current); }
    }

    /// <summary>변경을 적용하고 저장한다. 저장에 실패하면 false이고 메모리 값도 바꾸지 않는다.</summary>
    public bool Update(Action<WebSettings> change)
    {
        lock (_lock)
        {
            var next = Clone(_current);
            change(next);
            if (!_store.Save(_path, next)) return false;
            _current = next;
            return true;
        }
    }

    private static WebSettings Clone(WebSettings s) => new()
    {
        AiEngineId = s.AiEngineId,
        MaxRefreshSec = s.MaxRefreshSec,
        AutoEmoteDefault = s.AutoEmoteDefault,
        CliPath = s.CliPath,
        LanEnabled = s.LanEnabled,
        ChatterPersona = s.ChatterPersona,
        SttAutoSend = s.SttAutoSend,
        SttMaxSeconds = s.SttMaxSeconds,
        SttDeviceId = s.SttDeviceId,
        SttThreads = s.SttThreads,
        SttHotkey = s.SttHotkey,
        SttHotkeyMode = s.SttHotkeyMode,
        SttCue = s.SttCue,
        SttHotwords = s.SttHotwords,
        SttSpacing = s.SttSpacing,
        SttHistory = s.SttHistory,
        SttSpacingLevel = s.SttSpacingLevel,
        SttRefine = s.SttRefine,
        ChatSmartEmote = s.ChatSmartEmote,
        ChatPersonaTone = s.ChatPersonaTone,
        ChatStyleModel = s.ChatStyleModel,
        ChatStyleMs = s.ChatStyleMs,
        SttRefineModel = s.SttRefineModel,
        SttRefineMs = s.SttRefineMs,
    };
}
